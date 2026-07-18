using UnityEngine;

namespace VRGloveDataCapture.RoboticsTasks
{
    /// <summary>Stores the deterministic reset pose and task identity of a graspable object.</summary>
    public sealed class PickPlaceTaskObject : MonoBehaviour
    {
        [SerializeField] private string taskId;
        [SerializeField] private int hi5ObjectId;
        [SerializeField] private string hi5ObjectName;

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
        }

        private void OnTransformParentChanged()
        {
            UpdatePhysicsMode();
        }

        private void FixedUpdate()
        {
            UpdatePhysicsMode();
        }

        private void UpdatePhysicsMode()
        {
            if (body == null)
            {
                body = GetComponent<Rigidbody>();
            }
            if (body == null)
            {
                return;
            }

            bool heldByHi5Hand = IsUnderHi5Hand(transform.parent);
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
            if (wasHeldByHi5Hand && !heldByHi5Hand)
            {
                body.WakeUp();
            }
            wasHeldByHi5Hand = heldByHi5Hand;
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
    }
}
