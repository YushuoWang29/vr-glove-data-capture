using NUnit.Framework;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using VRGloveDataCapture.MixedReality;

namespace VRGloveDataCapture.Tests
{
    public sealed class OpenVrLifecycleSmokeTests
    {
        [Test]
        public void PresenceProbeNeverClosesTheActiveXrSceneSession()
        {
            XRManagerSettings manager = XRGeneralSettings.Instance == null
                ? null
                : XRGeneralSettings.Instance.Manager;
            XRLoader loaderBefore = manager == null ? null : manager.activeLoader;
            XRDisplaySubsystem displayBefore = loaderBefore == null
                ? null
                : loaderBefore.GetLoadedSubsystem<XRDisplaySubsystem>();
            bool displayWasRunning = displayBefore != null && displayBefore.running;
            bool openVrSystemWasAvailable =
                displayWasRunning && Valve.VR.OpenVR.System != null;
            bool compositorWasAvailable =
                displayWasRunning && Valve.VR.OpenVR.Compositor != null;

            string detail;
            SteamVrRuntimeProbe.TryGetReady(out detail);

            Assert.AreSame(loaderBefore, manager == null ? null : manager.activeLoader,
                "The hardware-presence probe changed the XR Management loader.");

            if (displayBefore != null)
            {
                Assert.AreEqual(displayWasRunning, displayBefore.running,
                    "The hardware-presence probe stopped the XR display subsystem.");
            }

            if (openVrSystemWasAvailable)
            {
                Assert.IsNotNull(Valve.VR.OpenVR.System,
                    "The hardware-presence probe closed the active OpenVR system session.");
            }

            if (compositorWasAvailable)
            {
                Assert.IsNotNull(Valve.VR.OpenVR.Compositor,
                    "The hardware-presence probe closed the active OpenVR compositor session.");
            }
        }
    }
}
