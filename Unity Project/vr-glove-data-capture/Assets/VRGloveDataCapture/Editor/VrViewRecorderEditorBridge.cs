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

        static VrViewRecorderEditorBridge()
        {
            VrViewRecordingHotkey.ToggleRequested -= ToggleRecording;
            VrViewRecordingHotkey.ToggleRequested += ToggleRecording;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= StopAndRelease;
            AssemblyReloadEvents.beforeAssemblyReload += StopAndRelease;
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

            if (recorderController != null && recorderController.IsRecording())
            {
                StopAndRelease();
            }
            else
            {
                StartRecording();
            }
        }

        [MenuItem("Tools/VR Glove Data Capture/Toggle VR View Recording", true)]
        private static bool ValidateToggleRecording()
        {
            return !EditorApplication.isCompiling;
        }

        private static void StartRecording()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string outputDirectory = Path.Combine(projectRoot, "Recordings");
            Directory.CreateDirectory(outputDirectory);

            currentOutputPath = Path.Combine(
                outputDirectory,
                "vr_view_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4");

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
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[VRGloveDataCapture] Failed to start VR view recording: " + exception);
                StopAndRelease();
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
