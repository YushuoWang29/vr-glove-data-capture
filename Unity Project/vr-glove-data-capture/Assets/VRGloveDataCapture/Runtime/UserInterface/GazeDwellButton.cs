using System;
using System.Reflection;
using UnityEngine;

namespace VRGloveDataCapture.UserInterface
{
    /// <summary>
    /// Adapts a project-owned action to the vendor VRInteractiveItem and
    /// SelectionRadial events without adding a compile-time Hi5 SDK dependency.
    /// </summary>
    public sealed class GazeDwellButton : MonoBehaviour
    {
        private Component interactiveItem;
        private Component selectionRadial;
        private Action selected;
        private EventInfo overEvent;
        private EventInfo outEvent;
        private EventInfo selectionCompleteEvent;
        private Action overHandler;
        private Action outHandler;
        private Action selectionCompleteHandler;
        private bool isOver;
        private bool armed;
        private bool wired;

        public string ActionId { get; private set; }
        public bool IsGazeOver { get { return isOver; } }

        public void Configure(
            Component vendorInteractiveItem,
            Component vendorSelectionRadial,
            string actionId,
            Action onSelected)
        {
            Unwire();
            interactiveItem = vendorInteractiveItem;
            selectionRadial = vendorSelectionRadial;
            ActionId = actionId ?? string.Empty;
            selected = onSelected;

            if (isActiveAndEnabled)
            {
                Wire();
            }
        }

        private void OnEnable()
        {
            Wire();
        }

        private void OnDisable()
        {
            Unwire();
            isOver = false;
            armed = false;
        }

        private void Wire()
        {
            if (wired || interactiveItem == null || selectionRadial == null)
            {
                return;
            }

            overEvent = interactiveItem.GetType().GetEvent("OnOver");
            outEvent = interactiveItem.GetType().GetEvent("OnOut");
            selectionCompleteEvent = selectionRadial.GetType().GetEvent("OnSelectionComplete");
            if (overEvent == null || outEvent == null || selectionCompleteEvent == null)
            {
                Debug.LogError("[GazeControlPanel] Vendor gaze events are unavailable.", this);
                return;
            }

            overHandler = HandleOver;
            outHandler = HandleOut;
            selectionCompleteHandler = HandleSelectionComplete;
            overEvent.AddEventHandler(interactiveItem, overHandler);
            outEvent.AddEventHandler(interactiveItem, outHandler);
            selectionCompleteEvent.AddEventHandler(selectionRadial, selectionCompleteHandler);
            wired = true;
        }

        private void Unwire()
        {
            if (!wired)
            {
                return;
            }

            overEvent.RemoveEventHandler(interactiveItem, overHandler);
            outEvent.RemoveEventHandler(interactiveItem, outHandler);
            selectionCompleteEvent.RemoveEventHandler(selectionRadial, selectionCompleteHandler);
            wired = false;
        }

        private void HandleOver()
        {
            isOver = true;
            armed = true;
            InvokeRadial("Show");
        }

        private void HandleOut()
        {
            isOver = false;
            armed = false;
            InvokeRadial("Hide");
        }

        private void HandleSelectionComplete()
        {
            if (!isOver || !armed)
            {
                return;
            }

            // Require the user to look away before the same action can fire again.
            armed = false;
            if (selected != null)
            {
                selected.Invoke();
            }
        }

        private void InvokeRadial(string methodName)
        {
            if (selectionRadial == null)
            {
                return;
            }

            MethodInfo method = selectionRadial.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public);
            if (method != null)
            {
                method.Invoke(selectionRadial, null);
            }
        }
    }
}
