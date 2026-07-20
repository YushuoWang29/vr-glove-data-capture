using UnityEditor;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.XR.Management;

namespace VRGloveDataCapture.Editor
{
    /// <summary>
    /// Restores the Standalone XR settings singleton when this project's Unity 2019 Editor
    /// reaches Play Mode with that singleton unset after an Editor load or domain reload.
    /// This does not initialize OpenVR and therefore cannot create or close a native session.
    /// </summary>
    [InitializeOnLoad]
    internal static class OpenVrXrEditorSettingsBridge
    {
        static OpenVrXrEditorSettingsBridge()
        {
            RestoreStandaloneSettingsInstance();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void RestoreForPlayMode()
        {
            RestoreStandaloneSettingsInstance();
        }

        private static void RestoreStandaloneSettingsInstance()
        {
            if (XRGeneralSettings.Instance != null)
            {
                return;
            }

            XRGeneralSettings settings =
                XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (settings == null)
            {
                Debug.LogError("[XRBootstrap] Standalone XR settings could not be loaded from EditorBuildSettings.");
                return;
            }

            XRGeneralSettings.Instance = settings;
            Debug.Log("[XRBootstrap] Restored the Standalone XR settings instance because it was unset after an Editor load or domain reload.");
        }
    }
}
