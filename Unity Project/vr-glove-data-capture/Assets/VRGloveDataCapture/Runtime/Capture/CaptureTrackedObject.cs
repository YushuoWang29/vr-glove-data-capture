using UnityEngine;

namespace VRGloveDataCapture.Capture
{
    /// <summary>Add to non-task objects whose pose must be included in a capture trial.</summary>
    [DisallowMultipleComponent]
    public sealed class CaptureTrackedObject : MonoBehaviour
    {
        [SerializeField] private string objectId;
        [SerializeField] private string category = "scene_object";

        public string ObjectId
        {
            get
            {
                return CapturePathUtility.SanitizeSegment(
                    string.IsNullOrEmpty(objectId) ? gameObject.name : objectId,
                    "object");
            }
        }

        public string Category
        {
            get { return string.IsNullOrEmpty(category) ? "scene_object" : category; }
        }
    }
}
