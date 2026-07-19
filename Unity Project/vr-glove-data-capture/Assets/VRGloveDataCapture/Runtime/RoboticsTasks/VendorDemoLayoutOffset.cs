using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRGloveDataCapture.RoboticsTasks
{
    /// <summary>
    /// Moves the vendor demonstration furniture and objects to the sides while a
    /// project-owned task setup is running. Positions are absolute and idempotent,
    /// and the vendor TableScene asset is never modified or saved.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VendorDemoLayoutOffset : MonoBehaviour
    {
        private struct LayoutRule
        {
            public readonly string ObjectName;
            public readonly float TargetWorldX;

            public LayoutRule(string objectName, float targetWorldX)
            {
                ObjectName = objectName;
                TargetWorldX = targetWorldX;
            }
        }

        private static readonly LayoutRule[] Rules =
        {
            new LayoutRule("table_tall", -2.258f),
            new LayoutRule("Interaction_Object_7", -2.738f),
            new LayoutRule("Interaction_Object_6", -2.410f),
            new LayoutRule("Interaction_Compound _Object_9", -2.252f),
            new LayoutRule("Interaction_Object_2", -1.971f),
            new LayoutRule("Interaction_Object_4", -1.959f),
            new LayoutRule("Button_Interaction_3", -1.736f),
            new LayoutRule("Interaction_Simple_Object_3", -1.420f),
            new LayoutRule("Interaction_Simple_Compound _Object_1", -1.218f),
            new LayoutRule("table_S", 2.481f),
            new LayoutRule("Interaction_Compound_Object_10", 2.059f),
            new LayoutRule("Interaction_Simple_Object_2", 1.770f)
        };

        private bool[] appliedRules;
        private float nextRetryTime;
        private float nextSubscriptionAttemptTime;
        private bool loggedComplete;
        private bool reapplyAfterReset;
        private int resetMessageFrame = -1;
        private Hi5RuntimeBridge.ResetSubscription resetSubscription;

        public int AppliedEntryCount { get; private set; }
        public int ExpectedEntryCount { get { return Rules.Length; } }

        /// <summary>
        /// Exposes the same absolute layout rules to the editor workspace preview
        /// without creating a second list that can drift from runtime behavior.
        /// </summary>
        public static bool TryGetTargetWorldX(string objectName, out float targetWorldX)
        {
            for (int index = 0; index < Rules.Length; index++)
            {
                if (string.Equals(Rules[index].ObjectName, objectName, StringComparison.Ordinal))
                {
                    targetWorldX = Rules[index].TargetWorldX;
                    return true;
                }
            }

            targetWorldX = 0.0f;
            return false;
        }

        private void OnEnable()
        {
            reapplyAfterReset = false;
            resetMessageFrame = -1;
            nextSubscriptionAttemptTime = 0.0f;
            ResetApplicationState();
            SceneManager.sceneLoaded += HandleSceneLoaded;
            if (Application.isPlaying)
            {
                EnsureResetSubscription();
                ApplyLayout();
            }
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            if (resetSubscription != null)
            {
                resetSubscription.Dispose();
                resetSubscription = null;
            }
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (resetSubscription == null && Time.unscaledTime >= nextSubscriptionAttemptTime)
            {
                nextSubscriptionAttemptTime = Time.unscaledTime + 1.0f;
                EnsureResetSubscription();
            }

            if (reapplyAfterReset && Time.frameCount > resetMessageFrame)
            {
                reapplyAfterReset = false;
                ResetApplicationState();
                ApplyLayout();
                return;
            }

            if (AppliedEntryCount >= Rules.Length || Time.unscaledTime < nextRetryTime)
            {
                return;
            }

            nextRetryTime = Time.unscaledTime + 0.5f;
            ApplyLayout();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (string.Equals(
                    scene.path,
                    TaskSetupSceneMarker.DefaultBaseScenePath,
                    StringComparison.Ordinal))
            {
                ResetApplicationState();
                ApplyLayout();
            }
        }

        /// <summary>Applies all currently discoverable vendor layout rules.</summary>
        public int ApplyLayout()
        {
            if (appliedRules == null || appliedRules.Length != Rules.Length)
            {
                ResetApplicationState();
            }

            Transform[] allTransforms = Resources.FindObjectsOfTypeAll<Transform>();
            for (int ruleIndex = 0; ruleIndex < Rules.Length; ruleIndex++)
            {
                if (appliedRules[ruleIndex])
                {
                    continue;
                }

                LayoutRule rule = Rules[ruleIndex];
                for (int transformIndex = 0; transformIndex < allTransforms.Length; transformIndex++)
                {
                    Transform candidate = allTransforms[transformIndex];
                    if (candidate == null ||
                        !string.Equals(candidate.name, rule.ObjectName, StringComparison.Ordinal) ||
                        !candidate.gameObject.scene.IsValid() ||
                        !string.Equals(
                            candidate.gameObject.scene.path,
                            TaskSetupSceneMarker.DefaultBaseScenePath,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    Vector3 position = candidate.position;
                    position.x = rule.TargetWorldX;
                    candidate.position = position;
                    appliedRules[ruleIndex] = true;
                    AppliedEntryCount++;
                    break;
                }
            }

            if (!loggedComplete && AppliedEntryCount == Rules.Length)
            {
                loggedComplete = true;
                Debug.Log(
                    "[PickPlaceTasks] Vendor demonstration area moved to the side; " +
                    "the project worktable now has a clear center workspace.",
                    this);
            }

            return AppliedEntryCount;
        }

        private void EnsureResetSubscription()
        {
            if (resetSubscription == null)
            {
                resetSubscription = Hi5RuntimeBridge.RegisterResetCallback(this, "OnHi5Reset");
            }
        }

        // Signature intentionally mirrors Hi5_Interaction_Message.MessageFun.
        private void OnHi5Reset(
            string messageKey,
            object param1,
            object param2,
            object param3,
            object param4)
        {
            // Vendor objects restore their own cached positions synchronously.
            // Reapply on the following frame so callback ordering cannot bring
            // the demonstration furniture back into the central task workspace.
            reapplyAfterReset = true;
            resetMessageFrame = Time.frameCount;
        }

        private void ResetApplicationState()
        {
            appliedRules = new bool[Rules.Length];
            AppliedEntryCount = 0;
            nextRetryTime = 0.0f;
            loggedComplete = false;
        }
    }
}
