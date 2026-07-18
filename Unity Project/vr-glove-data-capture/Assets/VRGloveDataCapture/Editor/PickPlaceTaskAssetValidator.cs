using System.IO;
using UnityEditor;
using UnityEngine;

namespace VRGloveDataCapture.Editor
{
    /// <summary>Checks the local vendor scene contract and tracked YCB resources.</summary>
    public static class PickPlaceTaskAssetValidator
    {
        private const string VendorScenePath = "Assets/Hi5_Interaction_SDK/Scenes/Vive/TableScene_Vive.unity";
        private const string TaskScenePath = "Assets/VRGloveDataCapture/Scenes/TaskSetups/PickPlaceTasks.unity";

        private static readonly string[] RequiredResources =
        {
            "RoboticsObjects/YCB/005_tomato_soup_can/textured",
            "RoboticsObjects/YCB/025_mug/textured",
            "RoboticsObjects/YCB/055_baseball/textured"
        };

        [MenuItem("Tools/VR Glove Data Capture/Validate Pick Place Task Assets")]
        public static void ValidateFromMenu()
        {
            int errors = Validate();
            if (errors == 0)
            {
                EditorUtility.DisplayDialog(
                    "Pick Place Task Validation",
                    "The editable task scene, required YCB resources, Hi5 layers, and local TableScene_Vive asset are available.",
                    "OK");
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "Pick Place Task Validation",
                    errors + " validation error(s) were found. See the Console for details.",
                    "OK");
            }
        }

        public static void ValidateBatch()
        {
            int errors = Validate();
            Debug.Log("[PickPlaceTasks] Batch asset validation finished with " + errors + " error(s).");
            EditorApplication.Exit(errors == 0 ? 0 : 1);
        }

        private static int Validate()
        {
            int errors = 0;
            for (int index = 0; index < RequiredResources.Length; index++)
            {
                string resourcePath = RequiredResources[index];
                GameObject model = Resources.Load<GameObject>(resourcePath);
                if (model == null)
                {
                    Debug.LogError("[PickPlaceTasks] Required YCB resource is missing: " + resourcePath);
                    errors++;
                    continue;
                }

                Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    Debug.LogError("[PickPlaceTasks] YCB resource has no renderer: " + resourcePath);
                    errors++;
                    continue;
                }

                bool hasTexture = false;
                for (int rendererIndex = 0; rendererIndex < renderers.Length && !hasTexture; rendererIndex++)
                {
                    Material[] materials = renderers[rendererIndex].sharedMaterials;
                    for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                    {
                        if (materials[materialIndex] != null && materials[materialIndex].mainTexture != null)
                        {
                            hasTexture = true;
                            break;
                        }
                    }
                }

                if (!hasTexture)
                {
                    Debug.LogError("[PickPlaceTasks] YCB resource has no imported diffuse texture: " + resourcePath);
                    errors++;
                }
            }

            errors += ValidateLayer("Hi5ObjectGrasp", 11);
            errors += ValidateLayer("Hi5Plane", 12);
            errors += ValidateLayer("Hi5ObjectTrigger", 13);

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string absoluteScenePath = Path.Combine(projectRoot, VendorScenePath);
            if (!File.Exists(absoluteScenePath))
            {
                Debug.LogError(
                    "[PickPlaceTasks] Local Hi5 scene is missing. Import the Interaction SDK before testing: " +
                    VendorScenePath);
                errors++;
            }

            SceneAsset taskScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(TaskScenePath);
            if (taskScene == null)
            {
                Debug.LogError("[PickPlaceTasks] Editable task setup scene is missing: " + TaskScenePath);
                errors++;
            }

            if (errors == 0)
            {
                Debug.Log(
                    "[PickPlaceTasks] Asset validation passed: editable task scene, 3 YCB resources, " +
                    "3 Hi5 layers, and TableScene_Vive.");
            }

            return errors;
        }

        private static int ValidateLayer(string layerName, int expectedIndex)
        {
            int actualIndex = LayerMask.NameToLayer(layerName);
            if (actualIndex == expectedIndex)
            {
                return 0;
            }

            Debug.LogError(
                "[PickPlaceTasks] Layer '" + layerName + "' must be at index " + expectedIndex +
                ", but its current index is " + actualIndex + ".");
            return 1;
        }
    }
}
