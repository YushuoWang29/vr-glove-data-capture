using UnityEngine;

namespace VRGloveDataCapture.MixedReality
{
    /// <summary>
    /// Adds the passthrough effect to the active stereo camera without editing
    /// proprietary vendor demo scenes. The effect remains off until P is pressed.
    /// </summary>
    public sealed class SteamVrPassthroughAutoInstaller : MonoBehaviour
    {
        private const string InstallerObjectName = "[VR Glove Passthrough Installer]";
        private static SteamVrPassthroughAutoInstaller instance;
        private SteamVrPassthroughEffect installedEffect;
        private float nextSearchTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (instance != null)
            {
                return;
            }

            GameObject installer = new GameObject(InstallerObjectName);
            installer.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(installer);
            installer.AddComponent<SteamVrPassthroughAutoInstaller>();
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

        private void Update()
        {
            if (installedEffect != null || Time.unscaledTime < nextSearchTime)
            {
                return;
            }

            nextSearchTime = Time.unscaledTime + 0.5f;
            Camera target = FindStereoCamera();
            if (target == null)
            {
                return;
            }

            installedEffect = target.GetComponent<SteamVrPassthroughEffect>();
            if (installedEffect == null)
            {
                installedEffect = target.gameObject.AddComponent<SteamVrPassthroughEffect>();
            }

            Debug.Log("[VRGloveDataCapture] Passthrough is ready on " +
                      target.name + "; press P to toggle it.", target);
        }

        private static Camera FindStereoCamera()
        {
            Camera main = Camera.main;
            if (IsUsable(main))
            {
                return main;
            }

            Camera[] cameras = FindObjectsOfType<Camera>();
            for (int i = 0; i < cameras.Length; i++)
            {
                if (IsUsable(cameras[i]) && cameras[i].stereoTargetEye != StereoTargetEyeMask.None)
                {
                    return cameras[i];
                }
            }

            for (int i = 0; i < cameras.Length; i++)
            {
                if (IsUsable(cameras[i]))
                {
                    return cameras[i];
                }
            }

            return null;
        }

        private static bool IsUsable(Camera camera)
        {
            return camera != null && camera.enabled && camera.gameObject.activeInHierarchy;
        }
    }
}
