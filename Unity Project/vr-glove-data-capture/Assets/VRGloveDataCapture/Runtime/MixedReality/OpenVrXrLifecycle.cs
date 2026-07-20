using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace VRGloveDataCapture.MixedReality
{
    /// <summary>
    /// Repairs the project state observed in Unity 2019 Editor where XR Management reaches the
    /// first scene without an active loader. Normal XR Management startup remains the primary path.
    /// </summary>
    public static class OpenVrXrLifecycle
    {
        private const string LogPrefix = "[XRBootstrap] ";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureOpenVrBeforeFirstScene()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            XRGeneralSettings settings = XRGeneralSettings.Instance;
            if (settings == null)
            {
                Debug.LogError(LogPrefix +
                               "XRGeneralSettings.Instance is null. OpenVR cannot start; verify the Standalone " +
                               "XR Plug-in Management configuration.");
                return;
            }

            XRManagerSettings manager = settings.Manager;
            if (manager == null)
            {
                Debug.LogError(LogPrefix +
                               "The Standalone XR settings have no manager. OpenVR cannot start.");
                return;
            }

            bool fallbackOwnsLifecycle = false;
            if (manager.activeLoader == null)
            {
                Debug.LogWarning(LogPrefix +
                                 "Automatic XR startup did not create a loader. Retrying once before the first scene.");
                manager.InitializeLoaderSync();
                fallbackOwnsLifecycle = manager.activeLoader != null;
            }

            XRLoader loader = manager.activeLoader;
            if (loader == null)
            {
                Debug.LogError(LogPrefix +
                               "OpenVR Loader initialization failed. SteamVR must be Ready before entering Play Mode. " +
                               "Exit Play Mode, restart SteamVR if necessary, and try again.");
                return;
            }

            XRDisplaySubsystem display = loader.GetLoadedSubsystem<XRDisplaySubsystem>();
            XRInputSubsystem input = loader.GetLoadedSubsystem<XRInputSubsystem>();
            if (display == null || input == null)
            {
                Debug.LogError(LogPrefix + "The active XR loader did not provide both display and input subsystems.");
                InstallFallbackCleanupIfNeeded(manager, fallbackOwnsLifecycle);
                return;
            }

            if (!display.running || !input.running)
            {
                manager.StartSubsystems();
                fallbackOwnsLifecycle = true;
                display = loader.GetLoadedSubsystem<XRDisplaySubsystem>();
                input = loader.GetLoadedSubsystem<XRInputSubsystem>();
            }

            InstallFallbackCleanupIfNeeded(manager, fallbackOwnsLifecycle);

            bool displayRunning = display != null && display.running;
            bool inputRunning = input != null && input.running;
            if (!displayRunning || !inputRunning)
            {
                Debug.LogError(LogPrefix +
                               "OpenVR Loader exists, but its subsystems did not both start. " +
                               "display=" + displayRunning + ", input=" + inputRunning + ".");
                return;
            }

            Debug.Log(LogPrefix + "XR scene session is running. Loader=" + loader.name +
                      ", display=" + displayRunning + ", input=" + inputRunning + ".");
        }

        private static void InstallFallbackCleanupIfNeeded(XRManagerSettings manager, bool fallbackOwnsLifecycle)
        {
            if (!fallbackOwnsLifecycle)
            {
                return;
            }

            GameObject ownerObject = new GameObject("[XRBootstrap Lifecycle]");
            ownerObject.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(ownerObject);
            ownerObject.AddComponent<OpenVrXrFallbackLifecycleOwner>().Initialize(manager);
        }
    }

    /// <summary>
    /// Cleans up only sessions initialized by the fallback path. It deliberately delegates shutdown
    /// to XR Management instead of calling OpenVR.Shutdown directly.
    /// </summary>
    internal sealed class OpenVrXrFallbackLifecycleOwner : MonoBehaviour
    {
        private XRManagerSettings manager;
        private bool cleanedUp;

        internal void Initialize(XRManagerSettings lifecycleManager)
        {
            manager = lifecycleManager;
        }

        private void OnApplicationQuit()
        {
            Cleanup();
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        private void Cleanup()
        {
            if (cleanedUp || manager == null)
            {
                return;
            }

            cleanedUp = true;
            if (manager.activeLoader != null && manager.isInitializationComplete)
            {
                manager.DeinitializeLoader();
                Debug.Log("[XRBootstrap] Fallback XR scene session stopped and deinitialized cleanly.");
            }
        }
    }
}
