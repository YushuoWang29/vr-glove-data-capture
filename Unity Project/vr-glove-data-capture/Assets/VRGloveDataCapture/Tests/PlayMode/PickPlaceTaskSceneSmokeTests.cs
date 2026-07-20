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

            Transform worktableSurfaceTransform =
                FindChildByName(layoutRoot.transform, "Task_Worktable_Surface");
            Assert.IsNotNull(worktableSurfaceTransform,
                "The editable task setup has no supporting worktable.");
            BoxCollider worktableCollider = worktableSurfaceTransform.GetComponent<BoxCollider>();
            Assert.IsNotNull(worktableCollider, "The task worktable has no physical collider.");
            Assert.IsFalse(worktableCollider.isTrigger,
                "The task worktable must use a solid collider.");
            Assert.IsNull(worktableSurfaceTransform.GetComponent<Rigidbody>(),
                "The task worktable must remain static.");
            Assert.AreEqual(12, worktableSurfaceTransform.gameObject.layer,
                "The task worktable is not on the Hi5 plane layer.");
            float worktableTop = worktableCollider.bounds.max.y;
            Assert.That(worktableTop, Is.EqualTo(0.69f).Within(0.002f),
                "The worktable top does not match the authored task surface height.");

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
                Assert.GreaterOrEqual(
                    physicalCollider.bounds.min.y,
                    worktableTop - 0.01f,
                    taskObject.name + " starts below the worktable surface.");
                Assert.LessOrEqual(
                    physicalCollider.bounds.min.y,
                    worktableTop + 0.025f,
                    taskObject.name + " floats too far above the worktable surface.");
                Assert.GreaterOrEqual(physicalCollider.bounds.min.x, worktableCollider.bounds.min.x,
                    taskObject.name + " starts beyond the left edge of the worktable.");
                Assert.LessOrEqual(physicalCollider.bounds.max.x, worktableCollider.bounds.max.x,
                    taskObject.name + " starts beyond the right edge of the worktable.");
                Assert.GreaterOrEqual(physicalCollider.bounds.min.z, worktableCollider.bounds.min.z,
                    taskObject.name + " starts beyond the front edge of the worktable.");
                Assert.LessOrEqual(physicalCollider.bounds.max.z, worktableCollider.bounds.max.z,
                    taskObject.name + " starts beyond the back edge of the worktable.");
            }

            PickPlaceTaskObject mug = Array.Find(taskObjects, item => item.name == "YCB_Mug");
            Assert.IsNotNull(mug, "The YCB mug is missing.");
            FieldInfo capturedStartRotationField = typeof(PickPlaceTaskObject).GetField(
                "startRotation",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Quaternion mugStartRotation = (Quaternion)capturedStartRotationField.GetValue(mug);
            Assert.Less(Quaternion.Angle(mugStartRotation, Quaternion.Euler(-90f, 0f, 0f)), 0.1f,
                "The mug's authored start pose is not in its upright model orientation.");
            AssertMugUsesOneRigidbodyAndOpenHandleCompoundCollider(mug);

            Type visiblePalmType = FindType("Hi5_Interaction_Core.Hi5_Hand_Palm");
            Component visiblePalm = visiblePalmType == null
                ? null
                : UnityEngine.Object.FindObjectOfType(visiblePalmType) as Component;
            Assert.IsNotNull(visiblePalm, "The vendor visible-hand palm was not found.");
            PickPlaceTaskObject holdPhysicsProbe = taskObjects[0];
            Transform releasedParent = holdPhysicsProbe.transform.parent;
            Rigidbody holdPhysicsBody = holdPhysicsProbe.GetComponent<Rigidbody>();
            FieldInfo ignoredCollisionPairsField = typeof(PickPlaceTaskObject).GetField(
                "ignoredHandCollisions",
                BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo maximumLinearSpeedField = typeof(PickPlaceTaskObject).GetField(
                "maximumReleaseLinearSpeed",
                BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo maximumAngularSpeedField = typeof(PickPlaceTaskObject).GetField(
                "maximumReleaseAngularSpeed",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(ignoredCollisionPairsField);
            Assert.IsNotNull(maximumLinearSpeedField);
            Assert.IsNotNull(maximumAngularSpeedField);

            holdPhysicsProbe.transform.SetParent(visiblePalm.transform, true);
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(holdPhysicsBody.isKinematic,
                "A task object parented to the Hi5 palm must be kinematic while held.");

            IList ignoredCollisionPairs =
                (IList)ignoredCollisionPairsField.GetValue(holdPhysicsProbe);
            Assert.Greater(
                ignoredCollisionPairs.Count,
                0,
                "A held task object did not suppress collisions against its grabbing hand.");

            for (int sample = 0; sample < 4; sample++)
            {
                holdPhysicsProbe.transform.position += new Vector3(0.012f, 0.0f, 0.0f);
                holdPhysicsProbe.transform.rotation =
                    Quaternion.AngleAxis(7.0f, Vector3.up) * holdPhysicsProbe.transform.rotation;
                yield return null;
            }

            holdPhysicsProbe.transform.SetParent(releasedParent, true);
            Assert.IsFalse(holdPhysicsBody.isKinematic,
                "A task object released from the Hi5 palm must return to dynamic physics.");
            Assert.IsTrue(holdPhysicsBody.useGravity,
                "A task object released from the Hi5 palm must retain gravity.");
            Assert.Greater(
                holdPhysicsBody.velocity.magnitude,
                0.01f,
                "A released task object did not inherit the sampled hand motion.");
            Assert.Greater(
                holdPhysicsBody.angularVelocity.magnitude,
                0.01f,
                "A released task object did not inherit the sampled hand rotation.");
            Assert.LessOrEqual(
                holdPhysicsBody.velocity.magnitude,
                (float)maximumLinearSpeedField.GetValue(holdPhysicsProbe) + 0.001f,
                "Inherited task-object velocity exceeded its safety limit.");
            Assert.LessOrEqual(
                holdPhysicsBody.angularVelocity.magnitude,
                (float)maximumAngularSpeedField.GetValue(holdPhysicsProbe) + 0.001f,
                "Inherited task-object angular velocity exceeded its safety limit.");

            Assert.Greater(
                ignoredCollisionPairs.Count,
                0,
                "Hand collisions were restored before the release grace period elapsed.");
            holdPhysicsProbe.transform.position += Vector3.one * 0.5f;
            Physics.SyncTransforms();
            yield return new WaitForSecondsRealtime(0.16f);
            Assert.AreEqual(
                0,
                ignoredCollisionPairs.Count,
                "Hand collisions were not restored after the object separated from the hand.");

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

        private static void AssertMugUsesOneRigidbodyAndOpenHandleCompoundCollider(
            PickPlaceTaskObject mug)
        {
            Rigidbody rootBody = mug.GetComponent<Rigidbody>();
            Rigidbody[] rigidbodies = mug.GetComponentsInChildren<Rigidbody>(true);
            Assert.IsNotNull(rootBody, "YCB_Mug has no root Rigidbody.");
            Assert.AreEqual(1, rigidbodies.Length,
                "YCB_Mug compound colliders must share one root Rigidbody.");
            Assert.AreSame(rootBody, rigidbodies[0],
                "YCB_Mug has a child Rigidbody that splits its compound collider.");

            Collider[] colliders = mug.GetComponentsInChildren<Collider>(true);
            Assert.AreEqual(4, colliders.Length,
                "YCB_Mug must have one body primitive and three handle primitives.");
            Assert.IsNull(mug.GetComponentInChildren<MeshCollider>(true),
                "YCB_Mug must not use a convex or non-convex MeshCollider.");
            for (int index = 0; index < colliders.Length; index++)
            {
                Assert.IsInstanceOf<BoxCollider>(colliders[index],
                    "Every YCB_Mug collision shape must be a primitive BoxCollider.");
                Assert.IsFalse(colliders[index].isTrigger,
                    "YCB_Mug physical colliders must not be triggers.");
                Assert.AreSame(rootBody, colliders[index].attachedRigidbody,
                    "A YCB_Mug collider is not owned by the single root Rigidbody.");
            }

            BoxCollider bodyCollider = mug.GetComponent<BoxCollider>();
            Assert.IsNotNull(bodyCollider,
                "YCB_Mug must keep its body primitive on the physics root for Hi5 interaction.");

            BoxCollider topHandle = RequireHandleCollider(
                mug.transform,
                PickPlaceTaskLayout.MugHandleTopColliderName);
            BoxCollider outerHandle = RequireHandleCollider(
                mug.transform,
                PickPlaceTaskLayout.MugHandleOuterColliderName);
            BoxCollider bottomHandle = RequireHandleCollider(
                mug.transform,
                PickPlaceTaskLayout.MugHandleBottomColliderName);

            float bodyMinX = bodyCollider.center.x - bodyCollider.size.x * 0.5f;
            float outerMaxX = outerHandle.transform.localPosition.x +
                outerHandle.center.x + outerHandle.size.x * 0.5f;
            float topMinZ = topHandle.transform.localPosition.z +
                topHandle.center.z - topHandle.size.z * 0.5f;
            float bottomMaxZ = bottomHandle.transform.localPosition.z +
                bottomHandle.center.z + bottomHandle.size.z * 0.5f;

            Assert.Less(outerHandle.transform.localPosition.x, bodyCollider.center.x,
                "Unity's imported YCB mug handle must remain on local -X.");
            Assert.Greater(bodyMinX - outerMaxX, 0.005f,
                "The mug body and outer handle collider leave no horizontal handle opening.");
            Assert.Greater(topMinZ - bottomMaxZ, 0.015f,
                "The top and bottom handle colliders leave no vertical handle opening.");

            Vector3 holeCenterLocal = new Vector3(
                (bodyMinX + outerMaxX) * 0.5f,
                outerHandle.transform.localPosition.y,
                (bottomMaxZ + topMinZ) * 0.5f);
            Vector3 holeCenterWorld = mug.transform.TransformPoint(holeCenterLocal);
            Physics.SyncTransforms();
            for (int index = 0; index < colliders.Length; index++)
            {
                Vector3 closestPoint = colliders[index].ClosestPoint(holeCenterWorld);
                Assert.Greater(
                    (closestPoint - holeCenterWorld).sqrMagnitude,
                    0.000001f,
                    colliders[index].name + " fills the YCB mug handle opening.");
            }
        }

        private static BoxCollider RequireHandleCollider(Transform mug, string childName)
        {
            Transform child = FindChildByName(mug, childName);
            Assert.IsNotNull(child, "YCB_Mug is missing " + childName + ".");
            Assert.IsNull(child.GetComponent<Rigidbody>(),
                childName + " must share the mug root Rigidbody.");
            Assert.IsNull(child.GetComponent<Renderer>(),
                childName + " must remain an invisible collider helper.");

            BoxCollider collider = child.GetComponent<BoxCollider>();
            Assert.IsNotNull(collider, childName + " has no BoxCollider.");
            return collider;
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
