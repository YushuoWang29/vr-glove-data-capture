using UnityEngine;

namespace VRGloveDataCapture.RoboticsTasks
{
    /// <summary>
    /// Marks a project-owned task setup scene and declares the vendor scene that
    /// supplies the calibrated Hi5 rig, status UI, and reset controls.
    /// </summary>
    public sealed class TaskSetupSceneMarker : MonoBehaviour
    {
        public const string DefaultBaseScenePath =
            "Assets/Hi5_Interaction_SDK/Scenes/Vive/TableScene_Vive.unity";

        [SerializeField] private string setupName = "Task Setup";
        [SerializeField] private string baseScenePath = DefaultBaseScenePath;

        public string SetupName { get { return setupName; } }
        public string BaseScenePath { get { return baseScenePath; } }

        public void Configure(string configuredSetupName, string configuredBaseScenePath = DefaultBaseScenePath)
        {
            setupName = configuredSetupName;
            baseScenePath = configuredBaseScenePath;
        }
    }
}
