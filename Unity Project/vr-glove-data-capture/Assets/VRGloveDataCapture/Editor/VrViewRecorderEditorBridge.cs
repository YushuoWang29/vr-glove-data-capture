using System;
using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEngine;
using VRGloveDataCapture.Capture;

namespace VRGloveDataCapture.Editor
{
    /// <summary>
    /// Records the Unity Game View (the desktop mirror of the HMD view) to MP4.
    /// F9 starts/stops recording without requiring a scene or prefab edit.
    /// </summary>
    [InitializeOnLoad]
    internal static class VrViewRecorderEditorBridge
    {
        private const int OutputWidth = 1920;
        private const int OutputHeight = 1080;
        private const float OutputFrameRate = 30.0f;

        private static RecorderController recorderController;
        private static RecorderControllerSettings controllerSettings;
        private static MovieRecorderSettings movieSettings;
        private static string currentOutputPath;
        private static bool managedByCaptureTrial;

        static VrViewRecorderEditorBridge()
        {
            VrViewRecordingHotkey.ToggleRequested -= ToggleRecording;
            VrViewRecordingHotkey.ToggleRequested += ToggleRecording;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= StopAndRelease;
            AssemblyReloadEvents.beforeAssemblyReload += StopAndRelease;
            CaptureLifecycle.TrialStarted -= OnCaptureTrialStarted;
            CaptureLifecycle.TrialStarted += OnCaptureTrialStarted;
            CaptureLifecycle.TrialStopping -= OnCaptureTrialStopping;
            CaptureLifecycle.TrialStopping += OnCaptureTrialStopping;
            VrViewRecordingHotkey.ReportState(false, "VR view recording is idle.");
        }

        [MenuItem("Tools/VR Glove Data Capture/Toggle VR View Recording")]
        private static void ToggleRecording()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning(
                    "[VRGloveDataCapture] Enter Play Mode before starting VR view recording.");
                return;
            }

            if (CaptureSessionManager.Instance != null &&
                CaptureSessionManager.Instance.IsRecording &&
                CaptureSessionManager.Instance.CurrentTrial != null &&
                CaptureSessionManager.Instance.CurrentTrial.Profile.recordVideo)
            {
                Debug.LogWarning(
                    "[VRGloveDataCapture] F9 is locked while F12 unified capture controls synchronized video.");
                return;
            }

            if (recorderController != null && recorderController.IsRecording())
            {
                StopAndRelease();
            }
            else
            {
                StartRecording(null, false);
            }
        }

        [MenuItem("Tools/VR Glove Data Capture/Toggle VR View Recording", true)]
        private static bool ValidateToggleRecording()
        {
            return !EditorApplication.isCompiling;
        }

        private static void StartRecording(string requestedOutputPath, bool isManagedTrial)
        {
            string outputDirectory;
            if (string.IsNullOrEmpty(requestedOutputPath))
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                outputDirectory = Path.Combine(projectRoot, "Recordings");
                currentOutputPath = Path.Combine(
                    outputDirectory,
                    "vr_view_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4");
            }
            else
            {
                currentOutputPath = Path.ChangeExtension(requestedOutputPath, ".mp4");
                outputDirectory = Path.GetDirectoryName(currentOutputPath);
            }
            Directory.CreateDirectory(outputDirectory);
            managedByCaptureTrial = isManagedTrial;

            controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            controllerSettings.name = "VR Glove VR View Recorder";
            controllerSettings.SetRecordModeToManual();
            controllerSettings.FrameRate = OutputFrameRate;
            controllerSettings.FrameRatePlayback = FrameRatePlayback.Constant;
            controllerSettings.CapFrameRate = false;

            movieSettings = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movieSettings.name = "VR HMD Mirror MP4";
            movieSettings.Enabled = true;
            movieSettings.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
            movieSettings.VideoBitRateMode = VideoBitrateMode.High;
            movieSettings.ImageInputSettings = new GameViewInputSettings
            {
                OutputWidth = OutputWidth,
                OutputHeight = OutputHeight
            };
            movieSettings.AudioInputSettings.PreserveAudio = true;
            movieSettings.OutputFile = Path.Combine(
                Path.GetDirectoryName(currentOutputPath),
                Path.GetFileNameWithoutExtension(currentOutputPath));

            controllerSettings.AddRecorderSettings(movieSettings);
            RecorderOptions.VerboseMode = false;
            recorderController = new RecorderController(controllerSettings);

            try
            {
                recorderController.PrepareRecording();
                if (!recorderController.StartRecording())
                {
                    throw new InvalidOperationException(
                        "Unity Recorder did not create an active recording session.");
                }

                Debug.Log(
                    "[VRGloveDataCapture] VR VIEW RECORDING STARTED (F9 to stop): " +
                    currentOutputPath);
                VrViewRecordingHotkey.ReportState(
                    true,
                    managedByCaptureTrial
                        ? "Recording synchronized trial video."
                        : "Recording standalone VR view video.");
                if (managedByCaptureTrial)
                {
                    CaptureEventBus.Publish(
                        "video_recording_started",
                        string.Empty,
                        "hmd_camera",
                        currentOutputPath);
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[VRGloveDataCapture] Failed to start VR view recording: " + exception);
                StopAndRelease();
                VrViewRecordingHotkey.ReportState(false, "VR recording failed: " + exception.Message);
            }
        }

        private static void StopAndRelease()
        {
            bool wasRecording = recorderController != null && recorderController.IsRecording();

            if (recorderController != null)
            {
                recorderController.StopRecording();
            }

            if (wasRecording)
            {
                Debug.Log(
                    "[VRGloveDataCapture] VR VIEW RECORDING SAVED: " + currentOutputPath);
            }

            recorderController = null;

            if (movieSettings != null)
            {
                UnityEngine.Object.DestroyImmediate(movieSettings);
                movieSettings = null;
            }

            if (controllerSettings != null)
            {
                UnityEngine.Object.DestroyImmediate(controllerSettings);
                controllerSettings = null;
            }

            currentOutputPath = null;
            managedByCaptureTrial = false;
            VrViewRecordingHotkey.ReportState(false, "VR view recording is idle.");
        }

        private static void OnCaptureTrialStarted(CaptureTrialContext context)
        {
            if (context == null || !context.Profile.recordVideo)
            {
                return;
            }

            if (recorderController != null && recorderController.IsRecording())
            {
                StopAndRelease();
            }

            StartRecording(context.VideoOutputPath, true);
        }

        private static void OnCaptureTrialStopping(CaptureTrialContext context)
        {
            if (!managedByCaptureTrial)
            {
                return;
            }

            string savedPath = currentOutputPath;
            StopAndRelease();
            CaptureEventBus.Publish(
                "video_recording_stopped",
                string.Empty,
                "hmd_camera",
                savedPath ?? string.Empty);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode ||
                state == PlayModeStateChange.EnteredEditMode)
            {
                StopAndRelease();
            }
        }
    }
}
