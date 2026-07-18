using System;
using UnityEngine;

namespace VRGloveDataCapture.Capture
{
    /// <summary>
    /// Provides a scene-independent F9 recording request while running in the
    /// Unity Editor. The actual MP4 encoder lives in the Editor assembly because
    /// Unity Recorder 2.2 is an editor-only package.
    /// </summary>
    public sealed class VrViewRecordingHotkey : MonoBehaviour
    {
        private const string ObjectName = "[VR Glove VR View Recording Hotkey]";

        public static event Action ToggleRequested;
        public static event Action StateChanged;

        public static bool IsRecording { get; private set; }
        public static string Status { get; private set; } = "VR view recording is idle.";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            GameObject hotkey = new GameObject(ObjectName);
            hotkey.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(hotkey);
            hotkey.AddComponent<VrViewRecordingHotkey>();
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.F9))
            {
                return;
            }

            RequestToggle();
        }

        /// <summary>
        /// Uses the same editor recording path as F9. Runtime UI and future
        /// SteamVR actions call this method instead of synthesizing key input.
        /// </summary>
        public static bool RequestToggle()
        {
            Action handler = ToggleRequested;
            if (handler != null)
            {
                handler.Invoke();
                return true;
            }

            ReportState(false, "VR recording is available in Unity Editor Play Mode only.");
            Debug.LogWarning("[VRGloveDataCapture] " + Status);
            return false;
        }

        /// <summary>Called by the editor-only Recorder bridge to publish UI state.</summary>
        public static void ReportState(bool isRecording, string status)
        {
            IsRecording = isRecording;
            Status = string.IsNullOrEmpty(status)
                ? (isRecording ? "VR view recording is active." : "VR view recording is idle.")
                : status;

            Action handler = StateChanged;
            if (handler != null)
            {
                handler.Invoke();
            }
        }
    }
}
