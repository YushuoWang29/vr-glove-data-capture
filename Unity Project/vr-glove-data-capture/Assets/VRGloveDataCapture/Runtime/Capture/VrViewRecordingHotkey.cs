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

            Action handler = ToggleRequested;
            if (handler != null)
            {
                handler.Invoke();
            }
            else
            {
                Debug.LogWarning(
                    "[VRGloveDataCapture] F9 recording is available in Unity Editor Play Mode only.",
                    this);
            }
        }
    }
}
