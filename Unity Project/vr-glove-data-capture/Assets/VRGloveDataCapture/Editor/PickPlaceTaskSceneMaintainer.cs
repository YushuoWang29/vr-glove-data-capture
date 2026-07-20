using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRGloveDataCapture.RoboticsTasks;

namespace VRGloveDataCapture.Editor
{
    /// <summary>
    /// Applies narrowly scoped, repeatable maintenance to the editable task
    /// scene without rebuilding or repositioning any user-authored content.
    /// </summary>
    public static class PickPlaceTaskSceneMaintainer
    {
        public const string TaskScenePath =
            "Assets/VRGloveDataCapture/Scenes/TaskSetups/PickPlaceTasks.unity";

        [MenuItem(
            "Tools/VR Glove Data Capture/Task Setups/Update YCB Mug Compound Colliders",
            priority = 121)]
        public static void UpdateMugCompoundCollidersFromMenu()
        {
            try
            {
                UpdateMugCompoundCollidersInPickPlaceScene();
                EditorUtility.DisplayDialog(
                    "YCB Mug Collider Maintenance",
                    "Updated only the YCB_Mug compound colliders. Scene root transforms and task layout were preserved.",
                    "OK");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "YCB Mug Collider Maintenance",
                    "The collider update failed. See the Console for details.",
                    "OK");
            }
        }

