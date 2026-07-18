using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRGloveDataCapture.RoboticsTasks;

namespace VRGloveDataCapture.Capture
{
    /// <summary>
    /// Scene-independent session/trial recorder. F12 starts/stops a trial and F11
    /// adds a timestamped manual marker. All streams share one host monotonic clock.
    /// </summary>
    public sealed class CaptureSessionManager : MonoBehaviour
    {
        [Serializable]
        private sealed class SessionTrialRecord
        {
            public int trialIndex;
            public string relativeDirectory;
            public string taskId;
            public string condition;
            public string startUtc;
            public string endUtc;
            public string status;
        }

        [Serializable]
        private sealed class SessionManifest
        {
            public string schemaVersion = "1.0.0";
            public string participantId;
            public string sessionLabel;
            public string startedUtc;
            public string updatedUtc;
            public string operatorId;
            public List<SessionTrialRecord> trials = new List<SessionTrialRecord>();
        }

        private const string ManagerObjectName = "[VR Glove Unified Data Capture]";
        private static CaptureSessionManager instance;

        [SerializeField] private CaptureProfile profile = new CaptureProfile();
        [SerializeField] private bool showRuntimeHud = true;

        private readonly Hi5CaptureAdapter hi5 = new Hi5CaptureAdapter();
        private readonly List<Hi5CaptureAdapter.SensorStatus> sensorStatuses =
            new List<Hi5CaptureAdapter.SensorStatus>(12);
        private readonly List<TrackedObjectBinding> trackedObjects = new List<TrackedObjectBinding>();
        private readonly List<IRawImuProvider> rawProviders = new List<IRawImuProvider>();
        private readonly List<RawImuSample> rawSamples = new List<RawImuSample>(256);

        private CaptureTrialContext currentTrial;
        private CsvTrialWriter writer;
        private SessionManifest sessionManifest;
        private string sessionDirectory;
        private long sampleId;
        private long eventId;
        private float nextSampleTime;
        private bool applicationIsQuitting;
        private string lastTrialDirectory;
        private string lastError;

        public static CaptureSessionManager Instance { get { return instance; } }
        public bool IsRecording { get { return writer != null; } }
        public CaptureProfile Profile { get { return profile.Copy(); } }
        public CaptureTrialContext CurrentTrial { get { return currentTrial; } }
        public string LastTrialDirectory { get { return lastTrialDirectory; } }
        public string LastError { get { return lastError; } }
        public string CurrentSessionDirectory { get { return sessionDirectory; } }
        public int DiscoveredBoneCount { get { return hi5.Bones.Count; } }
        public int TrackedObjectCount { get { return trackedObjects.Count; } }

        public static string CaptureRootDirectory
        {
            get
            {
                // Keep recordings outside the nested Unity project. Besides separating
                // research data from source, this avoids Unity 2019 Mono's legacy
                // Windows path-length failures in deeply nested workspaces.
                return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "Captures"));
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (instance != null)
            {
                return;
            }

            GameObject managerObject = new GameObject(ManagerObjectName);
            managerObject.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(managerObject);
            instance = managerObject.AddComponent<CaptureSessionManager>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            CaptureEventBus.EventPublished -= OnCaptureEvent;
            CaptureEventBus.EventPublished += OnCaptureEvent;
        }

        private void OnDisable()
        {
            CaptureEventBus.EventPublished -= OnCaptureEvent;
        }

        public void ApplyProfile(CaptureProfile newProfile)
        {
            if (newProfile == null || IsRecording)
            {
                return;
            }

            profile = newProfile.Copy();
            profile.Normalize();
        }

