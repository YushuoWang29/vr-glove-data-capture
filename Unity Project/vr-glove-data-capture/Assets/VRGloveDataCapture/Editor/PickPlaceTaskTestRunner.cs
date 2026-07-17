using UnityEditor;
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

        private static readonly TestRunnerApi Api;

        static PickPlaceTaskTestRunner()
        {
            Api = ScriptableObject.CreateInstance<TestRunnerApi>();
            Api.RegisterCallbacks(ScriptableObject.CreateInstance<PickPlaceTestCallbacks>());
        }

        [MenuItem("Tools/VR Glove Data Capture/Task Setups/Run Pick Place Play Mode Test _F7", priority = 140)]
        public static void RunSmokeTest()
        {
            Api.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.PlayMode,
                testNames = new[] { SmokeTestName }
            }));

            Debug.Log("[PickPlaceTests] Started Play Mode smoke test: " + SmokeTestName);
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
