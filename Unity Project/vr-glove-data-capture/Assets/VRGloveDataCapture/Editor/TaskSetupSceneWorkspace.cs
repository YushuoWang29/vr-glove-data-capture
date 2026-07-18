using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRGloveDataCapture.RoboticsTasks;

namespace VRGloveDataCapture.Editor
{
    /// <summary>
    /// Keeps project-owned task setups editable while loading the untouched
    /// vendor TableScene additively for calibration, tracking status, and reset UI.
    /// </summary>
    [InitializeOnLoad]
    public static class TaskSetupSceneWorkspace
    {
        public const string TaskSetupDirectory = "Assets/VRGloveDataCapture/Scenes/TaskSetups";
        public const string PickPlaceScenePath = TaskSetupDirectory + "/PickPlaceTasks.unity";

        private const string MaterialDirectory = "Assets/VRGloveDataCapture/Materials/TaskSetups";
        private static bool isManagingScenes;

        static TaskSetupSceneWorkspace()
        {
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.delayCall += EnsurePickPlaceSceneExists;
            EditorApplication.delayCall += EnsureBaseScenesForOpenTaskSetups;
        }

        [MenuItem("Tools/VR Glove Data Capture/Task Setups/Open Pick Place Task Setup", priority = 100)]
        [Shortcut(
            "VR Glove Data Capture/Task Setups/Open Pick Place Task Setup",
            KeyCode.F6,
            ShortcutModifiers.Action | ShortcutModifiers.Shift)]
        public static void OpenPickPlaceTaskSetup()
        {
            EnsurePickPlaceSceneExists();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PickPlaceScenePath) == null)
            {
                Debug.LogError("[TaskSetup] PickPlaceTasks.unity could not be created.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Scene taskScene = EditorSceneManager.OpenScene(PickPlaceScenePath, OpenSceneMode.Single);
            EnsureBaseSceneLoaded(taskScene);
        }

        [MenuItem("Tools/VR Glove Data Capture/Task Setups/Create Empty Task Setup Scene", priority = 101)]
        public static void CreateEmptyTaskSetupScene()
        {
            string scenePath = EditorUtility.SaveFilePanelInProject(
                "Create task setup scene",
                "NewTaskSetup",
                "unity",
                "Save the editable task setup under Assets/VRGloveDataCapture/Scenes/TaskSetups.",
                TaskSetupDirectory);
            if (string.IsNullOrEmpty(scenePath))
            {
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Scene taskScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateMarker(taskScene, Path.GetFileNameWithoutExtension(scenePath));
            GameObject content = new GameObject("Task_Setup_Content");
            SceneManager.MoveGameObjectToScene(content, taskScene);
            EditorSceneManager.SaveScene(taskScene, scenePath);
            EnsureBaseSceneLoaded(taskScene);
            Selection.activeGameObject = content;
        }

        [MenuItem("Tools/VR Glove Data Capture/Task Setups/Rebuild Pick Place Task Setup", priority = 120)]
        public static void RebuildPickPlaceTaskSetup()
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "Rebuild PickPlaceTasks.unity",
                "This replaces the editable pick-and-place task scene and discards manual changes made to that scene.",
                "Rebuild",
                "Cancel");
            if (!confirmed || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            RebuildPickPlaceTaskSetupNonInteractive();
        }

        public static void RebuildPickPlaceTaskSetupNonInteractive()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GeneratePickPlaceScene();
            OpenPickPlaceTaskSetup();
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (isManagingScenes || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            TaskSetupSceneMarker marker = FindMarker(scene);
            if (marker == null)
            {
                return;
            }

            EditorApplication.delayCall += delegate
            {
                if (scene.IsValid() && scene.isLoaded)
                {
                    EnsureBaseSceneLoaded(scene);
                }
            };
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.delayCall += EnsureBaseScenesForOpenTaskSetups;
            }
        }

