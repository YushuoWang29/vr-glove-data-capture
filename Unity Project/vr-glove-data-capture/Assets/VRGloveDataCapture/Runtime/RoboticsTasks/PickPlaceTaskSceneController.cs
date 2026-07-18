using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VRGloveDataCapture.Capture;

namespace VRGloveDataCapture.RoboticsTasks
{
    /// <summary>Owns task progress and makes the vendor reset button reset the full task scene.</summary>
    public sealed class PickPlaceTaskSceneController : MonoBehaviour
    {
        [SerializeField] private bool autoInitializeFromScene = true;

        private readonly List<PickPlaceTaskObject> taskObjects = new List<PickPlaceTaskObject>();
        private readonly List<PickPlaceTargetZone> targetZones = new List<PickPlaceTargetZone>();
        private readonly HashSet<string> completedTasks = new HashSet<string>();
        private Hi5RuntimeBridge.ResetSubscription resetSubscription;
        private int totalTasks;
        private bool initialized;

        private IEnumerator Start()
        {
            if (!autoInitializeFromScene || initialized)
            {
                yield break;
            }

            // Capture the persistent task-scene membership before Hi5 reparents
            // interactable objects into its own manager hierarchy.
            PickPlaceTaskObject[] sceneObjects = GetComponentsInChildren<PickPlaceTaskObject>(true);
            PickPlaceTargetZone[] sceneTargets = GetComponentsInChildren<PickPlaceTargetZone>(true);

            const int maximumFramesToWait = 300;
            for (int frame = 0; frame < maximumFramesToWait; frame++)
            {
                if (Hi5RuntimeBridge.IsSimpleObjectManagerReady())
                {
                    int boundObjectCount = 0;
                    for (int index = 0; index < sceneObjects.Length; index++)
                    {
                        PickPlaceTaskObject taskObject = sceneObjects[index];
                        if (taskObject != null && taskObject.BindToHi5())
                        {
                            RegisterObject(taskObject);
                            boundObjectCount++;
                        }
                    }

                    for (int index = 0; index < sceneTargets.Length; index++)
                    {
                        RegisterTarget(sceneTargets[index]);
                    }

                    Initialize(boundObjectCount);
                    Debug.Log("[PickPlaceTasks] Bound " + boundObjectCount + " persistent task objects to Hi5.");
                    yield break;
                }

                yield return null;
            }

            Debug.LogError("[PickPlaceTasks] Hi5 simple-object manager did not become ready; persistent task objects were not bound.");
        }

        internal void Initialize(int expectedTaskCount)
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            totalTasks = expectedTaskCount;
            resetSubscription = Hi5RuntimeBridge.RegisterResetCallback(this, "OnHi5Reset");

            if (resetSubscription == null)
            {
                Debug.LogWarning("[PickPlaceTasks] Hi5 reset integration is unavailable; F8 local reset remains active.");
            }
        }

        internal void RegisterObject(PickPlaceTaskObject taskObject)
        {
            if (taskObject != null && !taskObjects.Contains(taskObject))
            {
                taskObjects.Add(taskObject);
                taskObject.CaptureStartPose();
            }
        }

        internal void RegisterTarget(PickPlaceTargetZone targetZone)
        {
            if (targetZone != null && !targetZones.Contains(targetZone))
            {
                targetZones.Add(targetZone);
            }
        }

        internal void MarkCompleted(string taskId)
        {
            if (!completedTasks.Add(taskId))
            {
                return;
            }

            Debug.Log("[PickPlaceTasks] Completed " + taskId + " (" + completedTasks.Count + "/" + totalTasks + ").");
            CaptureEventBus.Publish(
                "task_completed",
                taskId,
                string.Empty,
                "completed=" + completedTasks.Count + ";total=" + totalTasks);
            if (completedTasks.Count == totalTasks)
            {
                Debug.Log("[PickPlaceTasks] All pick-and-place tasks completed. Press the scene reset button or F8 to restart.");
                CaptureEventBus.Publish(
                    "task_set_completed",
                    "pick-place",
                    string.Empty,
                    "total=" + totalTasks);
            }
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.F8))
            {
                return;
            }

            if (!Hi5RuntimeBridge.DispatchReset())
            {
                ResetAll();
            }
        }

        private void OnDestroy()
        {
            if (resetSubscription != null)
            {
                resetSubscription.Dispose();
                resetSubscription = null;
            }
        }

        // Signature intentionally mirrors Hi5_Interaction_Message.MessageFun.
        private void OnHi5Reset(string messageKey, object param1, object param2, object param3, object param4)
        {
            ResetAll();
        }

        private void ResetAll()
        {
            completedTasks.Clear();

            for (int index = 0; index < taskObjects.Count; index++)
            {
                if (taskObjects[index] != null)
                {
                    taskObjects[index].ResetToStart();
                }
            }

            for (int index = 0; index < targetZones.Count; index++)
            {
                if (targetZones[index] != null)
                {
                    targetZones[index].ResetTarget();
                }
            }

            Debug.Log("[PickPlaceTasks] Scene objects and task progress reset.");
            CaptureEventBus.Publish(
                "scene_reset",
                "pick-place",
                string.Empty,
                "objects=" + taskObjects.Count + ";targets=" + targetZones.Count);
        }
    }
}
