using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace VRGloveDataCapture.EditorTools
{
    /// <summary>
    /// Stops the Hi5 managed polling thread before the vendor shutdown path
    /// unloads its native dongle. The original SDK does those operations in the
    /// reverse order, which can race ReadBVHData against StopHI5Dongle and crash
    /// Unity 2019 while leaving Play Mode.
    /// </summary>
    [InitializeOnLoad]
    internal static class Hi5EditorPlayModeShutdownGuard
    {
        private const string ManagerTypeName = "HI5.HI5_Manager_Thread";
        private static bool shutdownPrepared;

        static Hi5EditorPlayModeShutdownGuard()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                shutdownPrepared = false;
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                PrepareHi5ForShutdown("leaving Play Mode");
            }
        }

        private static void OnBeforeAssemblyReload()
        {
            if (EditorApplication.isPlaying)
            {
                PrepareHi5ForShutdown("reloading assemblies during Play Mode");
            }
        }

        private static void PrepareHi5ForShutdown(string reason)
        {
            if (shutdownPrepared)
            {
                return;
            }

            Type managerType = FindType(ManagerTypeName);
            if (managerType == null)
            {
                return;
            }

            FieldInfo instanceField = managerType.GetField(
                "_instance",
                BindingFlags.Static | BindingFlags.NonPublic);
            object manager = instanceField == null ? null : instanceField.GetValue(null);
            if (manager == null)
            {
                return;
            }

            shutdownPrepared = true;
            try
            {
                // This Join must happen before RequestCloseConnect reaches the
                // native StopHI5Dongle call. Do not call Instance(): doing so could
                // create a new manager while the editor is already shutting down.
                InvokeRequired(managerType, manager, "StopThread");
                InvokeOptionalStatusTimerCleanup(managerType);
                InvokeRequired(managerType, manager, "RequestCloseConnect");
                Debug.Log(
                    "[VRGloveDataCapture] Hi5 polling stopped safely before " + reason + ".");
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[VRGloveDataCapture] Could not prepare Hi5 for safe shutdown: " +
                    Unwrap(exception));
            }
        }

        private static void InvokeOptionalStatusTimerCleanup(Type managerType)
        {
            FieldInfo statusField = managerType.GetField(
                "m_HI5Status",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            object status = statusField == null ? null : statusField.GetValue(null);
            if (status == null)
            {
                return;
            }

            MethodInfo cleanup = status.GetType().GetMethod(
                "ReleaseBothTimerThread",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (cleanup != null)
            {
                cleanup.Invoke(status, null);
            }
        }

        private static void InvokeRequired(Type type, object instance, string methodName)
        {
            MethodInfo method = type.GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException(type.FullName, methodName);
            }

            method.Invoke(instance, null);
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

        private static Exception Unwrap(Exception exception)
        {
            TargetInvocationException invocation = exception as TargetInvocationException;
            return invocation != null && invocation.InnerException != null
                ? invocation.InnerException
                : exception;
        }
    }
}
