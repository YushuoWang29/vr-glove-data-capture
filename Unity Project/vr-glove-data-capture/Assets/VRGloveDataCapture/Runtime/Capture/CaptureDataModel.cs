using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace VRGloveDataCapture.Capture
{
    [Serializable]
    public sealed class CaptureProfile
    {
        public string participantId = "P000";
        public string sessionLabel = "pilot";
        public string taskId = "pick-place";
        public string condition = "baseline";
        public string operatorId = "";
        public string notes = "";
        public int sampleRateHz = 60;
        public bool recordVideo = true;
        public bool captureRawImuIfAvailable = true;
        public bool requireHandTracking = true;

        public CaptureProfile Copy()
        {
            return new CaptureProfile
            {
                participantId = participantId,
                sessionLabel = sessionLabel,
                taskId = taskId,
                condition = condition,
                operatorId = operatorId,
                notes = notes,
                sampleRateHz = sampleRateHz,
                recordVideo = recordVideo,
                captureRawImuIfAvailable = captureRawImuIfAvailable,
                requireHandTracking = requireHandTracking
            };
        }

        internal void Normalize()
        {
            participantId = CapturePathUtility.SanitizeSegment(participantId, "P000");
            sessionLabel = CapturePathUtility.SanitizeSegment(sessionLabel, "session");
            taskId = CapturePathUtility.SanitizeSegment(taskId, "task");
            condition = CapturePathUtility.SanitizeSegment(condition, "unspecified");
            operatorId = CapturePathUtility.SanitizeOptionalSegment(operatorId);
            sampleRateHz = Math.Max(1, Math.Min(120, sampleRateHz));
            notes = notes ?? string.Empty;
        }
    }

    public sealed class CaptureTrialContext
    {
        public string SessionDirectory { get; private set; }
        public string TrialDirectory { get; private set; }
        public string VideoOutputPath { get; private set; }
        public CaptureProfile Profile { get; private set; }
        public int TrialIndex { get; private set; }
        public long StartMonotonicNanoseconds { get; private set; }
        public string StartUtcIso8601 { get; private set; }

        internal CaptureTrialContext(
            string sessionDirectory,
            string trialDirectory,
            CaptureProfile profile,
            int trialIndex,
            long startMonotonicNanoseconds,
            string startUtcIso8601)
        {
            SessionDirectory = sessionDirectory;
            TrialDirectory = trialDirectory;
            VideoOutputPath = Path.Combine(trialDirectory, "video", "vr_view.mp4");
            Profile = profile.Copy();
            TrialIndex = trialIndex;
            StartMonotonicNanoseconds = startMonotonicNanoseconds;
            StartUtcIso8601 = startUtcIso8601;
        }
    }

    public static class CaptureClock
    {
        private static readonly Stopwatch MonotonicClock = Stopwatch.StartNew();

        public static long MonotonicNanoseconds
        {
            get
            {
                return (long)((double)MonotonicClock.ElapsedTicks /
                    Stopwatch.Frequency * 1000000000.0);
            }
        }

        public static string UtcNowIso8601
        {
            get { return DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture); }
        }
    }

    public static class CapturePathUtility
    {
        private const int MaximumSegmentLength = 64;

        public static string SanitizeSegment(string value, string fallback)
        {
            string sanitized = SanitizeOptionalSegment(value);
            return string.IsNullOrEmpty(sanitized) ? fallback : sanitized;
        }

        public static string SanitizeOptionalSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder result = new StringBuilder(value.Length);
            bool previousWasSeparator = false;
            string trimmed = value.Trim();

            for (int index = 0; index < trimmed.Length && result.Length < MaximumSegmentLength; index++)
            {
                char character = trimmed[index];
                bool isInvalid = Array.IndexOf(invalid, character) >= 0 || char.IsControl(character);
                bool isSeparator = isInvalid || char.IsWhiteSpace(character) || character == '/';

                if (isSeparator)
                {
                    if (!previousWasSeparator && result.Length > 0)
                    {
                        result.Append('-');
                        previousWasSeparator = true;
                    }
                    continue;
                }

                result.Append(character);
                previousWasSeparator = false;
            }

            return result.ToString().Trim('-', '.', '_');
        }

        public static string Csv(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        internal static string Float(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        internal static string Double(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }

    public static class CaptureLifecycle
    {
        public static event Action<CaptureTrialContext> TrialStarted;
        public static event Action<CaptureTrialContext> TrialStopping;
        public static event Action<CaptureTrialContext> TrialStopped;

        internal static void RaiseTrialStarted(CaptureTrialContext context)
        {
            Action<CaptureTrialContext> handler = TrialStarted;
            if (handler != null)
            {
                handler(context);
            }
        }

        internal static void RaiseTrialStopping(CaptureTrialContext context)
        {
            Action<CaptureTrialContext> handler = TrialStopping;
            if (handler != null)
            {
                handler(context);
            }
        }

        internal static void RaiseTrialStopped(CaptureTrialContext context)
        {
            Action<CaptureTrialContext> handler = TrialStopped;
            if (handler != null)
            {
                handler(context);
            }
        }
    }

    public static class CaptureEventBus
    {
        public static event Action<string, string, string, string> EventPublished;

        public static void Publish(string eventType, string taskId, string objectId, string details)
        {
            Action<string, string, string, string> handler = EventPublished;
            if (handler != null)
            {
                handler(
                    eventType ?? "event",
                    taskId ?? string.Empty,
                    objectId ?? string.Empty,
                    details ?? string.Empty);
            }
        }

        public static void AddManualMarker(string label)
        {
            Publish("manual_marker", string.Empty, string.Empty, label ?? "marker");
        }
    }

    public struct RawImuSample
    {
        public string Hand;
        public string SensorId;
        public long SourceTimestampNanoseconds;
        public long SourceSequence;
        public float AccelXMetresPerSecondSquared;
        public float AccelYMetresPerSecondSquared;
        public float AccelZMetresPerSecondSquared;
        public float GyroXRadiansPerSecond;
        public float GyroYRadiansPerSecond;
        public float GyroZRadiansPerSecond;
        public float MagXTesla;
        public float MagYTesla;
        public float MagZTesla;
    }

    /// <summary>
    /// Vendor or transport adapters implement this interface when they can expose
    /// actual, calibrated-or-uncalibrated nine-axis values. Solved bone rotations
    /// must never be registered as raw IMU samples.
    /// </summary>
    public interface IRawImuProvider
    {
        string ProviderId { get; }
        bool IsAvailable { get; }
        void DrainSamples(List<RawImuSample> destination);
    }

    public static class RawImuProviderRegistry
    {
        private static readonly List<IRawImuProvider> Providers = new List<IRawImuProvider>();

        public static void Register(IRawImuProvider provider)
        {
            if (provider != null && !Providers.Contains(provider))
            {
                Providers.Add(provider);
            }
        }

        public static void Unregister(IRawImuProvider provider)
        {
            Providers.Remove(provider);
        }

        internal static List<IRawImuProvider> GetAvailableProviders()
        {
            List<IRawImuProvider> available = new List<IRawImuProvider>();
            for (int index = Providers.Count - 1; index >= 0; index--)
            {
                IRawImuProvider provider = Providers[index];
                if (provider == null)
                {
                    Providers.RemoveAt(index);
                }
                else if (provider.IsAvailable)
                {
                    available.Add(provider);
                }
            }

            return available;
        }
    }
}
