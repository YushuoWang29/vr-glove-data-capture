using UnityEngine;

namespace VRGloveDataCapture.RoboticsTasks
{
    /// <summary>Detects when the matching task object has entered its placement target.</summary>
    public sealed class PickPlaceTargetZone : MonoBehaviour
    {
        private string taskId;
        private PickPlaceTaskSceneController controller;
        private Renderer indicator;
        private Color readyColor;
        private bool completed;

        internal void Configure(
            string targetTaskId,
            PickPlaceTaskSceneController taskController,
            Renderer targetIndicator,
            Color targetReadyColor)
        {
            taskId = targetTaskId;
            controller = taskController;
            indicator = targetIndicator;
            readyColor = targetReadyColor;
            ApplyColor(readyColor);
        }

        internal void ResetTarget()
        {
            completed = false;
            ApplyColor(readyColor);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (completed)
            {
                return;
            }

            Rigidbody attachedBody = other.attachedRigidbody;
            PickPlaceTaskObject taskObject = attachedBody != null
                ? attachedBody.GetComponent<PickPlaceTaskObject>()
                : other.GetComponentInParent<PickPlaceTaskObject>();

            if (taskObject == null || taskObject.TaskId != taskId)
            {
                return;
            }

            completed = true;
            ApplyColor(new Color(0.2f, 0.9f, 0.32f, 1f));
            if (controller != null)
            {
                controller.MarkCompleted(taskId);
            }
        }

        private void ApplyColor(Color color)
        {
            if (indicator != null && indicator.material != null)
            {
                indicator.material.color = color;
            }
        }
    }
}
