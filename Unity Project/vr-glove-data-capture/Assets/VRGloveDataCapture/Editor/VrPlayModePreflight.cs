using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRGloveDataCapture.MixedReality;

namespace VRGloveDataCapture.Editor
{
    /// <summary>Prevents the VR scenes from entering Play before SteamVR is ready.</summary>
    [InitializeOnLoad]
    public static class VrPlayModePreflight
    {
        private const string SkipForTestsKey = "VRGloveDataCapture.SkipVrPreflightForTests";
        private const string AllowOnceKey = "VRGloveDataCapture.AllowDesktopPlayOnce";
        private const string VendorScenePath =
            "Assets/Hi5_Interaction_SDK/Scenes/Vive/TableScene_Vive.unity";
        private static bool dialogQueued;

        static VrPlayModePreflight()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem("Tools/VR Glove Data Capture/VR Runtime/Validate SteamVR and HMD", priority = 10)]
        public static void ValidateSteamVrAndHmd()
        {
            string detail;
            bool ready = SteamVrRuntimeProbe.TryGetReady(out detail);
            if (ready)
            {
                Debug.Log("[VRPreflight] " + detail);
                EditorUtility.DisplayDialog("VR runtime ready", detail, "OK");
            }
            else
            {
                Debug.LogError("[VRPreflight] " + detail);
                EditorUtility.DisplayDialog("VR runtime is not ready", BuildFailureMessage(detail), "OK");
            }
        }

        [MenuItem("Tools/VR Glove Data Capture/VR Runtime/Allow Desktop-Only Play Once", priority = 11)]
        public static void AllowDesktopOnlyPlayOnce()
        {
            SessionState.SetBool(AllowOnceKey, true);
            Debug.Log("[VRPreflight] The next Play Mode entry may run without a ready HMD.");
        }

        internal static void BeginAutomatedTestRun()
        {
            SessionState.SetBool(SkipForTestsKey, true);
        }

        internal static void EndAutomatedTestRun()
        {
            SessionState.SetBool(SkipForTestsKey, false);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode || Application.isBatchMode ||
                SessionState.GetBool(SkipForTestsKey, false) || !LoadedScenesRequireVr())
            {
                return;
            }

            if (SessionState.GetBool(AllowOnceKey, false))
            {
                SessionState.SetBool(AllowOnceKey, false);
                return;
            }

            string detail;
            if (SteamVrRuntimeProbe.TryGetReady(out detail))
            {
                Debug.Log("[VRPreflight] " + detail);
                return;
            }

            EditorApplication.isPlaying = false;
            Debug.LogError("[VRPreflight] Play Mode cancelled. " + detail);
            if (!dialogQueued)
            {
                dialogQueued = true;
                EditorApplication.delayCall += delegate
                {
                    dialogQueued = false;
                    EditorUtility.DisplayDialog(
                        "VR runtime is not ready - Play Mode cancelled",
                        BuildFailureMessage(detail),
                        "OK");
                };
            }
        }

        private static bool LoadedScenesRequireVr()
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                string path = SceneManager.GetSceneAt(index).path.Replace('\\', '/');
                if (path == TaskSetupSceneWorkspace.PickPlaceScenePath || path == VendorScenePath)
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildFailureMessage(string detail)
        {
            return detail + "\n\n" +
                   "处理顺序：\n" +
                   "1. 打开 SteamVR，并确认头显图标为绿色且状态为 Ready。\n" +
                   "2. 如果使用 VIVE Console，等待它完成连接。\n" +
                   "3. 回到 Unity，再次进入 Play Mode。\n\n" +
                   "如仅需无头显的桌面调试，可执行 Tools > VR Glove Data Capture > " +
                   "VR Runtime > Allow Desktop-Only Play Once。";
        }
    }
}
