using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VRGloveDataCapture.RoboticsTasks
{
    /// <summary>Stores the deterministic reset pose and task identity of a graspable object.</summary>
    public sealed class PickPlaceTaskObject : MonoBehaviour
    {
        private const string VisibleHandTypeName = "Hi5_Interaction_Core.Hi5_Hand_Visible_Hand";
        private const string InteractionHandTypeName = "Hi5_Interaction_Core.Hi5_Glove_Interaction_Hand";

        [SerializeField] private string taskId;
        [SerializeField] private int hi5ObjectId;
        [SerializeField] private string hi5ObjectName;

        [Header("Natural release")]
        [SerializeField, Min(0.0f)]
        private float pinch2OpeningDistance = 0.018f;

        [SerializeField, Min(0.0f)]
        private float pinch2MinimumReleaseSeparation = 0.045f;

        [SerializeField, Min(0.0f)]
        private float pinch2MaximumReleaseSeparation = 0.065f;

        [SerializeField, Min(0.0f)]
        private float pinch2ReleaseSustainTime = 0.06f;

        [SerializeField, Min(0.0f)]
        private float velocitySampleResponseHz = 12.0f;

        [SerializeField, Min(0.0f)]
        private float maximumReleaseLinearSpeed = 3.5f;

        [SerializeField, Min(0.0f)]
        private float maximumReleaseAngularSpeed = 18.0f;

        [SerializeField, Min(0.0f)]
        private float releasedHandCollisionGrace = 0.12f;

        [SerializeField, Min(0.0f)]
        private float releasedHandCollisionTimeout = 0.35f;

        public string TaskId { get { return taskId; } }
        public int Hi5ObjectId { get { return hi5ObjectId; } }
        public string Hi5ObjectName { get { return hi5ObjectName; } }

        private Transform startParent;
        private Vector3 startPosition;
        private Quaternion startRotation;
        private Vector3 startScale;
        private Rigidbody body;
        private bool hasStartPose;
        private bool wasHeldByHi5Hand;
        private bool suppressPhysicsTransitions;

        private bool hasHeldMotionSample;
        private float lastHeldMotionSampleTime;
        private Vector3 lastHeldPosition;
        private Quaternion lastHeldRotation;
        private Vector3 filteredHeldLinearVelocity;
        private Vector3 filteredHeldAngularVelocity;

        private Component trackedPinch2Hand;
        private bool hasPinch2Baseline;
        private float pinch2BaselineSeparation;
        private float pinch2ReleaseCandidateTime;

        private readonly List<IgnoredCollisionPair> ignoredHandCollisions =
            new List<IgnoredCollisionPair>(64);
        private bool collisionRestorePending;
        private float collisionRestoreNotBefore;
        private float collisionRestoreDeadline;

        private sealed class IgnoredCollisionPair
        {
            public Collider ObjectCollider;
            public Collider HandCollider;
        }

        internal void Configure(string configuredTaskId, int objectId, string objectName)
        {
            taskId = configuredTaskId;
            hi5ObjectId = objectId;
            hi5ObjectName = objectName;
            body = GetComponent<Rigidbody>();
        }

        internal bool BindToHi5()
        {
            if (string.IsNullOrEmpty(taskId) || string.IsNullOrEmpty(hi5ObjectName))
            {
                Debug.LogError("[PickPlaceTasks] " + name + " has incomplete serialized task metadata.", this);
                return false;
            }

            if (Hi5RuntimeBridge.HasSimpleObjectComponents(gameObject))
            {
                return true;
            }

            bool wasActive = gameObject.activeSelf;
            gameObject.SetActive(false);
            bool configured = Hi5RuntimeBridge.AddSimpleObjectComponents(gameObject, hi5ObjectId, hi5ObjectName);
            gameObject.SetActive(wasActive);
            if (configured && wasActive &&
                !Hi5RuntimeBridge.IsRegisteredSimpleObject(gameObject, hi5ObjectId))
            {
                Debug.LogError(
                    "[PickPlaceTasks] Hi5 object id " + hi5ObjectId +
                    " is already owned by another object; disabling duplicate " + name + ".",
                    this);
                gameObject.SetActive(false);
                return false;
            }

            UpdatePhysicsMode();
            return configured;
        }

        internal void CaptureStartPose()
        {
            startParent = transform.parent;
            startPosition = transform.position;
            startRotation = transform.rotation;
            startScale = transform.localScale;
            hasStartPose = true;
        }

        internal void ResetToStart()
        {
            if (!hasStartPose)
            {
                return;
            }

            if (body == null)
            {
                body = GetComponent<Rigidbody>();
            }

            suppressPhysicsTransitions = true;
            ResetPinch2Watchdog();
            RestoreIgnoredHandCollisions();
            ResetHeldMotion();
            wasHeldByHi5Hand = false;

            if (body != null)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                body.isKinematic = true;
            }

            transform.SetParent(startParent, true);
            transform.position = startPosition;
            transform.rotation = startRotation;
            transform.localScale = startScale;

            if (body != null)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.useGravity = true;
                body.isKinematic = false;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.WakeUp();
            }

            suppressPhysicsTransitions = false;
        }

        private void OnDisable()
        {
            RestoreIgnoredHandCollisions();
            ResetPinch2Watchdog();
            ResetHeldMotion();
            wasHeldByHi5Hand = false;
        }

        private void OnDestroy()
        {
            RestoreIgnoredHandCollisions();
        }

        private void OnTransformParentChanged()
        {
            UpdatePhysicsMode();
        }

        private void FixedUpdate()
        {
            UpdatePhysicsMode();
            TryRestoreReleasedHandCollisions();
        }

        private void LateUpdate()
        {
            if (wasHeldByHi5Hand)
            {
                SampleHeldMotion(Time.unscaledTime);
                UpdatePinch2ReleaseWatchdog();
            }
            else
            {
                ResetPinch2Watchdog();
                TryRestoreReleasedHandCollisions();
            }
        }

        private void UpdatePhysicsMode()
        {
            if (suppressPhysicsTransitions)
            {
                return;
            }

            if (body == null)
            {
                body = GetComponent<Rigidbody>();
            }
            if (body == null)
            {
                return;
            }

            bool heldByHi5Hand = IsUnderHi5Hand(transform.parent);
            bool startedHolding = heldByHi5Hand && !wasHeldByHi5Hand;
            bool stoppedHolding = !heldByHi5Hand && wasHeldByHi5Hand;
            if (startedHolding)
            {
                BeginHeldState(transform.parent);
            }

            body.useGravity = true;
            if (heldByHi5Hand && !body.isKinematic)
            {
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }
            body.isKinematic = heldByHi5Hand;
            if (!heldByHi5Hand)
            {
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }
            if (stoppedHolding)
            {
                ApplyReleaseVelocity();
                ScheduleReleasedHandCollisionRestore();
                body.WakeUp();
                ResetPinch2Watchdog();
            }
            wasHeldByHi5Hand = heldByHi5Hand;
        }

        private void BeginHeldState(Transform heldParent)
        {
            RestoreIgnoredHandCollisions();
            ResetPinch2Watchdog();
            ResetHeldMotion();
            hasHeldMotionSample = true;
            lastHeldMotionSampleTime = Time.unscaledTime;
            lastHeldPosition = transform.position;
            lastHeldRotation = transform.rotation;
            IgnoreGrabbingHandCollisions(heldParent);
        }

        private void SampleHeldMotion(float sampleTime)
        {
            if (!hasHeldMotionSample)
            {
                hasHeldMotionSample = true;
                lastHeldMotionSampleTime = sampleTime;
                lastHeldPosition = transform.position;
                lastHeldRotation = transform.rotation;
                return;
            }

            float deltaTime = sampleTime - lastHeldMotionSampleTime;
            if (deltaTime <= 0.0001f)
            {
                return;
            }

            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;
            Vector3 linearVelocity = (position - lastHeldPosition) / deltaTime;
            linearVelocity = Vector3.ClampMagnitude(
                linearVelocity,
                Mathf.Max(maximumReleaseLinearSpeed * 1.5f, maximumReleaseLinearSpeed));

            Quaternion rotationDelta = rotation * Quaternion.Inverse(lastHeldRotation);
            float angleDegrees;
            Vector3 axis;
            rotationDelta.ToAngleAxis(out angleDegrees, out axis);
            if (angleDegrees > 180.0f)
            {
                angleDegrees -= 360.0f;
            }

            Vector3 angularVelocity = Vector3.zero;
            if (axis.sqrMagnitude > 0.000001f && IsFinite(axis))
            {
                angularVelocity = axis.normalized * angleDegrees * Mathf.Deg2Rad / deltaTime;
                angularVelocity = Vector3.ClampMagnitude(
                    angularVelocity,
                    Mathf.Max(maximumReleaseAngularSpeed * 1.5f, maximumReleaseAngularSpeed));
            }

            float response = velocitySampleResponseHz <= 0.0f
                ? 1.0f
                : 1.0f - Mathf.Exp(-2.0f * Mathf.PI * velocitySampleResponseHz * deltaTime);
            filteredHeldLinearVelocity = Vector3.Lerp(
                filteredHeldLinearVelocity,
                linearVelocity,
                response);
            filteredHeldAngularVelocity = Vector3.Lerp(
                filteredHeldAngularVelocity,
                angularVelocity,
                response);

            lastHeldMotionSampleTime = sampleTime;
            lastHeldPosition = position;
            lastHeldRotation = rotation;
        }

        private void ApplyReleaseVelocity()
        {
            if (body == null || !hasHeldMotionSample)
            {
                ResetHeldMotion();
                return;
            }

            body.velocity = Vector3.ClampMagnitude(
                filteredHeldLinearVelocity,
                maximumReleaseLinearSpeed);
            body.angularVelocity = Vector3.ClampMagnitude(
                filteredHeldAngularVelocity,
                maximumReleaseAngularSpeed);
            ResetHeldMotion();
        }

        private void UpdatePinch2ReleaseWatchdog()
        {
            Component interactionHand;
            float separation;
            if (!Hi5RuntimeBridge.TryGetPinch2Separation(
                    transform.parent,
                    hi5ObjectId,
                    out interactionHand,
                    out separation))
            {
                ResetPinch2Watchdog();
                return;
            }

            if (!hasPinch2Baseline || trackedPinch2Hand != interactionHand)
            {
                trackedPinch2Hand = interactionHand;
                pinch2BaselineSeparation = separation;
                pinch2ReleaseCandidateTime = 0.0f;
                hasPinch2Baseline = true;
                return;
            }

            float releaseSeparation = Mathf.Clamp(
                pinch2BaselineSeparation + pinch2OpeningDistance,
                pinch2MinimumReleaseSeparation,
                Mathf.Max(pinch2MinimumReleaseSeparation, pinch2MaximumReleaseSeparation));
            if (separation >= releaseSeparation)
            {
                pinch2ReleaseCandidateTime += Mathf.Max(Time.unscaledDeltaTime, 0.0f);
            }
            else if (separation < releaseSeparation - 0.004f)
            {
                pinch2ReleaseCandidateTime = 0.0f;
            }

            if (pinch2ReleaseCandidateTime < pinch2ReleaseSustainTime)
            {
                return;
            }

            // Capture the latest palm-driven object motion immediately before
            // the synchronous vendor release message reparents the object.
            SampleHeldMotion(Time.unscaledTime);
            Component handToRelease = trackedPinch2Hand;
            if (Hi5RuntimeBridge.ForceReleasePinch2(handToRelease, hi5ObjectId))
            {
                Debug.Log(
                    "[PickPlaceTasks] Released " + name +
                    " through the Hi5 Pinch2 compatibility watchdog.",
                    this);
            }
            ResetPinch2Watchdog();
        }

        private void ResetPinch2Watchdog()
        {
            trackedPinch2Hand = null;
            hasPinch2Baseline = false;
            pinch2BaselineSeparation = 0.0f;
            pinch2ReleaseCandidateTime = 0.0f;
        }

        private void ResetHeldMotion()
        {
            hasHeldMotionSample = false;
            lastHeldMotionSampleTime = 0.0f;
            lastHeldPosition = Vector3.zero;
            lastHeldRotation = Quaternion.identity;
            filteredHeldLinearVelocity = Vector3.zero;
            filteredHeldAngularVelocity = Vector3.zero;
        }

        private void IgnoreGrabbingHandCollisions(Transform heldParent)
        {
            Collider[] objectColliders = GetComponentsInChildren<Collider>(true);
            if (objectColliders.Length == 0)
            {
                return;
            }

            HashSet<int> objectColliderIds = new HashSet<int>();
            for (int index = 0; index < objectColliders.Length; index++)
            {
                if (objectColliders[index] != null)
                {
                    objectColliderIds.Add(objectColliders[index].GetInstanceID());
                }
            }

            List<Collider> handColliders = new List<Collider>(64);
            HashSet<int> handColliderIds = new HashSet<int>();
            CollectGrabbingHandColliders(heldParent, handColliders, handColliderIds);
            for (int objectIndex = 0; objectIndex < objectColliders.Length; objectIndex++)
            {
                Collider objectCollider = objectColliders[objectIndex];
                if (objectCollider == null || !objectCollider.enabled)
                {
                    continue;
                }

                for (int handIndex = 0; handIndex < handColliders.Count; handIndex++)
                {
                    Collider handCollider = handColliders[handIndex];
                    if (handCollider == null || !handCollider.enabled ||
                        handCollider == objectCollider ||
                        objectColliderIds.Contains(handCollider.GetInstanceID()) ||
                        Physics.GetIgnoreCollision(objectCollider, handCollider))
                    {
                        continue;
                    }

                    Physics.IgnoreCollision(objectCollider, handCollider, true);
                    ignoredHandCollisions.Add(new IgnoredCollisionPair
                    {
                        ObjectCollider = objectCollider,
                        HandCollider = handCollider
                    });
                }
            }
        }

        private static void CollectGrabbingHandColliders(
            Transform heldParent,
            List<Collider> destination,
            HashSet<int> colliderIds)
        {
            for (Transform current = heldParent; current != null; current = current.parent)
            {
                Component[] components = current.GetComponents<Component>();
                for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    Component component = components[componentIndex];
                    if (component == null)
                    {
                        continue;
                    }

                    string typeName = component.GetType().FullName;
                    if (typeName == VisibleHandTypeName)
                    {
                        AddUniqueColliders(
                            component.GetComponentsInChildren<Collider>(true),
                            destination,
                            colliderIds);

                        FieldInfo gloveField = FindField(component.GetType(), "mGlove_Hand");
                        Component sourceRig = gloveField == null
                            ? null
                            : gloveField.GetValue(component) as Component;
                        if (sourceRig != null)
                        {
                            AddUniqueColliders(
                                sourceRig.GetComponentsInChildren<Collider>(true),
                                destination,
                                colliderIds);
                        }
                    }
                    else if (typeName == InteractionHandTypeName)
                    {
                        AddUniqueColliders(
                            component.GetComponentsInChildren<Collider>(true),
                            destination,
                            colliderIds);
                    }
                }
            }
        }

        private static void AddUniqueColliders(
            Collider[] colliders,
            List<Collider> destination,
            HashSet<int> colliderIds)
        {
            for (int index = 0; index < colliders.Length; index++)
            {
                Collider collider = colliders[index];
                if (collider != null && colliderIds.Add(collider.GetInstanceID()))
                {
                    destination.Add(collider);
                }
            }
        }

        private void ScheduleReleasedHandCollisionRestore()
        {
            if (ignoredHandCollisions.Count == 0)
            {
                collisionRestorePending = false;
                return;
            }

            float now = Time.unscaledTime;
            collisionRestoreNotBefore = now + releasedHandCollisionGrace;
            collisionRestoreDeadline = now + Mathf.Max(
                releasedHandCollisionGrace,
                releasedHandCollisionTimeout);
            collisionRestorePending = true;
        }

        private void TryRestoreReleasedHandCollisions()
        {
            if (!collisionRestorePending || wasHeldByHi5Hand ||
                Time.unscaledTime < collisionRestoreNotBefore)
            {
                return;
            }

            if (Time.unscaledTime >= collisionRestoreDeadline ||
                !AnyIgnoredPairOverlaps())
            {
                RestoreIgnoredHandCollisions();
            }
        }

        private bool AnyIgnoredPairOverlaps()
        {
            for (int index = 0; index < ignoredHandCollisions.Count; index++)
            {
                IgnoredCollisionPair pair = ignoredHandCollisions[index];
                Collider objectCollider = pair.ObjectCollider;
                Collider handCollider = pair.HandCollider;
                if (objectCollider == null || handCollider == null ||
                    !objectCollider.enabled || !handCollider.enabled ||
                    !objectCollider.bounds.Intersects(handCollider.bounds))
                {
                    continue;
                }

                Vector3 direction;
                float distance;
                if (Physics.ComputePenetration(
                        objectCollider,
                        objectCollider.transform.position,
                        objectCollider.transform.rotation,
                        handCollider,
                        handCollider.transform.position,
                        handCollider.transform.rotation,
                        out direction,
                        out distance))
                {
                    return true;
                }
            }

            return false;
        }

        private void RestoreIgnoredHandCollisions()
        {
            for (int index = 0; index < ignoredHandCollisions.Count; index++)
            {
                IgnoredCollisionPair pair = ignoredHandCollisions[index];
                if (pair.ObjectCollider != null && pair.HandCollider != null)
                {
                    Physics.IgnoreCollision(pair.ObjectCollider, pair.HandCollider, false);
                }
            }

            ignoredHandCollisions.Clear();
            collisionRestorePending = false;
            collisionRestoreNotBefore = 0.0f;
            collisionRestoreDeadline = 0.0f;
        }

        private static FieldInfo FindField(Type type, string fieldName)
        {
            const BindingFlags flags =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(fieldName, flags);
                if (field != null)
                {
                    return field;
                }
            }

            return null;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static bool IsUnderHi5Hand(Transform candidate)
        {
            for (Transform current = candidate; current != null; current = current.parent)
            {
                Component[] components = current.GetComponents<Component>();
                for (int index = 0; index < components.Length; index++)
                {
                    Component component = components[index];
                    string fullName = component == null ? string.Empty : component.GetType().FullName;
                    if (fullName == "Hi5_Interaction_Core.Hi5_Glove_Interaction_Hand" ||
                        fullName == "Hi5_Interaction_Core.Hi5_Hand_Visible_Hand" ||
                        fullName == "Hi5_Interaction_Core.Hi5_Hand_Palm" ||
                        fullName == "Hi5_Interaction_Core.Hi5_Glove_Collider_Palm")
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void OnValidate()
        {
            pinch2MaximumReleaseSeparation = Mathf.Max(
                pinch2MinimumReleaseSeparation,
                pinch2MaximumReleaseSeparation);
            releasedHandCollisionTimeout = Mathf.Max(
                releasedHandCollisionGrace,
                releasedHandCollisionTimeout);
        }
    }
}
