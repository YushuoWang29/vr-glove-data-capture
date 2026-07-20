using System;
using System.Collections;
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
        private const string UnPinch2MessageKey = "messageUnPinchObject2";
        private const string VisibleHandTypeName = "Hi5_Interaction_Core.Hi5_Hand_Visible_Hand";
        private const string InteractionHandTypeName = "Hi5_Interaction_Core.Hi5_Glove_Interaction_Hand";
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
                // Task objects use one deterministic contact-grab path. The
                // vendor clap/lift paths can independently parent an object and
                // make a deliberate pinch release appear to remain attached.
                SetPublicField(property, "IsClap", false);
                SetPublicField(property, "IsLift", false);

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

        internal static bool IsRegisteredSimpleObject(GameObject target, int objectId)
        {
            Type managerType = FindType("Hi5_Interaction_Core.Hi5_Interaction_Simple_Object_Manager");
            Type itemType = FindType("Hi5_Interaction_Core.Hi5_Glove_Interaction_Simple_Item");
            MethodInfo getter = managerType == null
                ? null
                : managerType.GetMethod("Get_ItemId", BindingFlags.Public | BindingFlags.Static);
            if (getter == null || itemType == null || target == null)
            {
                return false;
            }

            try
            {
                object registered = getter.Invoke(null, new object[] { objectId });
                UnityEngine.Object registeredObject = registered as UnityEngine.Object;
                UnityEngine.Object targetObject = target.GetComponent(itemType) as UnityEngine.Object;
                return registeredObject != null && registeredObject == targetObject;
            }
            catch
            {
                return false;
            }
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

        /// <summary>
        /// Reads the vendor Pinch2 state's source interaction-finger tips. This
        /// observes the physical Hi5 state only; it never writes hand bones,
        /// calibration values, or capture streams.
        /// </summary>
        internal static bool TryGetPinch2Separation(
            Transform heldParent,
            int objectId,
            out Component interactionHand,
            out float separation)
        {
            interactionHand = FindInteractionHand(heldParent);
            separation = 0.0f;
            if (interactionHand == null)
            {
                return false;
            }

            try
            {
                FieldInfo pinchObjectIdField = FindField(
                    interactionHand.GetType(),
                    "mPinchObjectId");
                FieldInfo stateField = FindField(interactionHand.GetType(), "mState");
                if (pinchObjectIdField == null || stateField == null ||
                    (int)pinchObjectIdField.GetValue(interactionHand) != objectId)
                {
                    return false;
                }

                object state = stateField.GetValue(interactionHand);
                PropertyInfo stateProperty = state == null
                    ? null
                    : state.GetType().GetProperty(
                        "State",
                        BindingFlags.Public | BindingFlags.Instance);
                object stateValue = stateProperty == null ? null : stateProperty.GetValue(state, null);
                if (stateValue == null || stateValue.ToString() != "EPinch2")
                {
                    return false;
                }

                MethodInfo getPinch2 = state.GetType().GetMethod(
                    "GetPinch2",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                object pinch2 = getPinch2 == null ? null : getPinch2.Invoke(state, null);
                FieldInfo collidersField = pinch2 == null
                    ? null
                    : FindField(pinch2.GetType(), "fingerColliders");
                IDictionary colliders = collidersField == null
                    ? null
                    : collidersField.GetValue(pinch2) as IDictionary;
                if (colliders == null || colliders.Count < 2)
                {
                    return false;
                }

                bool hasThumb = false;
                bool hasOther = false;
                Vector3 thumbTip = Vector3.zero;
                Vector3 otherTip = Vector3.zero;
                foreach (DictionaryEntry entry in colliders)
                {
                    object finger = entry.Value;
                    MethodInfo getTailPosition = finger == null
                        ? null
                        : finger.GetType().GetMethod(
                            "GetTailPosition",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                    if (getTailPosition == null)
                    {
                        continue;
                    }

                    Vector3 tip = (Vector3)getTailPosition.Invoke(finger, null);
                    string fingerName = entry.Key == null ? string.Empty : entry.Key.ToString();
                    if (fingerName == "EThumb")
                    {
                        thumbTip = tip;
                        hasThumb = true;
                    }
                    else if (!hasOther || fingerName == "EIndex")
                    {
                        otherTip = tip;
                        hasOther = true;
                        if (fingerName == "EIndex" && hasThumb)
                        {
                            // Index is the preferred partner when several
                            // fingers participated in the vendor pinch state.
                            continue;
                        }
                    }
                }

                if (!hasThumb || !hasOther)
                {
                    return false;
                }

                separation = Vector3.Distance(thumbTip, otherTip);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[PickPlaceTasks] Could not inspect the Hi5 Pinch2 state: " +
                    exception.Message);
                return false;
            }
        }

        /// <summary>
        /// Compatibility path for the Hi5 2.0 Pinch2 early-return defect. It
        /// executes the same release state change and message that the vendor
        /// state intended to execute after detecting an opened pinch.
        /// </summary>
        internal static bool ForceReleasePinch2(Component interactionHand, int objectId)
        {
            if (interactionHand == null)
            {
                return false;
            }

            try
            {
                FieldInfo pinchObjectIdField = FindField(
                    interactionHand.GetType(),
                    "mPinchObjectId");
                FieldInfo stateField = FindField(interactionHand.GetType(), "mState");
                if (pinchObjectIdField == null || stateField == null ||
                    (int)pinchObjectIdField.GetValue(interactionHand) != objectId)
                {
                    return false;
                }

                object state = stateField.GetValue(interactionHand);
                PropertyInfo stateProperty = state == null
                    ? null
                    : state.GetType().GetProperty(
                        "State",
                        BindingFlags.Public | BindingFlags.Instance);
                object stateValue = stateProperty == null ? null : stateProperty.GetValue(state, null);
                if (stateValue == null || stateValue.ToString() != "EPinch2")
                {
                    return false;
                }

                Type stateEnumType = stateProperty.PropertyType;
                object releaseState = Enum.Parse(stateEnumType, "ERelease");
                MethodInfo changeState = state.GetType().GetMethod(
                    "ChangeState",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (changeState == null)
                {
                    return false;
                }

                changeState.Invoke(state, new[] { releaseState });
                if (!DispatchInteractionMessage(
                        UnPinch2MessageKey,
                        objectId,
                        interactionHand))
                {
                    return false;
                }

                pinchObjectIdField.SetValue(interactionHand, -1);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[PickPlaceTasks] Hi5 Pinch2 compatibility release failed: " +
                    exception.Message);
                return false;
            }
        }

        private static bool DispatchInteractionMessage(
            string messageKey,
            object parameter1,
            object parameter2)
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

            object messageBus = getInstance.Invoke(null, null);
            dispatch.Invoke(
                messageBus,
                new[] { (object)messageKey, parameter1, parameter2, null, null });
            return true;
        }

        private static Component FindInteractionHand(Transform candidate)
        {
            Type visibleHandType = FindType(VisibleHandTypeName);
            Type interactionHandType = FindType(InteractionHandTypeName);
            if (visibleHandType == null || interactionHandType == null)
            {
                return null;
            }

            for (Transform current = candidate; current != null; current = current.parent)
            {
                Component visibleHand = current.GetComponent(visibleHandType);
                if (visibleHand == null)
                {
                    continue;
                }

                FieldInfo gloveField = FindField(visibleHandType, "mGlove_Hand");
                Component sourceHand = gloveField == null
                    ? null
                    : gloveField.GetValue(visibleHand) as Component;
                return sourceHand == null
                    ? null
                    : sourceHand.GetComponent(interactionHandType);
            }

            return null;
        }

        private static void SetPublicField(Component component, string fieldName, object value)
        {
            FieldInfo field = component.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(component, value);
            }
        }

        private static FieldInfo FindField(Type type, string fieldName)
        {
            const BindingFlags flags =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(fieldName, flags);
                if (field != null)
                {
                    return field;
                }
            }

            return null;
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
