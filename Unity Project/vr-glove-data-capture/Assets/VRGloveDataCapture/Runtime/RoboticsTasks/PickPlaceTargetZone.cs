using UnityEngine;

namespace VRGloveDataCapture.RoboticsTasks
{
    /// <summary>Detects when the matching task object has entered its placement target.</summary>
    public sealed class PickPlaceTargetZone : MonoBehaviour
    {
        [SerializeField] private string taskId;
        [SerializeField] private PickPlaceTaskSceneController controller;
        [SerializeField] private Renderer indicator;
        [SerializeField] private Color readyColor;
        private bool completed;

        public string TaskId { get { return taskId; } }

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

        private void Awake()
        {
            if (controller == null)
            {
                controller = GetComponentInParent<PickPlaceTaskSceneController>();
            }

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
            if (indicator == null)
            {
                return;
            }

            // Runtime targets need independent color state; edit-time scene
            // generation must not instantiate and leak temporary materials.
            Material material = Application.isPlaying ? indicator.material : indicator.sharedMaterial;
            if (material != null)
            {
                material.color = color;
            }
        }
    }
}