        /// <summary>
        /// Batch entry point for Unity's -executeMethod option.
        /// </summary>
        public static void UpdateMugCompoundCollidersBatch()
        {
            try
            {
                UpdateMugCompoundCollidersInPickPlaceScene();
                Debug.Log("[PickPlaceTasks] Batch YCB mug collider maintenance completed.");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                    return;
                }

                throw;
            }
        }

        /// <summary>
        /// Replaces only Collider components owned by YCB_Mug, then saves the
        /// task scene. No scene root Transform is rebuilt, moved, or rescaled.
        /// </summary>
        public static void UpdateMugCompoundCollidersInPickPlaceScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "YCB mug collider maintenance can only run outside Play Mode.");
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TaskScenePath) == null)
            {
                throw new InvalidOperationException(
                    "The editable pick-and-place scene is missing: " + TaskScenePath);
            }

            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene taskScene = SceneManager.GetSceneByPath(TaskScenePath);
            bool openedByMaintainer = !taskScene.IsValid() || !taskScene.isLoaded;
            try
            {
                if (openedByMaintainer)
                {
                    taskScene = EditorSceneManager.OpenScene(TaskScenePath, OpenSceneMode.Additive);
                }

                RootTransformSnapshot[] rootSnapshots = CaptureRootTransforms(taskScene);
                GameObject mug = FindUniqueMug(taskScene);
                ValidateMugStructure(mug);

                Collider[] obsoleteColliders = mug.GetComponentsInChildren<Collider>(true);
                for (int index = obsoleteColliders.Length - 1; index >= 0; index--)
                {
                    UnityEngine.Object.DestroyImmediate(obsoleteColliders[index]);
                }

                MeshFilter meshFilter = mug.GetComponent<MeshFilter>();
                PickPlaceTaskLayout.AddMugCompoundColliders(
                    mug,
                    meshFilter.sharedMesh.bounds);
                ValidateCompoundColliderResult(mug);
                EnsureRootTransformsUnchanged(taskScene, rootSnapshots);

                EditorUtility.SetDirty(mug);
                Transform[] mugTransforms = mug.GetComponentsInChildren<Transform>(true);
                for (int index = 0; index < mugTransforms.Length; index++)
                {
                    EditorUtility.SetDirty(mugTransforms[index]);
                }

                Collider[] maintainedColliders = mug.GetComponentsInChildren<Collider>(true);
                for (int index = 0; index < maintainedColliders.Length; index++)
                {
                    EditorUtility.SetDirty(maintainedColliders[index]);
                }

                EditorSceneManager.MarkSceneDirty(taskScene);
                if (!EditorSceneManager.SaveScene(taskScene, TaskScenePath))
                {
                    throw new InvalidOperationException(
                        "Unity could not save the maintained task scene: " + TaskScenePath);
                }

                Debug.Log(
                    "[PickPlaceTasks] Updated YCB_Mug to one body collider and three handle colliders; " +
                    "all task-scene root transforms were preserved.");
            }
            finally
            {
                if (openedByMaintainer && taskScene.IsValid() && taskScene.isLoaded)
                {
                    EditorSceneManager.CloseScene(taskScene, true);
                }

                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                {
                    SceneManager.SetActiveScene(previousActiveScene);
                }
            }
        }

        private static GameObject FindUniqueMug(Scene scene)
        {
            GameObject match = null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                Transform[] transforms = roots[rootIndex].GetComponentsInChildren<Transform>(true);
                for (int index = 0; index < transforms.Length; index++)
                {
                    if (transforms[index].name != PickPlaceTaskLayout.MugObjectName)
                    {
                        continue;
                    }

                    if (match != null)
                    {
                        throw new InvalidOperationException(
                            "The task scene contains more than one object named " +
                            PickPlaceTaskLayout.MugObjectName + ".");
                    }

                    match = transforms[index].gameObject;
                }
            }

            if (match == null)
            {
                throw new InvalidOperationException(
                    "The task scene does not contain " + PickPlaceTaskLayout.MugObjectName + ".");
            }

            return match;
        }

        private static void ValidateMugStructure(GameObject mug)
        {
            MeshFilter meshFilter = mug.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                throw new InvalidOperationException(
                    "YCB_Mug must keep its imported mesh on the physics root.");
            }

            Rigidbody rootBody = mug.GetComponent<Rigidbody>();
            Rigidbody[] rigidbodies = mug.GetComponentsInChildren<Rigidbody>(true);
            if (rootBody == null || rigidbodies.Length != 1 || rigidbodies[0] != rootBody)
            {
                throw new InvalidOperationException(
                    "YCB_Mug must have exactly one Rigidbody on its root before collider maintenance.");
            }
        }

        private static void ValidateCompoundColliderResult(GameObject mug)
        {
            Rigidbody rootBody = mug.GetComponent<Rigidbody>();
            Collider[] colliders = mug.GetComponentsInChildren<Collider>(true);
            if (colliders.Length != 4)
            {
                throw new InvalidOperationException(
                    "YCB_Mug maintenance produced " + colliders.Length +
                    " colliders instead of four.");
            }

            for (int index = 0; index < colliders.Length; index++)
            {
                if (!(colliders[index] is BoxCollider) ||
                    colliders[index].isTrigger ||
                    colliders[index].attachedRigidbody != rootBody)
                {
                    throw new InvalidOperationException(
                        "Every YCB_Mug collider must be a solid primitive owned by the root Rigidbody.");
                }
            }
        }

        private static RootTransformSnapshot[] CaptureRootTransforms(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            RootTransformSnapshot[] snapshots = new RootTransformSnapshot[roots.Length];
            for (int index = 0; index < roots.Length; index++)
            {
                snapshots[index] = new RootTransformSnapshot(roots[index].transform);
            }

            return snapshots;
        }

        private static void EnsureRootTransformsUnchanged(
            Scene scene,
            RootTransformSnapshot[] snapshots)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            if (roots.Length != snapshots.Length)
            {
                throw new InvalidOperationException(
                    "YCB mug collider maintenance unexpectedly changed the scene root count.");
            }

            Dictionary<int, Transform> rootsById = new Dictionary<int, Transform>();
            for (int index = 0; index < roots.Length; index++)
            {
                rootsById.Add(roots[index].transform.GetInstanceID(), roots[index].transform);
            }

            for (int index = 0; index < snapshots.Length; index++)
            {
                Transform root;
                if (!rootsById.TryGetValue(snapshots[index].instanceId, out root) ||
                    !snapshots[index].Matches(root))
                {
                    throw new InvalidOperationException(
                        "YCB mug collider maintenance changed a scene root Transform or root order.");
                }
            }
        }

        private struct RootTransformSnapshot
        {
            internal readonly int instanceId;
            private readonly Vector3 localPosition;
            private readonly Quaternion localRotation;
            private readonly Vector3 localScale;
            private readonly int siblingIndex;

            internal RootTransformSnapshot(Transform transform)
            {
                instanceId = transform.GetInstanceID();
                localPosition = transform.localPosition;
                localRotation = transform.localRotation;
                localScale = transform.localScale;
                siblingIndex = transform.GetSiblingIndex();
            }

            internal bool Matches(Transform transform)
            {
                return transform.localPosition == localPosition &&
                    transform.localRotation == localRotation &&
                    transform.localScale == localScale &&
                    transform.GetSiblingIndex() == siblingIndex;
            }
        }
    }
}