        public bool StartTrial()
        {
            if (IsRecording)
            {
                return true;
            }

            lastError = string.Empty;
            profile.Normalize();
            int boneCount = hi5.RefreshBones();
            DiscoverTrackedObjects();

            if (profile.requireHandTracking && boneCount == 0)
            {
                lastError = "No Hi5 hand skeleton was found. Load the Hi5 VIVE base scene and wait for both hands before starting.";
                Debug.LogError("[DataCapture] " + lastError, this);
                return false;
            }

            try
            {
                EnsureSession();
                int trialIndex = NextTrialIndex();
                string trialName = "trial-" + trialIndex.ToString("D4", CultureInfo.InvariantCulture) +
                                   "__task-" + profile.taskId + "__cond-" + profile.condition;
                string trialDirectory = Path.Combine(sessionDirectory, "trials", trialName);
                Directory.CreateDirectory(trialDirectory);

                long startNs = CaptureClock.MonotonicNanoseconds;
                string startUtc = CaptureClock.UtcNowIso8601;
                currentTrial = new CaptureTrialContext(
                    sessionDirectory,
                    trialDirectory,
                    profile,
                    trialIndex,
                    startNs,
                    startUtc);

                rawProviders.Clear();
                if (profile.captureRawImuIfAvailable)
                {
                    rawProviders.AddRange(RawImuProviderRegistry.GetAvailableProviders());
                }

                writer = new CsvTrialWriter(
                    currentTrial,
                    rawProviders,
                    boneCount,
                    trackedObjects.Count,
                    GetLoadedSceneNames());
                sampleId = 0;
                eventId = 0;
                nextSampleTime = Time.unscaledTime;
                lastTrialDirectory = trialDirectory;

                WriteEvent("trial_started", profile.taskId, string.Empty,
                    "condition=" + profile.condition);
                CaptureLifecycle.RaiseTrialStarted(currentTrial);
                WriteLatestTrialPointer(trialDirectory);

                Debug.Log("[DataCapture] TRIAL STARTED (F12 to stop): " + trialDirectory, this);
                if (rawProviders.Count == 0 && profile.captureRawImuIfAvailable)
                {
                    Debug.LogWarning(
                        "[DataCapture] Hi5 Unity SDK has no raw nine-axis API. Other streams are recording; manifest marks raw IMU unavailable_vendor_api.",
                        this);
                }
                return true;
            }
            catch (Exception exception)
            {
                lastError = exception.Message;
                Debug.LogError("[DataCapture] Failed to start trial: " + exception, this);
                if (writer != null)
                {
                    writer.Dispose();
                }
                writer = null;
                currentTrial = null;
                return false;
            }
        }

        public void StopTrial()
        {
            StopTrial("complete");
        }

        public void ToggleTrial()
        {
            if (IsRecording)
            {
                StopTrial();
            }
            else
            {
                StartTrial();
            }
        }

        public void StartNewSession()
        {
            if (IsRecording)
            {
                lastError = "Stop the active trial before starting a new session.";
                return;
            }

            sessionDirectory = null;
            sessionManifest = null;
            lastError = string.Empty;
        }

