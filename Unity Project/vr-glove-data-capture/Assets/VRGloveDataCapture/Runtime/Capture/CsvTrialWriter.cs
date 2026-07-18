using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace VRGloveDataCapture.Capture
{
    internal struct CaptureFrameStamp
    {
        public long SampleId;
        public long MonotonicNanoseconds;
        public long RelativeNanoseconds;
        public string UtcIso8601;
        public int UnityFrame;
        public float UnityUnscaledTimeSeconds;
    }

    internal sealed class TrackedObjectBinding
    {
        public string ObjectId;
        public string Category;
        public string HierarchyPath;
        public Transform Transform;
        public Rigidbody Body;
    }

    internal sealed class CsvTrialWriter : IDisposable
    {
        [Serializable]
        private sealed class FileRecord
        {
            public string relativePath;
            public long bytes;
            public string sha256;
        }

        [Serializable]
        private sealed class TrialManifest
        {
            public string schemaVersion = "1.0.0";
            public string status;
            public int trialIndex;
            public string startUtc;
            public string endUtc;
            public long startMonotonicNs;
            public long endMonotonicNs;
            public string clock = "host_monotonic_stopwatch";
            public string coordinateSystem = "Unity left-handed; metres; world and local transforms";
            public string bonePoseSource = "Hi5 solved visual skeleton transforms (not raw IMU)";
            public string rawImuStatus;
            public string[] rawImuProviders;
            public string videoStatus;
            public string videoRelativePath;
            public int requestedSampleRateHz;
            public long frameSamples;
            public long boneRows;
            public long jointAngleRows;
            public long objectRows;
            public long deviceRows;
            public long sensorStatusRows;
            public long rawImuRows;
            public long eventRows;
            public int discoveredBoneCount;
            public int discoveredObjectCount;
            public string unityVersion;
            public string[] scenes;
            public CaptureProfile profile;
            public List<FileRecord> files = new List<FileRecord>();
        }

        private sealed class AtomicCsv : IDisposable
        {
            private readonly string finalPath;
            private readonly string partialPath;
            private StreamWriter writer;

            public AtomicCsv(string finalFilePath, string header)
            {
                finalPath = finalFilePath;
                partialPath = finalFilePath + ".partial";
                Directory.CreateDirectory(Path.GetDirectoryName(finalFilePath));
                writer = new StreamWriter(
                    new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.Read, 131072),
                    new UTF8Encoding(false),
                    131072);
                writer.WriteLine(header);
            }

            public void WriteLine(string line)
            {
                if (writer != null)
                {
                    writer.WriteLine(line);
                }
            }

            public void Complete()
            {
                Dispose();
                if (File.Exists(finalPath))
                {
                    File.Delete(finalPath);
                }
                if (File.Exists(partialPath))
                {
                    File.Move(partialPath, finalPath);
                }
            }

            public void Dispose()
            {
                if (writer == null)
                {
                    return;
                }
                writer.Flush();
                writer.Dispose();
                writer = null;
            }
        }

        private readonly CaptureTrialContext context;
        private readonly TrialManifest manifest;
        private readonly AtomicCsv frames;
        private readonly AtomicCsv handBones;
        private readonly AtomicCsv jointAngles;
        private readonly AtomicCsv objects;
        private readonly AtomicCsv devices;
        private readonly AtomicCsv sensorStatus;
        private readonly AtomicCsv events;
        private readonly AtomicCsv rawImu;
        private bool finalized;

        public CsvTrialWriter(
            CaptureTrialContext trialContext,
            IList<IRawImuProvider> rawProviders,
            int discoveredBoneCount,
            int discoveredObjectCount,
            string[] scenes)
        {
            context = trialContext;
            string streams = Path.Combine(context.TrialDirectory, "streams");
            string eventDirectory = Path.Combine(context.TrialDirectory, "events");
            Directory.CreateDirectory(Path.Combine(context.TrialDirectory, "video"));

            frames = new AtomicCsv(
                Path.Combine(streams, "frame_timing.csv"),
                "sample_id,t_monotonic_ns,t_trial_ns,utc_iso8601,unity_frame,unity_unscaled_time_s");
            handBones = new AtomicCsv(
                Path.Combine(streams, "hand_bones.csv"),
                "sample_id,t_monotonic_ns,t_trial_ns,hand,bone,parent_bone,world_px_m,world_py_m,world_pz_m,world_qx,world_qy,world_qz,world_qw,local_px_m,local_py_m,local_pz_m,local_qx,local_qy,local_qz,local_qw");
            jointAngles = new AtomicCsv(
                Path.Combine(streams, "joint_angles.csv"),
                "sample_id,t_monotonic_ns,t_trial_ns,hand,joint,parent_joint,local_euler_x_deg,local_euler_y_deg,local_euler_z_deg");
            objects = new AtomicCsv(
                Path.Combine(streams, "objects.csv"),
                "sample_id,t_monotonic_ns,t_trial_ns,object_id,category,hierarchy_path,active,world_px_m,world_py_m,world_pz_m,world_qx,world_qy,world_qz,world_qw,linear_vx_m_s,linear_vy_m_s,linear_vz_m_s,angular_vx_rad_s,angular_vy_rad_s,angular_vz_rad_s");
            devices = new AtomicCsv(
                Path.Combine(streams, "devices.csv"),
                "sample_id,t_monotonic_ns,t_trial_ns,device_id,world_px_m,world_py_m,world_pz_m,world_qx,world_qy,world_qz,world_qw");
            sensorStatus = new AtomicCsv(
                Path.Combine(streams, "hi5_sensor_status.csv"),
                "sample_id,t_monotonic_ns,t_trial_ns,hand,sensor,online,energy_percent,signal_percent,magnetic_quality_percent");
            events = new AtomicCsv(
                Path.Combine(eventDirectory, "events.csv"),
                "event_id,t_monotonic_ns,t_trial_ns,utc_iso8601,unity_frame,event_type,task_id,object_id,details");

            string[] providerIds = new string[rawProviders.Count];
            for (int index = 0; index < rawProviders.Count; index++)
            {
                providerIds[index] = rawProviders[index].ProviderId;
            }

            bool writeRaw = context.Profile.captureRawImuIfAvailable && rawProviders.Count > 0;
            if (writeRaw)
            {
                rawImu = new AtomicCsv(
                    Path.Combine(streams, "imu_raw.csv"),
                    "host_sample_id,host_t_monotonic_ns,host_t_trial_ns,provider_id,hand,sensor_id,source_t_ns,source_sequence,accel_x_m_s2,accel_y_m_s2,accel_z_m_s2,gyro_x_rad_s,gyro_y_rad_s,gyro_z_rad_s,mag_x_t,mag_y_t,mag_z_t");
            }

            manifest = new TrialManifest
            {
                status = "recording",
                trialIndex = context.TrialIndex,
                startUtc = context.StartUtcIso8601,
                startMonotonicNs = context.StartMonotonicNanoseconds,
                rawImuStatus = writeRaw
                    ? "available"
                    : context.Profile.captureRawImuIfAvailable
                        ? "unavailable_vendor_api"
                        : "disabled",
                rawImuProviders = providerIds,
                videoStatus = context.Profile.recordVideo ? "requested" : "disabled",
                videoRelativePath = context.Profile.recordVideo ? "video/vr_view.mp4" : string.Empty,
                requestedSampleRateHz = context.Profile.sampleRateHz,
                discoveredBoneCount = discoveredBoneCount,
                discoveredObjectCount = discoveredObjectCount,
                unityVersion = Application.unityVersion,
                scenes = scenes,
                profile = context.Profile.Copy()
            };

            File.WriteAllText(Path.Combine(context.TrialDirectory, "INCOMPLETE"),
                "Recording started at " + context.StartUtcIso8601 + Environment.NewLine,
                new UTF8Encoding(false));
            WriteManifest("manifest.partial.json");
        }

        public void WriteFrame(
            CaptureFrameStamp stamp,
            IList<Hi5CaptureAdapter.BoneBinding> bones,
            IList<TrackedObjectBinding> trackedObjects,
            IList<Hi5CaptureAdapter.SensorStatus> statuses,
            Camera hmdCamera)
        {
            frames.WriteLine(string.Join(",", new[]
            {
                stamp.SampleId.ToString(CultureInfo.InvariantCulture),
                stamp.MonotonicNanoseconds.ToString(CultureInfo.InvariantCulture),
                stamp.RelativeNanoseconds.ToString(CultureInfo.InvariantCulture),
                stamp.UtcIso8601,
                stamp.UnityFrame.ToString(CultureInfo.InvariantCulture),
                CapturePathUtility.Float(stamp.UnityUnscaledTimeSeconds)
            }));
            manifest.frameSamples++;

            for (int index = 0; index < bones.Count; index++)
            {
                Hi5CaptureAdapter.BoneBinding binding = bones[index];
                Transform transform = binding.Transform;
                if (transform == null)
                {
                    continue;
                }

                Vector3 worldPosition = transform.position;
                Quaternion worldRotation = transform.rotation;
                Vector3 localPosition = transform.localPosition;
                Quaternion localRotation = transform.localRotation;
                handBones.WriteLine(Prefix(stamp) + "," +
                    CapturePathUtility.Csv(binding.Hand) + "," +
                    CapturePathUtility.Csv(binding.Bone) + "," +
                    CapturePathUtility.Csv(binding.ParentBone) + "," +
                    Vector(worldPosition) + "," + QuaternionValue(worldRotation) + "," +
                    Vector(localPosition) + "," + QuaternionValue(localRotation));
                manifest.boneRows++;

                Vector3 euler = SignedEuler(localRotation.eulerAngles);
                jointAngles.WriteLine(Prefix(stamp) + "," +
                    CapturePathUtility.Csv(binding.Hand) + "," +
                    CapturePathUtility.Csv(binding.Bone) + "," +
                    CapturePathUtility.Csv(binding.ParentBone) + "," + Vector(euler));
                manifest.jointAngleRows++;
            }

            for (int index = 0; index < trackedObjects.Count; index++)
            {
                TrackedObjectBinding binding = trackedObjects[index];
                if (binding.Transform == null)
                {
                    continue;
                }

                Transform transform = binding.Transform;
                Rigidbody body = binding.Body;
                Vector3 velocity = body == null ? Vector3.zero : body.velocity;
                Vector3 angularVelocity = body == null ? Vector3.zero : body.angularVelocity;
                objects.WriteLine(Prefix(stamp) + "," +
                    CapturePathUtility.Csv(binding.ObjectId) + "," +
                    CapturePathUtility.Csv(binding.Category) + "," +
                    CapturePathUtility.Csv(binding.HierarchyPath) + "," +
                    (transform.gameObject.activeInHierarchy ? "1" : "0") + "," +
                    Vector(transform.position) + "," + QuaternionValue(transform.rotation) + "," +
                    Vector(velocity) + "," + Vector(angularVelocity));
                manifest.objectRows++;
            }

            if (hmdCamera != null)
            {
                devices.WriteLine(Prefix(stamp) + ",hmd_camera," +
                    Vector(hmdCamera.transform.position) + "," + QuaternionValue(hmdCamera.transform.rotation));
                manifest.deviceRows++;
            }

            for (int index = 0; index < statuses.Count; index++)
            {
                Hi5CaptureAdapter.SensorStatus status = statuses[index];
                sensorStatus.WriteLine(Prefix(stamp) + "," +
                    status.Hand + "," + status.Sensor + "," +
                    status.Online.ToString(CultureInfo.InvariantCulture) + "," +
                    status.Energy.ToString(CultureInfo.InvariantCulture) + "," +
                    status.Signal.ToString(CultureInfo.InvariantCulture) + "," +
                    status.MagneticQuality.ToString(CultureInfo.InvariantCulture));
                manifest.sensorStatusRows++;
            }
        }

        public void WriteRawImu(
            CaptureFrameStamp hostStamp,
            string providerId,
            IList<RawImuSample> samples)
        {
            if (rawImu == null)
            {
                return;
            }

            for (int index = 0; index < samples.Count; index++)
            {
                RawImuSample sample = samples[index];
                rawImu.WriteLine(Prefix(hostStamp) + "," +
                    CapturePathUtility.Csv(providerId) + "," +
                    CapturePathUtility.Csv(sample.Hand) + "," +
                    CapturePathUtility.Csv(sample.SensorId) + "," +
                    sample.SourceTimestampNanoseconds.ToString(CultureInfo.InvariantCulture) + "," +
                    sample.SourceSequence.ToString(CultureInfo.InvariantCulture) + "," +
                    CapturePathUtility.Float(sample.AccelXMetresPerSecondSquared) + "," +
                    CapturePathUtility.Float(sample.AccelYMetresPerSecondSquared) + "," +
                    CapturePathUtility.Float(sample.AccelZMetresPerSecondSquared) + "," +
                    CapturePathUtility.Float(sample.GyroXRadiansPerSecond) + "," +
                    CapturePathUtility.Float(sample.GyroYRadiansPerSecond) + "," +
                    CapturePathUtility.Float(sample.GyroZRadiansPerSecond) + "," +
                    CapturePathUtility.Float(sample.MagXTesla) + "," +
                    CapturePathUtility.Float(sample.MagYTesla) + "," +
                    CapturePathUtility.Float(sample.MagZTesla));
                manifest.rawImuRows++;
            }
        }

        public void WriteEvent(
            long eventId,
            long monotonicNanoseconds,
            string utcIso8601,
            int unityFrame,
            string eventType,
            string taskId,
            string objectId,
            string details)
        {
            events.WriteLine(string.Join(",", new[]
            {
                eventId.ToString(CultureInfo.InvariantCulture),
                monotonicNanoseconds.ToString(CultureInfo.InvariantCulture),
                (monotonicNanoseconds - context.StartMonotonicNanoseconds).ToString(CultureInfo.InvariantCulture),
                utcIso8601,
                unityFrame.ToString(CultureInfo.InvariantCulture),
                CapturePathUtility.Csv(eventType),
                CapturePathUtility.Csv(taskId),
                CapturePathUtility.Csv(objectId),
                CapturePathUtility.Csv(details)
            }));
            manifest.eventRows++;
        }

        public void FinalizeTrial(string outcome)
        {
            if (finalized)
            {
                return;
            }

            finalized = true;
            manifest.status = outcome;
            manifest.endUtc = CaptureClock.UtcNowIso8601;
            manifest.endMonotonicNs = CaptureClock.MonotonicNanoseconds;

            Complete(frames);
            Complete(handBones);
            Complete(jointAngles);
            Complete(objects);
            Complete(devices);
            Complete(sensorStatus);
            Complete(events);
            Complete(rawImu);

            if (rawImu != null && manifest.rawImuRows == 0)
            {
                manifest.rawImuStatus = "available_no_samples";
            }

            string videoPath = context.VideoOutputPath;
            if (context.Profile.recordVideo)
            {
                manifest.videoStatus = File.Exists(videoPath) ? "recorded" : "requested_but_missing";
            }

            BuildChecksums();
            WriteChecksumFile();
            WriteManifest("manifest.json");

            string partialManifest = Path.Combine(context.TrialDirectory, "manifest.partial.json");
            if (File.Exists(partialManifest))
            {
                File.Delete(partialManifest);
            }

            string incomplete = Path.Combine(context.TrialDirectory, "INCOMPLETE");
            if (File.Exists(incomplete))
            {
                File.Delete(incomplete);
            }

            string marker = outcome == "complete" ? "COMPLETE" : "ABORTED";
            File.WriteAllText(Path.Combine(context.TrialDirectory, marker),
                manifest.endUtc + Environment.NewLine,
                new UTF8Encoding(false));
        }

        public void Dispose()
        {
            if (finalized)
            {
                return;
            }

            Dispose(frames);
            Dispose(handBones);
            Dispose(jointAngles);
            Dispose(objects);
            Dispose(devices);
            Dispose(sensorStatus);
            Dispose(events);
            Dispose(rawImu);
        }

        private static string Prefix(CaptureFrameStamp stamp)
        {
            return stamp.SampleId.ToString(CultureInfo.InvariantCulture) + "," +
                   stamp.MonotonicNanoseconds.ToString(CultureInfo.InvariantCulture) + "," +
                   stamp.RelativeNanoseconds.ToString(CultureInfo.InvariantCulture);
        }

        private static string Vector(Vector3 value)
        {
            return CapturePathUtility.Float(value.x) + "," +
                   CapturePathUtility.Float(value.y) + "," +
                   CapturePathUtility.Float(value.z);
        }

        private static string QuaternionValue(Quaternion value)
        {
            return CapturePathUtility.Float(value.x) + "," +
                   CapturePathUtility.Float(value.y) + "," +
                   CapturePathUtility.Float(value.z) + "," +
                   CapturePathUtility.Float(value.w);
        }

        private static Vector3 SignedEuler(Vector3 value)
        {
            return new Vector3(SignedAngle(value.x), SignedAngle(value.y), SignedAngle(value.z));
        }

        private static float SignedAngle(float value)
        {
            return value > 180.0f ? value - 360.0f : value;
        }

        private static void Complete(AtomicCsv csv)
        {
            if (csv != null)
            {
                csv.Complete();
            }
        }

        private static void Dispose(AtomicCsv csv)
        {
            if (csv != null)
            {
                csv.Dispose();
            }
        }

        private void BuildChecksums()
        {
            manifest.files.Clear();
            string[] files = Directory.GetFiles(context.TrialDirectory, "*", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);
            for (int index = 0; index < files.Length; index++)
            {
                string file = files[index];
                if (file.EndsWith(".partial", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(file), "manifest.partial.json", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith("checksums.sha256", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(file), "INCOMPLETE", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                manifest.files.Add(new FileRecord
                {
                    relativePath = RelativePath(context.TrialDirectory, file),
                    bytes = new FileInfo(file).Length,
                    sha256 = Sha256(file)
                });
            }
        }

        private void WriteChecksumFile()
        {
            StringBuilder content = new StringBuilder();
            for (int index = 0; index < manifest.files.Count; index++)
            {
                FileRecord file = manifest.files[index];
                content.Append(file.sha256).Append("  ").Append(file.relativePath).AppendLine();
            }
            File.WriteAllText(Path.Combine(context.TrialDirectory, "checksums.sha256"),
                content.ToString(), new UTF8Encoding(false));
        }

        private void WriteManifest(string filename)
        {
            File.WriteAllText(
                Path.Combine(context.TrialDirectory, filename),
                JsonUtility.ToJson(manifest, true) + Environment.NewLine,
                new UTF8Encoding(false));
        }

        private static string Sha256(string path)
        {
            using (SHA256 algorithm = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = algorithm.ComputeHash(stream);
                StringBuilder result = new StringBuilder(hash.Length * 2);
                for (int index = 0; index < hash.Length; index++)
                {
                    result.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
                }
                return result.ToString();
            }
        }

        private static string RelativePath(string root, string path)
        {
            Uri rootUri = new Uri(AppendDirectorySeparator(root));
            Uri pathUri = new Uri(path);
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
        }

        private static string AppendDirectorySeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }
    }
}
