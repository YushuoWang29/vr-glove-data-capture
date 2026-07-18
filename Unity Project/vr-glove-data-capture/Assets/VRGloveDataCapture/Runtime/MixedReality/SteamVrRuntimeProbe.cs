using System;
using Valve.VR;

namespace VRGloveDataCapture.MixedReality
{
    /// <summary>Performs a short, side-effect-free OpenVR readiness probe before Play Mode.</summary>
    public static class SteamVrRuntimeProbe
    {
        public static bool TryGetReady(out string detail)
        {
            try
            {
                if (!OpenVR.IsRuntimeInstalled())
                {
                    detail = "The OpenVR runtime is not installed or is not registered.";
                    return false;
                }

                if (!OpenVR.IsHmdPresent())
                {
                    detail = "SteamVR cannot currently see an HMD. Check the VIVE Console/link box and wait for SteamVR Ready.";
                    return false;
                }

                EVRInitError error = EVRInitError.None;
                CVRSystem system = null;
                bool initialized = false;
                try
                {
                    system = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Background);
                    initialized = error == EVRInitError.None && system != null;
                    if (!initialized)
                    {
                        detail = "OpenVR initialization failed: " + error + " (" + (int)error + "). " +
                                 "Wait until SteamVR reports Ready before entering Play Mode.";
                        return false;
                    }

                    if (!system.IsTrackedDeviceConnected(OpenVR.k_unTrackedDeviceIndex_Hmd))
                    {
                        detail = "OpenVR started, but the HMD is not connected yet. Wait until SteamVR reports Ready.";
                        return false;
                    }

                    detail = "SteamVR/OpenVR is ready and the HMD is connected.";
                    return true;
                }
                finally
                {
                    if (initialized)
                    {
                        OpenVR.Shutdown();
                    }
                }
            }
            catch (Exception exception)
            {
                detail = "OpenVR readiness probe failed: " + exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }
    }
}
