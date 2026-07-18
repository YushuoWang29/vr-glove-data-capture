using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace VRGloveDataCapture.Editor
{
    /// <summary>Runs the project task-scene regression without opening Test Runner manually.</summary>
    [InitializeOnLoad]
    public static class PickPlaceTaskTestRunner
    {
        private const string SmokeTestName =
            "VRGloveDataCapture.Tests.PickPlaceTaskSceneSmokeTests." +
            "EditableTaskSceneBindsFiveTasksAndHi5ResetRestoresTheirPoses";
        private const string CaptureSmokeTestName =
            "VRGloveDataCapture.Tests.UnifiedCaptureSmokeTests." +
            "TrialFinalizesAtomicMachineReadableStreamsAndManifest";
        private const string AdaptiveContactSmokeTestName =
            "VRGloveDataCapture.Tests.AdaptiveHandContactSmokeTests." +
            "SolverAutoInstallsOnBothVisibleHandsWithoutWritingSourceBones";
        private const string GazeControlPanelSmokeTestName =
            "VRGloveDataCapture.Tests.GazeControlPanelSmokeTests." +
            "PanelPreservesCalibrationAndRoutesVendorGazeToProjectControls";

        private static readonly TestRunnerApi Api;

        static PickPlaceTaskTestRunner()
        {
            Api = ScriptableObject.CreateInstance<TestRunnerApi>();
            Api.RegisterCallbacks(ScriptableObject.CreateInstance<PickPlaceTestCallbacks>());
        }

        [MenuItem("Tools/VR Glove Data Capture/Task Setups/Run Pick Place Play Mode Test", priority = 140)]
        [Shortcut(
            "VR Glove Data Capture/Tests/Run Pick Place Play Mode Test",
            KeyCode.F7,
            ShortcutModifiers.Action | ShortcutModifiers.Shift)]
        public static void RunSmokeTest()
        {
            Api.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.PlayMode,
                testNames = new[] { SmokeTestName }
            }));

            Debug.Log("[PickPlaceTests] Started Play Mode smoke test: " + SmokeTestName);
        }

        [MenuItem("Tools/VR Glove Data Capture/Data Capture/Run Unified Capture Play Mode Test", priority = 150)]
        public static void RunCaptureSmokeTest()
        {
            Api.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.PlayMode,
                testNames = new[] { CaptureSmokeTestName }
            }));

            Debug.Log("[DataCaptureTests] Started Play Mode smoke test: " + CaptureSmokeTestName);
        }

        [MenuItem("Tools/VR Glove Data Capture/Run All Project Play Mode Tests", priority = 200)]
        [Shortcut(
            "VR Glove Data Capture/Tests/Run All Project Play Mode Tests",
            KeyCode.F10,
            ShortcutModifiers.Action | ShortcutModifiers.Shift)]
        public static void RunAllProjectSmokeTests()
        {
            Api.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.PlayMode,
                testNames = new[]
                {
                    SmokeTestName,
                    CaptureSmokeTestName,
                    AdaptiveContactSmokeTestName,
                    GazeControlPanelSmokeTestName
                }
            }));

            Debug.Log("[VRGloveTests] Started all project Play Mode smoke tests.");
        }
    }

    public sealed class PickPlaceTestCallbacks : ScriptableObject, ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun)
        {
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            string summary =
                "[PickPlaceTests] Finished: passed=" + result.PassCount +
                ", failed=" + result.FailCount +
                ", skipped=" + result.SkipCount + ".";

            if (result.FailCount == 0)
            {
                Debug.Log(summary);
            }
            else
            {
                Debug.LogError(summary + " " + result.Message + "\n" + result.StackTrace);
            }
        }

        public void TestStarted(ITestAdaptor test)
        {
        }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (result.TestStatus == TestStatus.Failed)
            {
                Debug.LogError(
                    "[PickPlaceTests] Failed: " + result.FullName + "\n" +
                    result.Message + "\n" + result.StackTrace);
            }
        }
    }
}
