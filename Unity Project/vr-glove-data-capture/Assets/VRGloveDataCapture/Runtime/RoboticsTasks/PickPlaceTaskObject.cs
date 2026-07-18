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
                body.Sleep();
            }
        }
    }
}
