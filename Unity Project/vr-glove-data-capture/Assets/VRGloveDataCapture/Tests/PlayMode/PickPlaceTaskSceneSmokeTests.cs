using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VRGloveDataCapture.RoboticsTasks;

namespace VRGloveDataCapture.Tests
{
    public sealed class PickPlaceTaskSceneSmokeTests
    {
        private const string VendorScenePath = "Assets/Hi5_Interaction_SDK/Scenes/Vive/TableScene_Vive.unity";

        [UnityTest]
        public IEnumerator TableSceneBuildsFiveTasksAndHi5ResetRestoresTheirPoses()
        {
            string absoluteScenePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", VendorScenePath));
            if (!File.Exists(absoluteScenePath))
            {
                Assert.Ignore("Local Hi5 Interaction SDK scene is not installed.");
            }

            AsyncOperation loadOperation = EditorSceneManager.LoadSceneAsyncInPlayMode(
                VendorScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
            while (!loadOperation.isDone)
            {
                yield return null;
            }

            GameObject layoutRoot = null;
            for (int frame = 0; frame < 600 && layoutRoot == null; frame++)
            {
                layoutRoot = GameObject.Find("VRGlove_PickPlace_Tasks");
                yield return null;
            }

            Assert.IsNotNull(layoutRoot, "Runtime installer did not create the task layout.");

            PickPlaceTaskObject[] taskObjects = UnityEngine.Object.FindObjectsOfType<PickPlaceTaskObject>();
            PickPlaceTargetZone[] targetZones = UnityEngine.Object.FindObjectsOfType<PickPlaceTargetZone>();
            Assert.AreEqual(5, taskObjects.Length, "Expected five independently scored task objects.");
            Assert.AreEqual(5, targetZones.Length, "Expected five target volumes.");

            Dictionary<PickPlaceTaskObject, Vector3> startPositions =
                new Dictionary<PickPlaceTaskObject, Vector3>();
            Dictionary<PickPlaceTargetZone, Color> readyColors =
                new Dictionary<PickPlaceTargetZone, Color>();
            for (int index = 0; index < taskObjects.Length; index++)
            {
                PickPlaceTaskObject taskObject = taskObjects[index];
                startPositions.Add(taskObject, taskObject.transform.position);
            }

            FieldInfo zoneTaskIdField = typeof(PickPlaceTargetZone).GetField(
                "taskId",
                BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo zoneIndicatorField = typeof(PickPlaceTargetZone).GetField(
                "indicator",
                BindingFlags.NonPublic | BindingFlags.Instance);

            for (int zoneIndex = 0; zoneIndex < targetZones.Length; zoneIndex++)
            {
                PickPlaceTargetZone zone = targetZones[zoneIndex];
                string targetTaskId = (string)zoneTaskIdField.GetValue(zone);
                PickPlaceTaskObject matchingObject = Array.Find(
                    taskObjects,
                    candidate => candidate.TaskId == targetTaskId);
                Assert.IsNotNull(matchingObject, "No task object matches target " + targetTaskId + ".");

                Renderer indicator = (Renderer)zoneIndicatorField.GetValue(zone);
                readyColors.Add(zone, indicator.material.color);

                Rigidbody body = matchingObject.GetComponent<Rigidbody>();
                body.isKinematic = false;
                matchingObject.transform.position = zone.GetComponent<Collider>().bounds.center;
                Physics.SyncTransforms();
                yield return new WaitForFixedUpdate();
            }

            yield return new WaitForFixedUpdate();

            PickPlaceTaskSceneController controller =
                UnityEngine.Object.FindObjectOfType<PickPlaceTaskSceneController>();
            FieldInfo completedTasksField = typeof(PickPlaceTaskSceneController).GetField(
                "completedTasks",
                BindingFlags.NonPublic | BindingFlags.Instance);
            object completedTasks = completedTasksField.GetValue(controller);
            int completedTaskCount = (int)completedTasks.GetType().GetProperty("Count").GetValue(completedTasks, null);
            Assert.AreEqual(5, completedTaskCount, "All five matching target entries should be detected.");

            for (int zoneIndex = 0; zoneIndex < targetZones.Length; zoneIndex++)
            {
                Renderer indicator = (Renderer)zoneIndicatorField.GetValue(targetZones[zoneIndex]);
                Assert.Greater(indicator.material.color.g, 0.8f, "Completed target did not turn green.");
            }

            for (int index = 0; index < taskObjects.Length; index++)
            {
                PickPlaceTaskObject taskObject = taskObjects[index];
                taskObject.transform.position += new Vector3(0.25f, 0.18f, -0.12f);
                Rigidbody body = taskObject.GetComponent<Rigidbody>();
                body.velocity = new Vector3(1f, 2f, 3f);
                body.angularVelocity = new Vector3(3f, 2f, 1f);
            }

            Assert.IsTrue(DispatchHi5Reset(), "The Hi5 message bus could not publish messageObjectReset.");
            yield return null;

            foreach (KeyValuePair<PickPlaceTaskObject, Vector3> entry in startPositions)
            {
                Rigidbody body = entry.Key.GetComponent<Rigidbody>();
                Assert.Less(Vector3.Distance(entry.Value, entry.Key.transform.position), 0.0001f,
                    entry.Key.name + " did not return to its captured start position.");
                Assert.IsTrue(body.isKinematic, entry.Key.name + " did not restore its kinematic reset state.");
                Assert.Less(body.velocity.sqrMagnitude, 0.000001f, entry.Key.name + " retained linear velocity.");
                Assert.Less(body.angularVelocity.sqrMagnitude, 0.000001f, entry.Key.name + " retained angular velocity.");
            }

            foreach (KeyValuePair<PickPlaceTargetZone, Color> entry in readyColors)
            {
                Renderer indicator = (Renderer)zoneIndicatorField.GetValue(entry.Key);
                Assert.Less(
                    Vector4.Distance(indicator.material.color, entry.Value),
                    0.001f,
                    entry.Key.name + " did not restore its ready color.");
            }
        }

        private static bool DispatchHi5Reset()
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
            dispatch.Invoke(messageBus, new object[] { "messageObjectReset", null, null, null, null });
            return true;
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
