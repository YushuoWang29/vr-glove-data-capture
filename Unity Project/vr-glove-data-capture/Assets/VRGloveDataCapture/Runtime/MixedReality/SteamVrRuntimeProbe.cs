using System;
using Valve.VR;

namespace VRGloveDataCapture.MixedReality
{
    /// <summary>
    /// Checks whether OpenVR and an HMD are present without opening or closing an OpenVR session.
    /// The XR loader is the sole owner of the scene session once Play Mode starts.
    /// </summary>
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

                detail = "OpenVR is installed and an HMD is present. " +
                         "The OpenVR XR Loader will create the scene session when Play Mode starts.";
                return true;
            }
            catch (Exception exception)
            {
                detail = "OpenVR presence check failed: " + exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }
    }
}
