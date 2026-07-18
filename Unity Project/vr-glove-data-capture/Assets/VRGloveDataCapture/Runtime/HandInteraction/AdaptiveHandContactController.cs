using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VRGloveDataCapture.HandInteraction
{
    /// <summary>
    /// Constrains only the final rendered Hi5 finger rig when the glove-requested
    /// pose would enter a rigidbody collider. It never writes to the Hi5 source
    /// skeleton, calibration state, grasp state machine, or capture streams.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(32500)]
    public sealed class AdaptiveHandContactController : MonoBehaviour
    {
        private const string VisibleHandTypeName = "Hi5_Interaction_Core.Hi5_Hand_Visible_Hand";
        private const int FingerCount = 5;
        private const int ControlledJointCount = 3;
        private const int ColliderBufferCapacity = 128;

        private static readonly string[] FingerNames =
        {
            "Thumb", "Index", "Middle", "Ring", "Pinky"
        };

        private static readonly string[] VisibleFingerFieldNames =
        {
            "m_ThumbFingerTransforms",
            "m_IndexFingerTransforms",
            "m_MiddleFingerTransforms",
            "m_RingFingerTransforms",
            "m_PinkyFingerTransforms"
        };

        // Hi5 visible-hand code maps these source bones to the first three
        // controllable visible joints. The in-palm metacarpals are intentionally
        // not altered here.
        private static readonly int[][] SourceBoneIndices =
        {
            new[] { 2, 3, 4 },
            new[] { 6, 7, 8 },
            new[] { 10, 11, 12 },
            new[] { 14, 15, 16 },
            new[] { 18, 19, 20 }
        };

        private static int lastPhysicsSyncFrame = -1;

        [Header("Visual contact")]
        [SerializeField]
        private bool applyVisualContact = true;

        [SerializeField]
        [Tooltip("Only colliders attached to a Rigidbody can constrain the hand. This keeps tables, UI volumes and scene geometry out of the visual solver by default.")]
        private bool requireRigidbody = true;

        [SerializeField]
        private LayerMask contactLayers = ~0;

        [SerializeField]
        [Min(0.05f)]
        private float broadphaseRadius = 0.28f;

        [Header("Finger surface approximation")]
        [SerializeField]
        [Range(0.1f, 0.45f)]
        private float radiusToBoneLength = 0.24f;

        [SerializeField]
        [Min(0.001f)]
        private float minimumFingerRadius = 0.0045f;

        [SerializeField]
        [Min(0.001f)]
        private float maximumFingerRadius = 0.0105f;

        [SerializeField]
        [Min(0.0f)]
        private float surfaceClearance = 0.0008f;

        [SerializeField]
        [Min(0.0f)]
        private float contactHysteresis = 0.0007f;

        [Header("Stability")]
        [SerializeField]
        [Range(3, 10)]
        private int solverIterations = 7;

        [SerializeField]
        [Min(0.0f)]
        private float releaseResponseHz = 18.0f;

        [SerializeField]
        [Tooltip("Synchronizes moved task-object colliders once per rendered frame before solving contact.")]
        private bool synchronizePhysicsTransforms = true;

        private readonly Collider[] colliderBuffer = new Collider[ColliderBufferCapacity];
        private readonly List<Collider> candidateColliders = new List<Collider>(16);
        private readonly HashSet<int> ignoredColliderIds = new HashSet<int>();
        private readonly Dictionary<int, bool> handColliderCache = new Dictionary<int, bool>();

        private Component vendorVisibleHand;
        private Component sourceRig;
        private FieldInfo sourceBonesField;
        private Transform[] sourceBones;
        private Transform handCenter;
        private FingerState[] fingers = new FingerState[0];
        private float nextBindAttempt;
        private string status = "Waiting for a Hi5 visible hand.";

        public bool ApplyVisualContact
        {
            get { return applyVisualContact; }
            set { applyVisualContact = value; }
        }

        public bool IsBound
        {
            get { return sourceRig != null && sourceBones != null && fingers.Length == FingerCount; }
        }

        public bool IsAnyFingerConstrained { get; private set; }

        public string Status
        {
            get { return status; }
        }

        public Transform VisualRigRoot
        {
            get { return transform; }
        }

        public Transform SourceRigRoot
        {
            get { return sourceRig == null ? null : sourceRig.transform; }
        }

        public Transform HandCenter
        {
            get { return handCenter; }
        }

        private sealed class FingerState
        {
            public string Name;
            public Transform[] Visible;
            public int[] SourceIndices;
            public readonly Quaternion[] Target = new Quaternion[ControlledJointCount];
            public readonly Quaternion[] Applied = new Quaternion[ControlledJointCount];
            public readonly Quaternion[] FreePose = new Quaternion[ControlledJointCount];
            public readonly Quaternion[] BasePose = new Quaternion[ControlledJointCount];
            public readonly Quaternion[] SolvedPose = new Quaternion[ControlledJointCount];
            public bool Initialized;
            public bool Constrained;
            public float ConstraintAmount;
        }

        private void OnEnable()
        {
            nextBindAttempt = 0.0f;
            TryBindFromGameObject();
        }

        private void OnDisable()
        {
            RestoreSourcePose();
        }

        private void LateUpdate()
        {
            if (!IsBound)
            {
                if (Time.unscaledTime >= nextBindAttempt)
                {
                    nextBindAttempt = Time.unscaledTime + 0.5f;
                    TryBindFromGameObject();
                }

                return;
            }

            if (!RefreshSourceBones() || !ReadAllTargetPoses())
            {
                status = "The Hi5 source skeleton is temporarily unavailable.";
                return;
            }

            if (!applyVisualContact)
            {
                RestoreSourcePose();
                return;
            }

            if (synchronizePhysicsTransforms && lastPhysicsSyncFrame != Time.frameCount)
            {
                Physics.SyncTransforms();
                lastPhysicsSyncFrame = Time.frameCount;
            }

            CollectCandidateColliders();
            IsAnyFingerConstrained = false;

            for (int fingerIndex = 0; fingerIndex < fingers.Length; fingerIndex++)
            {
                UpdateFinger(fingers[fingerIndex]);
                IsAnyFingerConstrained |= fingers[fingerIndex].Constrained;
            }
        }

        internal void Configure(Component visibleHand)
        {
            if (visibleHand == null || visibleHand.GetType().FullName != VisibleHandTypeName)
            {
                return;
            }

            if (vendorVisibleHand == visibleHand && IsBound)
            {
                return;
            }

            vendorVisibleHand = visibleHand;
            TryBind(visibleHand);
        }

        private void TryBindFromGameObject()
        {
            Component[] components = GetComponents<Component>();
            for (int index = 0; index < components.Length; index++)
            {
                Component component = components[index];
                if (component != null && component.GetType().FullName == VisibleHandTypeName)
                {
                    Configure(component);
                    return;
                }
            }
        }

        private void TryBind(Component visibleHand)
        {
            try
            {
                Type visibleType = visibleHand.GetType();
                FieldInfo sourceRigField = FindField(visibleType, "mGlove_Hand");
                FieldInfo handTransformField = FindField(visibleType, "handTransform");
                sourceRig = sourceRigField == null ? null : sourceRigField.GetValue(visibleHand) as Component;
                handCenter = handTransformField == null
                    ? transform
                    : handTransformField.GetValue(visibleHand) as Transform;
                if (handCenter == null)
                {
                    handCenter = transform;
                }

                if (sourceRig == null)
                {
                    status = "The Hi5 visible hand has not been linked to its source rig yet.";
                    return;
                }

                sourceBonesField = FindField(sourceRig.GetType(), "HandBones");
                if (!RefreshSourceBones())
                {
                    status = "The Hi5 source rig does not expose a complete HandBones array.";
                    return;
                }

                FingerState[] boundFingers = new FingerState[FingerCount];
                for (int fingerIndex = 0; fingerIndex < FingerCount; fingerIndex++)
                {
                    FieldInfo visibleFingerField = FindField(
                        visibleType,
                        VisibleFingerFieldNames[fingerIndex]);
                    IList visibleList = visibleFingerField == null
                        ? null
                        : visibleFingerField.GetValue(visibleHand) as IList;
                    if (visibleList == null || visibleList.Count < ControlledJointCount + 1)
                    {
                        status = "The visible " + FingerNames[fingerIndex] + " chain is incomplete.";
                        return;
                    }

                    Transform[] visibleTransforms = new Transform[ControlledJointCount + 1];
                    for (int jointIndex = 0; jointIndex < visibleTransforms.Length; jointIndex++)
                    {
                        visibleTransforms[jointIndex] = visibleList[jointIndex] as Transform;
                        if (visibleTransforms[jointIndex] == null)
                        {
                            status = "The visible " + FingerNames[fingerIndex] + " chain contains a null joint.";
                            return;
                        }
                    }

                    boundFingers[fingerIndex] = new FingerState
                    {
                        Name = FingerNames[fingerIndex],
                        Visible = visibleTransforms,
                        SourceIndices = SourceBoneIndices[fingerIndex]
                    };
                }

                fingers = boundFingers;
                RebuildIgnoredColliderSet();
                status = "Adaptive visual contact is active on " + gameObject.name + ".";
                Debug.Log("[AdaptiveHandContact] " + status, this);
            }
            catch (Exception exception)
            {
                sourceRig = null;
                sourceBones = null;
                fingers = new FingerState[0];
                status = "Could not bind the Hi5 visible hand: " + exception.Message;
                Debug.LogWarning("[AdaptiveHandContact] " + status, this);
            }
        }

        private bool RefreshSourceBones()
        {
            if (sourceRig == null || sourceBonesField == null)
            {
                return false;
            }

            sourceBones = sourceBonesField.GetValue(sourceRig) as Transform[];
            return sourceBones != null && sourceBones.Length > SourceBoneIndices[FingerCount - 1][ControlledJointCount - 1];
        }

        private bool ReadAllTargetPoses()
        {
            for (int fingerIndex = 0; fingerIndex < fingers.Length; fingerIndex++)
            {
                FingerState finger = fingers[fingerIndex];
                for (int jointIndex = 0; jointIndex < ControlledJointCount; jointIndex++)
                {
                    Transform source = sourceBones[finger.SourceIndices[jointIndex]];
                    if (source == null)
                    {
                        return false;
                    }

                    finger.Target[jointIndex] = source.localRotation;
                }
            }

            return true;
        }

        private void UpdateFinger(FingerState finger)
        {
            if (!finger.Initialized)
            {
                CopyRotations(finger.Target, finger.Applied);
                CopyRotations(finger.Target, finger.FreePose);
                ApplyRotations(finger, finger.Target);
                finger.Initialized = true;
            }

            if (candidateColliders.Count == 0)
            {
                ApplyRotations(finger, finger.Target);
                CopyRotations(finger.Target, finger.Applied);
                CopyRotations(finger.Target, finger.FreePose);
                finger.Constrained = false;
                finger.ConstraintAmount = 0.0f;
                return;
            }

            ApplyRotations(finger, finger.Target);
            float activeMargin = finger.Constrained ? contactHysteresis : 0.0f;
            bool targetPenetrates = HasSurfaceContact(finger, 0, activeMargin);
            if (!targetPenetrates)
            {
                CopyRotations(finger.Target, finger.FreePose);
                ReleaseTowardsTarget(finger);
                return;
            }

            ApplyRotations(finger, finger.Applied);
            if (HasSurfaceContact(finger, 0, 0.0f))
            {
                ApplyRotations(finger, finger.FreePose);
                if (HasSurfaceContact(finger, 0, 0.0f))
                {
                    // An object moved into both cached safe poses. Preserve the
                    // last rendered pose and wait for a collision-free target;
                    // never push a correction back into the source skeleton.
                    ApplyRotations(finger, finger.Applied);
                    finger.Constrained = true;
                    finger.ConstraintAmount = 1.0f;
                    return;
                }
            }

            CaptureVisibleRotations(finger, finger.BasePose);
            float maximumConstraint = 0.0f;
            for (int jointIndex = 0; jointIndex < ControlledJointCount; jointIndex++)
            {
                float safeFraction = SolveJointFraction(finger, jointIndex);
                maximumConstraint = Mathf.Max(maximumConstraint, 1.0f - safeFraction);
            }

            CaptureVisibleRotations(finger, finger.SolvedPose);
            ApplyRotations(finger, finger.SolvedPose);
            CopyRotations(finger.SolvedPose, finger.Applied);
            finger.Constrained = true;
            finger.ConstraintAmount = maximumConstraint;
        }

        private float SolveJointFraction(FingerState finger, int jointIndex)
        {
            Quaternion start = finger.Visible[jointIndex].localRotation;
            Quaternion target = finger.Target[jointIndex];
            if (Quaternion.Angle(start, target) < 0.001f)
            {
                return 0.0f;
            }

            float low = 0.0f;
            float high = 1.0f;
            for (int iteration = 0; iteration < solverIterations; iteration++)
            {
                float midpoint = (low + high) * 0.5f;
                finger.Visible[jointIndex].localRotation = Quaternion.Slerp(start, target, midpoint);
                if (HasSurfaceContact(finger, jointIndex, contactHysteresis))
                {
                    high = midpoint;
                }
                else
                {
                    low = midpoint;
                }
            }

            finger.Visible[jointIndex].localRotation = Quaternion.Slerp(start, target, low);
            return low;
        }

        private void ReleaseTowardsTarget(FingerState finger)
        {
            if (!finger.Constrained || releaseResponseHz <= 0.0f)
            {
                ApplyRotations(finger, finger.Target);
                CopyRotations(finger.Target, finger.Applied);
                finger.Constrained = false;
                finger.ConstraintAmount = 0.0f;
                return;
            }

            float response = 1.0f - Mathf.Exp(
                -2.0f * Mathf.PI * releaseResponseHz * Mathf.Max(Time.deltaTime, 0.0001f));
            float maximumAngle = 0.0f;
            for (int jointIndex = 0; jointIndex < ControlledJointCount; jointIndex++)
            {
                Quaternion rotation = Quaternion.Slerp(
                    finger.Applied[jointIndex],
                    finger.Target[jointIndex],
                    response);
                finger.Visible[jointIndex].localRotation = rotation;
                maximumAngle = Mathf.Max(maximumAngle, Quaternion.Angle(rotation, finger.Target[jointIndex]));
            }

            if (HasSurfaceContact(finger, 0, 0.0f))
            {
                // Both endpoints are safe but a curved interpolation path can
                // occasionally cross a collider. Prefer the known-safe glove pose.
                ApplyRotations(finger, finger.Target);
                maximumAngle = 0.0f;
            }

            CaptureVisibleRotations(finger, finger.Applied);
            finger.Constrained = maximumAngle > 0.1f;
            finger.ConstraintAmount = finger.Constrained
                ? Mathf.Clamp01(maximumAngle / 15.0f)
                : 0.0f;
        }

        private bool HasSurfaceContact(FingerState finger, int firstSegment, float extraMargin)
        {
            for (int segmentIndex = firstSegment; segmentIndex < ControlledJointCount; segmentIndex++)
            {
                Vector3 start = finger.Visible[segmentIndex].position;
                Vector3 end = finger.Visible[segmentIndex + 1].position;
                float length = Vector3.Distance(start, end);
                if (length < 0.0001f)
                {
                    continue;
                }

                float radius = Mathf.Clamp(
                    length * radiusToBoneLength,
                    minimumFingerRadius,
                    maximumFingerRadius) + surfaceClearance + extraMargin;
                float radiusSquared = radius * radius;

                for (int candidateIndex = 0; candidateIndex < candidateColliders.Count; candidateIndex++)
                {
                    Collider candidate = candidateColliders[candidateIndex];
                    if (candidate == null || !candidate.enabled)
                    {
                        continue;
                    }

                    // Three interior samples form a conservative capsule-like
                    // approximation without adding probe colliders or impulses.
                    if (PointTouchesCollider(Vector3.Lerp(start, end, 0.2f), candidate, radiusSquared) ||
                        PointTouchesCollider(Vector3.Lerp(start, end, 0.5f), candidate, radiusSquared) ||
                        PointTouchesCollider(Vector3.Lerp(start, end, 0.8f), candidate, radiusSquared))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool PointTouchesCollider(Vector3 point, Collider collider, float radiusSquared)
        {
            Vector3 closest = collider.ClosestPoint(point);
            return (closest - point).sqrMagnitude <= radiusSquared;
        }

        private void CollectCandidateColliders()
        {
            candidateColliders.Clear();
            if (handCenter == null)
            {
                return;
            }

            int count = Physics.OverlapSphereNonAlloc(
                handCenter.position,
                broadphaseRadius,
                colliderBuffer,
                contactLayers,
                QueryTriggerInteraction.Ignore);

            for (int index = 0; index < count; index++)
            {
                Collider candidate = colliderBuffer[index];
                colliderBuffer[index] = null;
                if (!IsCandidateCollider(candidate))
                {
                    continue;
                }

                candidateColliders.Add(candidate);
            }
        }

        private bool IsCandidateCollider(Collider candidate)
        {
            if (candidate == null || !candidate.enabled || candidate.isTrigger ||
                ignoredColliderIds.Contains(candidate.GetInstanceID()))
            {
                return false;
            }

            Rigidbody body = candidate.attachedRigidbody;
            if (requireRigidbody && body == null)
            {
                return false;
            }

            if (body != null &&
                (body.transform.IsChildOf(transform) ||
                 (sourceRig != null && body.transform.IsChildOf(sourceRig.transform))))
            {
                return false;
            }

            int instanceId = candidate.GetInstanceID();
            bool isHandCollider;
            if (!handColliderCache.TryGetValue(instanceId, out isHandCollider))
            {
                isHandCollider = BelongsToHi5Hand(candidate.transform);
                handColliderCache.Add(instanceId, isHandCollider);
            }

            return !isHandCollider;
        }

        private static bool BelongsToHi5Hand(Transform candidate)
        {
            Transform current = candidate;
            for (int depth = 0; current != null && depth < 16; depth++, current = current.parent)
            {
                Component[] components = current.GetComponents<Component>();
                for (int index = 0; index < components.Length; index++)
                {
                    Component component = components[index];
                    string typeName = component == null ? string.Empty : component.GetType().FullName;
                    if (typeName == "HI5.HI5_InertiaInstance" ||
                        typeName == "Hi5_Interaction_Core.Hi5_Glove_Interaction_Hand" ||
                        typeName == "Hi5_Interaction_Core.Hi5_Hand_Visible_Hand" ||
                        typeName == "Hi5_Interaction_Core.Hi5_Hand_Collider_Hand")
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void RebuildIgnoredColliderSet()
        {
            ignoredColliderIds.Clear();
            AddIgnoredColliders(gameObject);
            if (sourceRig != null)
            {
                AddIgnoredColliders(sourceRig.gameObject);
            }

            handColliderCache.Clear();
        }

        private void AddIgnoredColliders(GameObject root)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
            {
                ignoredColliderIds.Add(colliders[index].GetInstanceID());
            }
        }

        private void RestoreSourcePose()
        {
            if (!IsBound || !RefreshSourceBones() || !ReadAllTargetPoses())
            {
                return;
            }

            IsAnyFingerConstrained = false;
            for (int fingerIndex = 0; fingerIndex < fingers.Length; fingerIndex++)
            {
                FingerState finger = fingers[fingerIndex];
                ApplyRotations(finger, finger.Target);
                CopyRotations(finger.Target, finger.Applied);
                CopyRotations(finger.Target, finger.FreePose);
                finger.Initialized = true;
                finger.Constrained = false;
                finger.ConstraintAmount = 0.0f;
            }
        }

        private static void ApplyRotations(FingerState finger, Quaternion[] rotations)
        {
            for (int jointIndex = 0; jointIndex < ControlledJointCount; jointIndex++)
            {
                finger.Visible[jointIndex].localRotation = rotations[jointIndex];
            }
        }

        private static void CaptureVisibleRotations(FingerState finger, Quaternion[] destination)
        {
            for (int jointIndex = 0; jointIndex < ControlledJointCount; jointIndex++)
            {
                destination[jointIndex] = finger.Visible[jointIndex].localRotation;
            }
        }

        private static void CopyRotations(Quaternion[] source, Quaternion[] destination)
        {
            for (int index = 0; index < ControlledJointCount; index++)
            {
                destination[index] = source[index];
            }
        }

        private static FieldInfo FindField(Type type, string name)
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            Type current = type;
            while (current != null)
            {
                FieldInfo field = current.GetField(name, all);
                if (field != null)
                {
                    return field;
                }

                current = current.BaseType;
            }

            return null;
        }

        private void OnValidate()
        {
            maximumFingerRadius = Mathf.Max(minimumFingerRadius, maximumFingerRadius);
            broadphaseRadius = Mathf.Max(0.05f, broadphaseRadius);
            solverIterations = Mathf.Clamp(solverIterations, 3, 10);
        }
    }
}
