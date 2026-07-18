using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRGloveDataCapture.HandInteraction
{
    /// <summary>
    /// Adds the visual-only contact solver to every Hi5 visible hand at runtime.
    /// The vendor prefabs and scenes remain untouched, so the feature also applies
    /// to future task setup scenes that load the standard Hi5 hand prefabs.
    /// </summary>
    [DefaultExecutionOrder(-31000)]
    public sealed class AdaptiveHandContactAutoInstaller : MonoBehaviour
    {
        private const string RuntimeObjectName = "[Adaptive Hand Contact Runtime]";
        private const string VisibleHandTypeName = "Hi5_Interaction_Core.Hi5_Hand_Visible_Hand";

        private static AdaptiveHandContactAutoInstaller instance;
        private Type visibleHandType;
        private float nextScanTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (instance != null)
            {
                return;
            }

            GameObject runtimeObject = new GameObject(RuntimeObjectName);
            runtimeObject.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(runtimeObject);
            runtimeObject.AddComponent<AdaptiveHandContactAutoInstaller>();
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
            SceneManager.sceneLoaded += OnSceneLoaded;
            nextScanTime = 0.0f;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextScanTime)
            {
                return;
            }

            nextScanTime = Time.unscaledTime + 0.5f;
            InstallOnVisibleHands();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            nextScanTime = 0.0f;
        }

        private void InstallOnVisibleHands()
        {
            if (visibleHandType == null)
            {
                visibleHandType = FindType(VisibleHandTypeName);
            }

            if (visibleHandType == null)
            {
                return;
            }

            UnityEngine.Object[] visibleHands = FindObjectsOfType(visibleHandType);
            for (int index = 0; index < visibleHands.Length; index++)
            {
                Component visibleHand = visibleHands[index] as Component;
                if (visibleHand == null || !visibleHand.gameObject.scene.IsValid())
                {
                    continue;
                }

                AdaptiveHandContactController controller =
                    visibleHand.GetComponent<AdaptiveHandContactController>();
                if (controller == null)
                {
                    controller = visibleHand.gameObject.AddComponent<AdaptiveHandContactController>();
                }

                controller.Configure(visibleHand);
            }
        }

        private static Type FindType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int index = 0; index < assemblies.Length; index++)
            {
                Type type = assemblies[index].GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }
    }
}