        private static void EnsureBaseScenesForOpenTaskSetups()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || isManagingScenes)
            {
                return;
            }

            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (FindMarker(scene) != null)
                {
                    EnsureBaseSceneLoaded(scene);
                }
            }
        }

        private static void EnsurePickPlaceSceneExists()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                AssetDatabase.LoadAssetAtPath<SceneAsset>(PickPlaceScenePath) != null)
            {
                return;
            }

            GeneratePickPlaceScene();
        }

        private static void GeneratePickPlaceScene()
        {
            if (isManagingScenes)
            {
                return;
            }

            isManagingScenes = true;
            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene taskScene = default(Scene);
            bool reusedCurrentEmptyScene = false;
            try
            {
                EnsureAssetFolder(TaskSetupDirectory);
                EnsureAssetFolder(MaterialDirectory);
                TaskMaterials materials = EnsureTaskMaterials();

                reusedCurrentEmptyScene =
                    previousActiveScene.IsValid() &&
                    string.IsNullOrEmpty(previousActiveScene.path) &&
                    previousActiveScene.rootCount == 0 &&
                    SceneManager.sceneCount == 1;
                taskScene = reusedCurrentEmptyScene
                    ? previousActiveScene
                    : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                CreateMarker(taskScene, "Pick and Place Tasks");
                GameObject layoutRoot = PickPlaceTaskLayout.Build(taskScene, false);
                if (layoutRoot == null)
                {
                    Debug.LogError("[TaskSetup] Failed to build the editable pick-and-place layout.");
                    return;
                }

                AssignPersistentMaterials(layoutRoot, materials);
                EditorSceneManager.MarkSceneDirty(taskScene);
                if (!EditorSceneManager.SaveScene(taskScene, PickPlaceScenePath))
                {
                    Debug.LogError("[TaskSetup] Failed to save " + PickPlaceScenePath + ".");
                    return;
                }

                AssetDatabase.SaveAssets();
                Debug.Log("[TaskSetup] Generated editable scene: " + PickPlaceScenePath);
            }
            finally
            {
                if (!reusedCurrentEmptyScene && taskScene.IsValid() && taskScene.isLoaded)
                {
                    EditorSceneManager.CloseScene(taskScene, true);
                }

                if (!reusedCurrentEmptyScene && previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                {
                    SceneManager.SetActiveScene(previousActiveScene);
                }

                isManagingScenes = false;
            }
        }

        private static void EnsureBaseSceneLoaded(Scene taskScene)
        {
            TaskSetupSceneMarker marker = FindMarker(taskScene);
            if (marker == null)
            {
                return;
            }

            string baseScenePath = marker.BaseScenePath;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(baseScenePath) == null)
            {
                Debug.LogError(
                    "[TaskSetup] The local Hi5 base scene is missing: " + baseScenePath +
                    ". Import the Hi5 Interaction SDK before opening task setups.",
                    marker);
                return;
            }

            isManagingScenes = true;
            try
            {
                Scene baseScene = SceneManager.GetSceneByPath(baseScenePath);
                if (!baseScene.IsValid() || !baseScene.isLoaded)
                {
                    baseScene = EditorSceneManager.OpenScene(baseScenePath, OpenSceneMode.Additive);
                    Debug.Log("[TaskSetup] Loaded the untouched Hi5 base scene additively: " + baseScene.name);
                }

                SceneManager.SetActiveScene(taskScene);
            }
            finally
            {
                isManagingScenes = false;
            }
        }

        private static TaskSetupSceneMarker FindMarker(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return null;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                TaskSetupSceneMarker marker = roots[index].GetComponentInChildren<TaskSetupSceneMarker>(true);
                if (marker != null)
                {
                    return marker;
                }
            }

            return null;
        }

        private static TaskSetupSceneMarker CreateMarker(Scene scene, string setupName)
        {
            GameObject markerObject = new GameObject("Task_Setup_Metadata");
            SceneManager.MoveGameObjectToScene(markerObject, scene);
            TaskSetupSceneMarker marker = markerObject.AddComponent<TaskSetupSceneMarker>();
            marker.Configure(setupName);
            return marker;
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string currentPath = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string nextPath = currentPath + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, parts[index]);
                }

                currentPath = nextPath;
            }
        }

        private static TaskMaterials EnsureTaskMaterials()
        {
            return new TaskMaterials
            {
                structure = EnsureMaterial("TaskStructure", new Color(0.12f, 0.17f, 0.22f, 1f), 0.15f, 0.35f),
                yellow = EnsureMaterial("TargetYellow", new Color(0.95f, 0.68f, 0.12f, 1f), 0f, 0.25f),
                orange = EnsureMaterial("TargetOrange", new Color(0.95f, 0.36f, 0.1f, 1f), 0.05f, 0.32f),
                green = EnsureMaterial("TargetGreen", new Color(0.25f, 0.72f, 0.36f, 1f), 0f, 0.22f),
                red = EnsureMaterial("TaskRed", new Color(0.78f, 0.08f, 0.08f, 1f), 0.05f, 0.25f),
                blue = EnsureMaterial("TaskBlue", new Color(0.06f, 0.28f, 0.85f, 1f), 0.05f, 0.25f)
            };
        }

        private static Material EnsureMaterial(string assetName, Color color, float metallic, float smoothness)
        {
            string assetPath = MaterialDirectory + "/" + assetName + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Standard"));
                material.name = assetName;
                AssetDatabase.CreateAsset(material, assetPath);
            }

            material.color = color;
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void AssignPersistentMaterials(GameObject layoutRoot, TaskMaterials materials)
        {
            Renderer[] renderers = layoutRoot.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                string objectName = renderer.gameObject.name;
                Material material = null;

                if (objectName.StartsWith("Bucket_Wall_") || objectName.StartsWith("Bin_Wall_"))
                {
                    material = materials.structure;
                }
                else if (objectName == "Bucket_Bottom")
                {
                    material = materials.yellow;
                }
                else if (objectName == "Target_Coaster")
                {
                    material = materials.orange;
                }
                else if (objectName == "Bin_Bottom")
                {
                    string parentName = renderer.transform.parent != null ? renderer.transform.parent.name : string.Empty;
                    material = parentName.Contains("red_block")
                        ? materials.red
                        : parentName.Contains("blue_block")
                            ? materials.blue
                            : materials.green;
                }
                else if (objectName == "Red_Sorting_Block")
                {
                    material = materials.red;
                }
                else if (objectName == "Blue_Sorting_Block")
                {
                    material = materials.blue;
                }

                if (material != null)
                {
                    renderer.sharedMaterial = material;
                    EditorUtility.SetDirty(renderer);
                }
            }
        }

        private sealed class TaskMaterials
        {
            internal Material structure;
            internal Material yellow;
            internal Material orange;
            internal Material green;
            internal Material red;
            internal Material blue;
        }
    }
}
