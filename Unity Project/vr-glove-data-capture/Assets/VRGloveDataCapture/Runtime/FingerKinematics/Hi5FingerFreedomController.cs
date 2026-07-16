using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRGloveDataCapture.FingerKinematics
{
    /// <summary>
    /// Enables the Hi5 native finger abduction/adduction solver without taking
    /// a compile-time dependency on the locally installed proprietary SDK.
    /// </summary>
    [DefaultExecutionOrder(-32000)]
    public sealed class Hi5FingerFreedomController : MonoBehaviour
    {
        private const string RuntimeObjectName = "[VR Glove Data Capture Runtime]";
        private const string ThreadBehaviourTypeName = "HI5.Hi5_Thread_MonoBehaviour";
        private const string ManagerTypeName = "HI5.HI5_Manager_Thread";
        private const string DeviceTypeName = "HI5.HI5_Device";

        private static Hi5FingerFreedomController instance;

        [SerializeField]
        [Tooltip("When enabled, Hi5 finger yaw is free. Internally this sets the vendor 'finger ADB fixed' flag to false.")]
        private bool enableAbductionAdduction = true;

        [SerializeField]
        [Min(0.1f)]
        private float retryIntervalSeconds = 1.0f;

        private float nextRetryTime;
        private bool modeApplied;
        private string status = "Waiting for the Hi5 runtime.";

        public bool EnableAbductionAdduction
        {
            get { return enableAbductionAdduction; }
        }

        public bool ModeApplied
        {
            get { return modeApplied; }
        }

        public string Status
        {
            get { return status; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InstallRuntimeController()
        {
            if (instance != null)
            {
                return;
            }

            GameObject runtimeObject = new GameObject(RuntimeObjectName);
            runtimeObject.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(runtimeObject);
            runtimeObject.AddComponent<Hi5FingerFreedomController>();
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
            nextRetryTime = 0.0f;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void Update()
        {
            if (modeApplied || Time.unscaledTime < nextRetryTime)
            {
                return;
            }

            nextRetryTime = Time.unscaledTime + retryIntervalSeconds;
            TryApplyMode();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            modeApplied = false;
            nextRetryTime = 0.0f;
        }

        /// <summary>
        /// Changes the native Hi5 finger-yaw mode at runtime. This method is
        /// suitable for wiring to a UI toggle.
        /// </summary>
        public void SetAbductionAdductionEnabled(bool value)
        {
            enableAbductionAdduction = value;
            modeApplied = false;
            nextRetryTime = 0.0f;
            TryApplyMode();
        }

        [ContextMenu("Apply Hi5 finger freedom mode now")]
        public void TryApplyMode()
        {
            bool fixedMode = !enableAbductionAdduction;

            try
            {
                Type threadType = FindType(ThreadBehaviourTypeName);
                Type managerType = FindType(ManagerTypeName);
                Type deviceType = FindType(DeviceTypeName);

                if (threadType == null || managerType == null || deviceType == null)
                {
                    status = "Hi5 SDK is not loaded; waiting for a scene containing the local vendor import.";
                    return;
                }

                UnityEngine.Object threadBehaviour = FindObjectOfType(threadType);
                if (threadBehaviour != null)
                {
                    SetBooleanField(threadBehaviour, threadType, "isEnableFingerFixed", fixedMode);
                }

                MethodInfo instanceMethod = managerType.GetMethod(
                    "Instance",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object manager = instanceMethod == null ? null : instanceMethod.Invoke(null, null);
                if (manager != null)
                {
                    SetBooleanField(manager, managerType, "enableFingerAdbFixed", fixedMode);
                }

                MethodInfo enableMethod = deviceType.GetMethod(
                    "EnableFingerAdbFixed",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (enableMethod == null)
                {
                    status = "The installed Hi5 SDK does not expose EnableFingerAdbFixed(bool).";
                    return;
                }

                enableMethod.Invoke(null, new object[] { fixedMode });
                modeApplied = true;
                status = enableAbductionAdduction
                    ? "Hi5 finger abduction/adduction is enabled (fixed mode is off)."
                    : "Hi5 finger abduction/adduction is fixed by the vendor solver.";
                Debug.Log("[VRGloveDataCapture] " + status);
            }
            catch (TargetInvocationException exception)
            {
                modeApplied = false;
                Exception cause = exception.InnerException ?? exception;
                status = "Hi5 rejected the finger freedom setting: " + cause.Message;
                Debug.LogWarning("[VRGloveDataCapture] " + status);
            }
            catch (Exception exception)
            {
                modeApplied = false;
                status = "Could not apply the Hi5 finger freedom setting: " + exception.Message;
                Debug.LogWarning("[VRGloveDataCapture] " + status);
            }
        }

        private static void SetBooleanField(object target, Type targetType, string fieldName, bool value)
        {
            FieldInfo field = targetType.GetField(
                fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && field.FieldType == typeof(bool))
            {
                field.SetValue(target, value);
            }
        }

        private static Type FindType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }
    }
}
