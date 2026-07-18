using System.IO;
using UnityEditor;
using UnityEngine;
using VRGloveDataCapture.Capture;

namespace VRGloveDataCapture.Editor
{
    /// <summary>Operator-facing metadata and trial controls for unified capture.</summary>
    [InitializeOnLoad]
    internal sealed class CaptureControlWindow : EditorWindow
    {
        private const string ProfilePreferenceKey = "VRGloveDataCapture.CaptureProfile.v1";
        private CaptureProfile profile;
        private string markerLabel = "checkpoint";

        static CaptureControlWindow()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem("Tools/VR Glove Data Capture/Capture Control")]
        private static void Open()
        {
            CaptureControlWindow window = GetWindow<CaptureControlWindow>("Capture Control");
            window.minSize = new Vector2(460.0f, 520.0f);
            window.Show();
        }

        private void OnEnable()
        {
            profile = LoadProfile();
            EditorApplication.update -= RefreshWhilePlaying;
            EditorApplication.update += RefreshWhilePlaying;
        }

        private void OnDisable()
        {
            SaveProfile(profile);
            EditorApplication.update -= RefreshWhilePlaying;
        }

        private void OnGUI()
        {
            if (profile == null)
            {
                profile = LoadProfile();
            }

            EditorGUILayout.LabelField("Unified Capture Session", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Set metadata before Play Mode. F12 starts/stops one trial; F11 adds a manual marker. " +
                "Files are finalized atomically under the project's Captures directory.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            profile.participantId = EditorGUILayout.TextField("Participant ID", profile.participantId);
            profile.sessionLabel = EditorGUILayout.TextField("Session Label", profile.sessionLabel);
            profile.taskId = EditorGUILayout.TextField("Task ID", profile.taskId);
            profile.condition = EditorGUILayout.TextField("Condition", profile.condition);
            profile.operatorId = EditorGUILayout.TextField("Operator ID", profile.operatorId);
            profile.sampleRateHz = EditorGUILayout.IntSlider("Sample Rate (Hz)", profile.sampleRateHz, 1, 120);
            profile.recordVideo = EditorGUILayout.Toggle("Record VR View Video", profile.recordVideo);
            profile.captureRawImuIfAvailable = EditorGUILayout.Toggle("Raw IMU If Available", profile.captureRawImuIfAvailable);
            profile.requireHandTracking = EditorGUILayout.Toggle("Require Hi5 Skeleton", profile.requireHandTracking);
            EditorGUILayout.LabelField("Notes");
            profile.notes = EditorGUILayout.TextArea(profile.notes, GUILayout.MinHeight(55.0f));
            if (EditorGUI.EndChangeCheck())
            {
                SaveProfile(profile);
                ApplyProfileToRuntime();
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Current Hi5 2.0 Unity SDK exposes solved skeleton transforms and sensor health only. " +
                "It does not expose raw accelerometer, gyroscope, or three-axis magnetometer vectors. " +
                "Raw IMU remains explicitly unavailable until a vendor/transport IRawImuProvider is installed.",
                MessageType.Warning);

            DrawRuntimeStatus();
            DrawControls();
            DrawOutputControls();
        }

        private void DrawRuntimeStatus()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Runtime Status", EditorStyles.boldLabel);
            CaptureSessionManager manager = CaptureSessionManager.Instance;
            if (!EditorApplication.isPlaying || manager == null)
            {
                EditorGUILayout.LabelField("State", "Enter Play Mode to record");
                EditorGUILayout.LabelField("Output Root", CaptureSessionManager.CaptureRootDirectory);
                return;
            }

            EditorGUILayout.LabelField("State", manager.IsRecording ? "RECORDING" : "Ready");
            EditorGUILayout.LabelField("Hi5 Bones", manager.DiscoveredBoneCount.ToString());
            EditorGUILayout.LabelField("Tracked Objects", manager.TrackedObjectCount.ToString());
            if (manager.CurrentTrial != null)
            {
                EditorGUILayout.LabelField("Trial", manager.CurrentTrial.TrialIndex.ToString("D4"));
                EditorGUILayout.SelectableLabel(
                    manager.CurrentTrial.TrialDirectory,
                    EditorStyles.textField,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
            if (!string.IsNullOrEmpty(manager.LastError))
            {
                EditorGUILayout.HelpBox(manager.LastError, MessageType.Error);
            }
        }

        private void DrawControls()
        {
            CaptureSessionManager manager = CaptureSessionManager.Instance;
            bool runtimeReady = EditorApplication.isPlaying && manager != null;

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!runtimeReady))
            {
                string toggleLabel = runtimeReady && manager.IsRecording
                    ? "Stop & Finalize Trial (F12)"
                    : "Start Trial (F12)";
                if (GUILayout.Button(toggleLabel, GUILayout.Height(34.0f)))
                {
                    ApplyProfileToRuntime();
                    manager.ToggleTrial();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    markerLabel = EditorGUILayout.TextField(markerLabel);
                    using (new EditorGUI.DisabledScope(!runtimeReady || !manager.IsRecording))
                    {
                        if (GUILayout.Button("Add Event Marker", GUILayout.Width(135.0f)))
                        {
                            manager.AddMarker(markerLabel);
                        }
                    }
                }

                using (new EditorGUI.DisabledScope(runtimeReady && manager.IsRecording))
                {
                    if (GUILayout.Button("Start New Session on Next Trial"))
                    {
                        manager.StartNewSession();
                    }
                }
            }
        }

        private void DrawOutputControls()
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Captures Folder"))
                {
                    Directory.CreateDirectory(CaptureSessionManager.CaptureRootDirectory);
                    EditorUtility.RevealInFinder(CaptureSessionManager.CaptureRootDirectory);
                }

                CaptureSessionManager manager = CaptureSessionManager.Instance;
                string latest = manager == null ? null : manager.LastTrialDirectory;
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(latest) || !Directory.Exists(latest)))
                {
                    if (GUILayout.Button("Open Latest Trial"))
                    {
                        EditorUtility.RevealInFinder(latest);
                    }
                }
            }
        }

        private void ApplyProfileToRuntime()
        {
            CaptureSessionManager manager = CaptureSessionManager.Instance;
            if (manager != null && !manager.IsRecording)
            {
                manager.ApplyProfile(profile);
            }
        }

        private void RefreshWhilePlaying()
        {
            if (EditorApplication.isPlaying)
            {
                Repaint();
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode)
            {
                return;
            }

            EditorApplication.delayCall += delegate
            {
                CaptureSessionManager manager = CaptureSessionManager.Instance;
                if (manager != null && !manager.IsRecording)
                {
                    manager.ApplyProfile(LoadProfile());
                }
            };
        }

        private static CaptureProfile LoadProfile()
        {
            string json = EditorPrefs.GetString(ProfilePreferenceKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return new CaptureProfile();
            }

            CaptureProfile loaded = JsonUtility.FromJson<CaptureProfile>(json);
            return loaded ?? new CaptureProfile();
        }

        private static void SaveProfile(CaptureProfile value)
        {
            if (value != null)
            {
                EditorPrefs.SetString(ProfilePreferenceKey, JsonUtility.ToJson(value));
            }
        }
    }
}
