using UnityEngine;

namespace VRGloveDataCapture.RoboticsTasks
{
    /// <summary>Stores the deterministic reset pose and task identity of a graspable object.</summary>
    public sealed class PickPlaceTaskObject : MonoBehaviour
    {
        public string TaskId { get; private set; }

        private Transform startParent;
        private Vector3 startPosition;
        private Quaternion startRotation;
        private Vector3 startScale;
        private Rigidbody body;
        private bool hasStartPose;

        internal void Configure(string taskId)
        {
            TaskId = taskId;
            body = GetComponent<Rigidbody>();
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
