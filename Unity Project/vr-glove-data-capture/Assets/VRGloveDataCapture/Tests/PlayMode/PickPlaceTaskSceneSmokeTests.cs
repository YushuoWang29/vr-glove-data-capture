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
        private const string TaskScenePath = "Assets/VRGloveDataCapture/Scenes/TaskSetups/PickPlaceTasks.unity";

        [UnityTest]
        public IEnumerator EditableTaskSceneBindsFiveTasksAndHi5ResetRestoresTheirPoses()
        {
            string absoluteScenePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", VendorScenePath));
            if (!File.Exists(absoluteScenePath))
            {
                Assert.Ignore("Local Hi5 Interaction SDK scene is not installed.");
            }

            // The editor workspace opens the vendor scene before Play Mode and
            // keeps the project-owned task scene active. Mirror that lifecycle:
            // the Hi5 managers must exist before the task controller starts.
            AsyncOperation baseLoadOperation = EditorSceneManager.LoadSceneAsyncInPlayMode(
                VendorScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
            while (!baseLoadOperation.isDone)
            {
                yield return null;
            }

            AsyncOperation taskLoadOperation = EditorSceneManager.LoadSceneAsyncInPlayMode(
                TaskScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive));
            while (!taskLoadOperation.isDone)
            {
                yield return null;
            }

            GameObject layoutRoot = null;
            PickPlaceTaskSceneController controller = null;
            FieldInfo initializedField = typeof(PickPlaceTaskSceneController).GetField(
                "initialized",
                BindingFlags.NonPublic | BindingFlags.Instance);
            for (int frame = 0; frame < 600; frame++)
            {
                layoutRoot = GameObject.Find("VRGlove_PickPlace_Tasks");
                controller = UnityEngine.Object.FindObjectOfType<PickPlaceTaskSceneController>();
                if (layoutRoot != null && controller != null && (bool)initializedField.GetValue(controller))
                {
                    break;
                }

                yield return null;
            }

            Assert.IsNotNull(layoutRoot, "The persistent task scene did not contain its editable layout.");
            Assert.IsNotNull(controller, "The persistent task controller did not load.");
            Assert.IsTrue((bool)initializedField.GetValue(controller), "The persistent task scene did not bind to Hi5.");

            PickPlaceTaskObject[] taskObjects = UnityEngine.Object.FindObjectsOfType<PickPlaceTaskObject>();
            PickPlaceTargetZone[] targetZones = UnityEngine.Object.FindObjectsOfType<PickPlaceTargetZone>();
            Assert.AreEqual(5, taskObjects.Length, "Expected five independently scored task objects.");
            Assert.AreEqual(5, targetZones.Length, "Expected five target volumes.");
            Assert.AreEqual(0, layoutRoot.GetComponentsInChildren<TextMesh>(true).Length,
                "The editable task area must not contain floating instruction text.");
            Assert.IsNull(FindChildByName(layoutRoot.transform, "Pick_Source_Pad"),
                "Legacy blue source pads must not remain under graspable objects.");

            Dictionary<PickPlaceTaskObject, Vector3> startPositions =
                new Dictionary<PickPlaceTaskObject, Vector3>();
            FieldInfo capturedStartPositionField = typeof(PickPlaceTaskObject).GetField(
                "startPosition",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Dictionary<PickPlaceTargetZone, Color> readyColors =
                new Dictionary<PickPlaceTargetZone, Color>();
            HashSet<string> uniqueTaskIds = new HashSet<string>();
            HashSet<int> uniqueHi5Ids = new HashSet<int>();
            for (int index = 0; index < taskObjects.Length; index++)
            {
                PickPlaceTaskObject taskObject = taskObjects[index];
                startPositions.Add(taskObject, (Vector3)capturedStartPositionField.GetValue(taskObject));
                Assert.IsTrue(uniqueTaskIds.Add(taskObject.TaskId),
                    "Duplicate task id " + taskObject.TaskId + " creates ambiguous scoring.");
                Assert.IsTrue(uniqueHi5Ids.Add(taskObject.Hi5ObjectId),
                    "Duplicate Hi5 id " + taskObject.Hi5ObjectId + " can cause remote grabbing.");

                Rigidbody body = taskObject.GetComponent<Rigidbody>();
                Assert.IsNotNull(body, taskObject.name + " has no Rigidbody.");
                Assert.IsTrue(body.useGravity, taskObject.name + " does not use gravity.");
                Assert.IsFalse(body.isKinematic, taskObject.name + " is not dynamic while released.");
                Collider physicalCollider = taskObject.GetComponent<Collider>();
                Assert.IsNotNull(physicalCollider, taskObject.name + " has no physical collider.");
                Assert.IsFalse(physicalCollider.isTrigger, taskObject.name + " uses a trigger instead of a physical collider.");
                Assert.IsNotNull(taskObject.GetComponent<Renderer>(),
                    taskObject.name + " renderer is detached from its physics/grab root.");
                Assert.AreEqual(1, taskObject.GetComponentsInChildren<Renderer>(true).Length,
                    taskObject.name + " has a duplicate or residual visual renderer.");
            }

            PickPlaceTaskObject mug = Array.Find(taskObjects, item => item.name == "YCB_Mug");
            Assert.IsNotNull(mug, "The YCB mug is missing.");
            FieldInfo capturedStartRotationField = typeof(PickPlaceTaskObject).GetField(
                "startRotation",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Quaternion mugStartRotation = (Quaternion)capturedStartRotationField.GetValue(mug);
            Assert.Less(Quaternion.Angle(mugStartRotation, Quaternion.Euler(-90f, 0f, 0f)), 0.1f,
                "The mug's authored start pose is not in its upright model orientation.");

            Type visiblePalmType = FindType("Hi5_Interaction_Core.Hi5_Hand_Palm");
            Component visiblePalm = visiblePalmType == null
                ? null
                : UnityEngine.Object.FindObjectOfType(visiblePalmType) as Component;
            Assert.IsNotNull(visiblePalm, "The vendor visible-hand palm was not found.");
            PickPlaceTaskObject holdPhysicsProbe = taskObjects[0];
            Transform releasedParent = holdPhysicsProbe.transform.parent;
            Rigidbody holdPhysicsBody = holdPhysicsProbe.GetComponent<Rigidbody>();
            holdPhysicsProbe.transform.SetParent(visiblePalm.transform, true);
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(holdPhysicsBody.isKinematic,
                "A task object parented to the Hi5 palm must be kinematic while held.");
            holdPhysicsProbe.transform.SetParent(releasedParent, true);
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(holdPhysicsBody.isKinematic,
                "A task object released from the Hi5 palm must return to dynamic physics.");
            Assert.IsTrue(holdPhysicsBody.useGravity,
                "A task object released from the Hi5 palm must retain gravity.");

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

            foreach (KeyValuePair<PickPlaceTaskObject, Vector3> entry in startPositions)
            {
                Rigidbody body = entry.Key.GetComponent<Rigidbody>();
                Assert.Less(Vector3.Distance(entry.Value, entry.Key.transform.position), 0.0001f,
                    entry.Key.name + " did not return to its captured start position.");
                Assert.IsFalse(body.isKinematic, entry.Key.name + " did not restore its dynamic released state.");
                Assert.IsTrue(body.useGravity, entry.Key.name + " lost gravity after reset.");
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

        private static Transform FindChildByName(Transform root, string objectName)
        {
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < children.Length; index++)
            {
                if (children[index].name == objectName)
                {
                    return children[index];
                }
            }

            return null;
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
