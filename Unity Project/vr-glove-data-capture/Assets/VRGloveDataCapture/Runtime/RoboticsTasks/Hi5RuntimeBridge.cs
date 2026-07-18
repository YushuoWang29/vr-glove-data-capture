using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VRGloveDataCapture.RoboticsTasks
{
    /// <summary>
    /// Connects the project-owned task code to the optional Hi5 SDK without
    /// creating an assembly definition dependency on the vendor source tree.
    /// </summary>
    internal static class Hi5RuntimeBridge
    {
        private const string ResetMessageKey = "messageObjectReset";
        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>();

        internal sealed class ResetSubscription : IDisposable
        {
            private readonly object messageBus;
            private readonly Delegate callback;
            private readonly MethodInfo unregisterMethod;

            internal ResetSubscription(object messageBus, Delegate callback, MethodInfo unregisterMethod)
            {
                this.messageBus = messageBus;
                this.callback = callback;
                this.unregisterMethod = unregisterMethod;
            }

            public void Dispose()
            {
                if (messageBus == null || callback == null || unregisterMethod == null)
                {
                    return;
                }

                try
                {
                    unregisterMethod.Invoke(messageBus, new object[] { callback, ResetMessageKey });
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[PickPlaceTasks] Could not unregister the Hi5 reset callback: " + exception.Message);
                }
            }
        }

        internal static bool IsSimpleObjectManagerReady()
        {
            Type managerType = FindType("Hi5_Interaction_Core.Hi5_Interaction_Simple_Object_Manager");
            MethodInfo getter = managerType == null
                ? null
                : managerType.GetMethod("GetObjectManager", BindingFlags.Public | BindingFlags.Static);

            try
            {
                return getter != null && getter.Invoke(null, null) != null;
            }
            catch
            {
                return false;
            }
        }

        internal static bool AddSimpleObjectComponents(GameObject target, int objectId, string objectName)
        {
            Type colliderType = FindType("Hi5_Interaction_Core.Hi5_Interaction_Item_Simple_Collider");
            Type propertyType = FindType("Hi5_Interaction_Core.Hi5_Simple_Object_Property");
            Type itemType = FindType("Hi5_Interaction_Core.Hi5_Glove_Interaction_Simple_Item");
            Type interfaceType = FindType("Hi5_Interaction_Interface.Hi5_Interface_Simple_Object");

            if (colliderType == null || propertyType == null || itemType == null || interfaceType == null)
            {
                Debug.LogError("[PickPlaceTasks] The Hi5 simple-object runtime types are unavailable.");
                return false;
            }

            try
            {
                GetOrAddComponent(target, colliderType);

                Component property = GetOrAddComponent(target, propertyType);
                SetPublicField(property, "IsPinch", true);
                SetPublicField(property, "IsPinchInHand", true);
                SetPublicField(property, "IsClap", true);
                SetPublicField(property, "IsLift", true);

                Component item = GetOrAddComponent(target, itemType);
                SetPublicField(item, "nameObject", objectName);
                SetPublicField(item, "idObject", objectId);
                SetPublicField(item, "IsChangeColor", false);

                GetOrAddComponent(target, interfaceType);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("[PickPlaceTasks] Failed to configure '" + objectName + "' for Hi5 interaction: " + exception);
                return false;
            }
        }

        internal static bool HasSimpleObjectComponents(GameObject target)
        {
            if (target == null)
            {
                return false;
            }

            Type interfaceType = FindType("Hi5_Interaction_Interface.Hi5_Interface_Simple_Object");
            return interfaceType != null && target.GetComponent(interfaceType) != null;
        }

        internal static ResetSubscription RegisterResetCallback(object target, string methodName)
        {
            Type messageType = FindType("Hi5_Interaction_Core.Hi5_Interaction_Message");
            if (messageType == null)
            {
                return null;
            }

            Type callbackType = messageType.GetNestedType("MessageFun", BindingFlags.Public);
            MethodInfo getInstance = messageType.GetMethod("GetInstance", BindingFlags.Public | BindingFlags.Static);
            MethodInfo register = messageType.GetMethod("RegisterMessage", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo unregister = messageType.GetMethod("UnRegisterMessage", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo callbackMethod = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);

            if (callbackType == null || getInstance == null || register == null || unregister == null || callbackMethod == null)
            {
                return null;
            }

            try
            {
                object messageBus = getInstance.Invoke(null, null);
                Delegate callback = Delegate.CreateDelegate(callbackType, target, callbackMethod);
                register.Invoke(messageBus, new object[] { callback, ResetMessageKey });
                return new ResetSubscription(messageBus, callback, unregister);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[PickPlaceTasks] Could not subscribe to the Hi5 reset message: " + exception.Message);
                return null;
            }
        }

        internal static bool DispatchReset()
        {
            Type messageType = FindType("Hi5_Interaction_Core.Hi5_Interaction_Message");
            MethodInfo getInstance = messageType == null
                ? null
                : messageType.GetMethod("GetInstance", BindingFlags.Public | BindingFlags.Static);
            MethodInfo dispatch = messageType == null
                ? null
                : messageType.GetMethod("DispenseMessage", BindingFlags.Public | BindingFlags.Instance);

            if (getInstance == null || dispatch == null)
            {
                return false;
            }

            try
            {
                object messageBus = getInstance.Invoke(null, null);
                dispatch.Invoke(messageBus, new object[] { ResetMessageKey, null, null, null, null });
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[PickPlaceTasks] Could not dispatch the Hi5 reset message: " + exception.Message);
                return false;
            }
        }

        private static void SetPublicField(Component component, string fieldName, object value)
        {
            FieldInfo field = component.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(component, value);
            }
        }

        private static Component GetOrAddComponent(GameObject target, Type componentType)
        {
            Component component = target.GetComponent(componentType);
            return component != null ? component : target.AddComponent(componentType);
        }

        private static Type FindType(string fullName)
        {
            Type cached;
            if (TypeCache.TryGetValue(fullName, out cached))
            {
                return cached;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int index = 0; index < assemblies.Length; index++)
            {
                Type type = assemblies[index].GetType(fullName, false);
                if (type != null)
                {
                    TypeCache[fullName] = type;
                    return type;
                }
            }

            return null;
        }
    }
}