        public void AddMarker(string label)
        {
            if (!IsRecording)
            {
                lastError = "Manual markers require an active trial.";
                Debug.LogWarning("[DataCapture] " + lastError, this);
                return;
            }

            CaptureEventBus.AddManualMarker(string.IsNullOrEmpty(label) ? "marker" : label);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F12))
            {
                ToggleTrial();
            }

            if (Input.GetKeyDown(KeyCode.F11))
            {
                AddMarker("F11");
            }
        }

        private void LateUpdate()
        {
            if (!IsRecording || Time.unscaledTime + 0.0001f < nextSampleTime)
            {
                return;
            }

            float interval = 1.0f / Mathf.Max(1, profile.sampleRateHz);
            nextSampleTime = Mathf.Max(nextSampleTime + interval, Time.unscaledTime);
            CaptureFrameStamp stamp = CreateFrameStamp();
            hi5.ReadSensorStatuses(sensorStatuses);
            Camera hmdCamera = Camera.main;

            try
            {
                writer.WriteFrame(stamp, hi5.Bones, trackedObjects, sensorStatuses, hmdCamera);
                DrainRawImu(stamp);
                sampleId++;
            }
            catch (Exception exception)
            {
                lastError = exception.Message;
                Debug.LogError("[DataCapture] Stream write failed; trial will be finalized as aborted: " + exception, this);
                StopTrial("aborted_write_error");
            }
        }

        private void OnGUI()
        {
            if (!showRuntimeHud)
            {
                return;
            }

            string state = IsRecording
                ? "RECORDING  trial-" + currentTrial.TrialIndex.ToString("D4", CultureInfo.InvariantCulture)
                : "READY";
            string rawState = rawProviders.Count > 0 ? "raw IMU: available" : "raw IMU: vendor API unavailable";
            string content = "Unified Data Capture: " + state + "\n" +
                             profile.participantId + " / " + profile.taskId + " / " + profile.condition + "\n" +
                             "F12 start/stop trial   F11 marker   " + rawState;
            GUI.Box(new Rect(12.0f, 12.0f, 590.0f, 67.0f), content);
        }

        private void OnApplicationQuit()
        {
            applicationIsQuitting = true;
            if (IsRecording)
            {
                StopTrial("aborted_application_quit");
            }
        }

        private void OnDestroy()
        {
            if (!applicationIsQuitting && IsRecording)
            {
                StopTrial("aborted_manager_destroyed");
            }

            if (instance == this)
            {
                instance = null;
            }
        }

        private void StopTrial(string outcome)
        {
            if (!IsRecording)
            {
                return;
            }

            CaptureTrialContext stoppingTrial = currentTrial;
            string finalDirectory = stoppingTrial.TrialDirectory;
            try
            {
                WriteEvent("trial_stopping", profile.taskId, string.Empty, "outcome=" + outcome);
                CaptureLifecycle.RaiseTrialStopping(stoppingTrial);
                writer.FinalizeTrial(outcome);
                AddSessionTrial(stoppingTrial, outcome);
                Debug.Log("[DataCapture] TRIAL FINALIZED: " + finalDirectory, this);
            }
            catch (Exception exception)
            {
                lastError = exception.Message;
                Debug.LogError("[DataCapture] Failed to finalize trial: " + exception, this);
                writer.Dispose();
            }
            finally
            {
                writer = null;
                currentTrial = null;
                rawProviders.Clear();
                rawSamples.Clear();
                CaptureLifecycle.RaiseTrialStopped(stoppingTrial);
            }
        }

        private CaptureFrameStamp CreateFrameStamp()
        {
            long monotonic = CaptureClock.MonotonicNanoseconds;
            return new CaptureFrameStamp
            {
                SampleId = sampleId,
                MonotonicNanoseconds = monotonic,
                RelativeNanoseconds = monotonic - currentTrial.StartMonotonicNanoseconds,
                UtcIso8601 = CaptureClock.UtcNowIso8601,
                UnityFrame = Time.frameCount,
                UnityUnscaledTimeSeconds = Time.unscaledTime
            };
        }

        private void DrainRawImu(CaptureFrameStamp stamp)
        {
            for (int index = 0; index < rawProviders.Count; index++)
            {
                IRawImuProvider provider = rawProviders[index];
                rawSamples.Clear();
                try
                {
                    provider.DrainSamples(rawSamples);
                    writer.WriteRawImu(stamp, provider.ProviderId, rawSamples);
                }
                catch (Exception exception)
                {
                    WriteEvent("raw_imu_provider_error", string.Empty, provider.ProviderId, exception.Message);
                }
            }
        }

        private void OnCaptureEvent(string eventType, string taskId, string objectId, string details)
        {
            if (IsRecording)
            {
                WriteEvent(eventType, taskId, objectId, details);
            }
        }

        private void WriteEvent(string eventType, string taskId, string objectId, string details)
        {
            if (writer == null)
            {
                return;
            }

            long monotonic = CaptureClock.MonotonicNanoseconds;
            writer.WriteEvent(
                eventId++,
                monotonic,
                CaptureClock.UtcNowIso8601,
                Time.frameCount,
                eventType,
                taskId,
                objectId,
                details);
        }

        private void DiscoverTrackedObjects()
        {
            trackedObjects.Clear();
            HashSet<Transform> added = new HashSet<Transform>();

            CaptureTrackedObject[] explicitObjects = FindObjectsOfType<CaptureTrackedObject>();
            for (int index = 0; index < explicitObjects.Length; index++)
            {
                CaptureTrackedObject tracked = explicitObjects[index];
                AddTrackedObject(tracked.transform, tracked.ObjectId, tracked.Category, added);
            }

            PickPlaceTaskObject[] taskObjects = FindObjectsOfType<PickPlaceTaskObject>();
            for (int index = 0; index < taskObjects.Length; index++)
            {
                PickPlaceTaskObject taskObject = taskObjects[index];
                AddTrackedObject(
                    taskObject.transform,
                    "task-" + CapturePathUtility.SanitizeSegment(taskObject.TaskId, taskObject.name),
                    "pick_place_task_object",
                    added);
            }

            PickPlaceTargetZone[] targetZones = FindObjectsOfType<PickPlaceTargetZone>();
            for (int index = 0; index < targetZones.Length; index++)
            {
                PickPlaceTargetZone target = targetZones[index];
                AddTrackedObject(
                    target.transform,
                    "target-" + CapturePathUtility.SanitizeSegment(target.TaskId, target.name),
                    "pick_place_target",
                    added);
            }

            trackedObjects.Sort(delegate(TrackedObjectBinding left, TrackedObjectBinding right)
            {
                return string.CompareOrdinal(left.ObjectId, right.ObjectId);
            });
        }

        private void AddTrackedObject(
            Transform transform,
            string objectId,
            string category,
            HashSet<Transform> added)
        {
            if (transform == null || !added.Add(transform))
            {
                return;
            }

            trackedObjects.Add(new TrackedObjectBinding
            {
                ObjectId = CapturePathUtility.SanitizeSegment(objectId, "object"),
                Category = category,
                HierarchyPath = HierarchyPath(transform),
                Transform = transform,
                Body = transform.GetComponent<Rigidbody>()
            });
        }

        private void EnsureSession()
        {
            if (!string.IsNullOrEmpty(sessionDirectory))
            {
                return;
            }

            string sessionTimestamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss.fff'Z'", CultureInfo.InvariantCulture);
            string sessionName = sessionTimestamp + "__session-" + profile.sessionLabel;
            sessionDirectory = Path.Combine(
                CaptureRootDirectory,
                "participants",
                profile.participantId,
                "sessions",
                sessionName);
            Directory.CreateDirectory(Path.Combine(sessionDirectory, "trials"));
            sessionManifest = new SessionManifest
            {
                participantId = profile.participantId,
                sessionLabel = profile.sessionLabel,
                operatorId = profile.operatorId,
                startedUtc = CaptureClock.UtcNowIso8601,
                updatedUtc = CaptureClock.UtcNowIso8601
            };
            WriteSessionManifest();
        }

        private int NextTrialIndex()
        {
            int index = sessionManifest == null ? 1 : sessionManifest.trials.Count + 1;
            while (Directory.Exists(Path.Combine(sessionDirectory, "trials", "trial-" + index.ToString("D4", CultureInfo.InvariantCulture) +
                "__task-" + profile.taskId + "__cond-" + profile.condition)))
            {
                index++;
            }
            return index;
        }

        private void AddSessionTrial(CaptureTrialContext trial, string status)
        {
            if (sessionManifest == null)
            {
                return;
            }

            sessionManifest.trials.Add(new SessionTrialRecord
            {
                trialIndex = trial.TrialIndex,
                relativeDirectory = "trials/" + Path.GetFileName(trial.TrialDirectory),
                taskId = trial.Profile.taskId,
                condition = trial.Profile.condition,
                startUtc = trial.StartUtcIso8601,
                endUtc = CaptureClock.UtcNowIso8601,
                status = status
            });
            sessionManifest.updatedUtc = CaptureClock.UtcNowIso8601;
            WriteSessionManifest();
        }

        private void WriteSessionManifest()
        {
            string finalPath = Path.Combine(sessionDirectory, "session.json");
            string temporaryPath = finalPath + ".partial";
            File.WriteAllText(temporaryPath,
                JsonUtility.ToJson(sessionManifest, true) + Environment.NewLine,
                new UTF8Encoding(false));
            if (File.Exists(finalPath))
            {
                File.Delete(finalPath);
            }
            File.Move(temporaryPath, finalPath);
        }

        private static void WriteLatestTrialPointer(string trialDirectory)
        {
            Directory.CreateDirectory(CaptureRootDirectory);
            File.WriteAllText(
                Path.Combine(CaptureRootDirectory, "LATEST_TRIAL.txt"),
                trialDirectory + Environment.NewLine,
                new UTF8Encoding(false));
        }

        private static string[] GetLoadedSceneNames()
        {
            string[] scenes = new string[SceneManager.sceneCount];
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                scenes[index] = SceneManager.GetSceneAt(index).name;
            }
            return scenes;
        }

        private static string HierarchyPath(Transform transform)
        {
            string path = transform.name;
            Transform current = transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return transform.gameObject.scene.name + ":" + path;
        }
    }
}
