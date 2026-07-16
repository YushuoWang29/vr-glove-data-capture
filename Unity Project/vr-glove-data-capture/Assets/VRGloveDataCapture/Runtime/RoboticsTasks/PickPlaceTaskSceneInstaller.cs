using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRGloveDataCapture.RoboticsTasks
{
    /// <summary>
    /// Adds the project-owned robotics tasks to the vendor TableScene_Vive at runtime.
    /// The proprietary scene asset remains untouched and outside version control.
    /// </summary>
    public sealed class PickPlaceTaskSceneInstaller : MonoBehaviour
    {
        private const string SupportedSceneName = "TableScene_Vive";
        private const string InstallerName = "VRGlove_PickPlace_Task_Installer";
        private readonly HashSet<int> installedSceneHandles = new HashSet<int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindObjectOfType<PickPlaceTaskSceneInstaller>() != null)
            {
                return;
            }

            GameObject installerObject = new GameObject(InstallerName);
            DontDestroyOnLoad(installerObject);
            installerObject.AddComponent<PickPlaceTaskSceneInstaller>();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.isLoaded)
            {
                OnSceneLoaded(activeScene, LoadSceneMode.Single);
            }
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
        }

        private void OnSceneUnloaded(Scene scene)
        {
            installedSceneHandles.Remove(scene.handle);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode loadMode)
        {
            if (scene.name != SupportedSceneName || installedSceneHandles.Contains(scene.handle))
            {
                return;
            }

            StartCoroutine(BuildWhenHi5IsReady(scene));
        }

        private IEnumerator BuildWhenHi5IsReady(Scene scene)
        {
            const int maximumFramesToWait = 300;
            for (int frame = 0; frame < maximumFramesToWait; frame++)
            {
                if (!scene.isLoaded)
                {
                    yield break;
                }

                if (Hi5RuntimeBridge.IsSimpleObjectManagerReady())
                {
                    if (GameObject.Find(PickPlaceTaskLayout.LayoutRootName) == null)
                    {
                        GameObject layout = PickPlaceTaskLayout.Build(scene);
                        if (layout != null)
                        {
                            installedSceneHandles.Add(scene.handle);
                            Debug.Log("[PickPlaceTasks] YCB pick-and-place task layout installed in " + scene.name + ".");
                        }
                    }
                    else
                    {
                        installedSceneHandles.Add(scene.handle);
                    }

                    yield break;
                }

                yield return null;
            }

            Debug.LogError("[PickPlaceTasks] Hi5 simple-object manager did not become ready; task layout was not installed.");
        }
    }

    internal static class PickPlaceTaskLayout
    {
        internal const string LayoutRootName = "VRGlove_PickPlace_Tasks";

        private const int Hi5ObjectLayer = 11;
        private const int Hi5PlaneLayer = 12;
        private const int Hi5TriggerLayer = 13;

        private static readonly Color SlateColor = new Color(0.12f, 0.17f, 0.22f, 1f);
        private static readonly Color WhiteColor = new Color(0.9f, 0.94f, 0.98f, 1f);
        private static readonly Color YellowColor = new Color(0.95f, 0.68f, 0.12f, 1f);
        private static readonly Color OrangeColor = new Color(0.95f, 0.36f, 0.1f, 1f);
        private static readonly Color RedColor = new Color(0.78f, 0.08f, 0.08f, 1f);
        private static readonly Color BlueColor = new Color(0.06f, 0.28f, 0.85f, 1f);

        internal static GameObject Build(Scene scene)
        {
            Vector3 anchor = FindTaskAnchor();
            float surfaceY = FindSurfaceHeight(anchor);
            Vector3 origin = new Vector3(anchor.x, surfaceY, anchor.z + 0.27f);

            GameObject root = new GameObject(LayoutRootName);
            SceneManager.MoveGameObjectToScene(root, scene);

            PickPlaceTaskSceneController controller = root.AddComponent<PickPlaceTaskSceneController>();
            Material structureMaterial = CreateMaterial("Task structure", SlateColor, 0.15f, 0.35f);
            Material sourceMaterial = CreateMaterial("Pick source", new Color(0.16f, 0.55f, 0.78f, 1f), 0f, 0.2f);

            CreateHeader(root.transform, origin + new Vector3(0f, 0.012f, 0.25f));

            int createdTaskCount = 0;

            // Task 1: a spherical object into a round bucket.
            Vector3 ballStart = origin + new Vector3(-0.66f, 0f, -0.10f);
            Vector3 bucketCenter = origin + new Vector3(-0.47f, 0f, 0.10f);
            CreateSourcePad(root.transform, ballStart, sourceMaterial);
            Renderer bucketIndicator;
            PickPlaceTargetZone bucketZone = CreateBucket(
                root.transform,
                bucketCenter,
                "ball_to_bucket",
                controller,
                structureMaterial,
                YellowColor,
                out bucketIndicator);
            controller.RegisterTarget(bucketZone);
            PickPlaceTaskObject ball = CreateYcbObject(
                root.transform,
                "YCB_Baseball",
                "RoboticsObjects/YCB/055_baseball/textured",
                "ball_to_bucket",
                -1001,
                ballStart,
                surfaceY,
                0.074f,
                0.15f,
                true,
                controller);
            if (ball != null)
            {
                createdTaskCount++;
            }
            CreateLabel(root.transform, "1  BALL  >  BUCKET", origin + new Vector3(-0.565f, 0.009f, -0.205f), WhiteColor, 0.011f);

            // Task 2: mug onto a marked coaster.
            Vector3 mugStart = origin + new Vector3(-0.27f, 0f, -0.10f);
            Vector3 coasterCenter = origin + new Vector3(-0.08f, 0f, 0.10f);
            CreateSourcePad(root.transform, mugStart, sourceMaterial);
            PickPlaceTargetZone coasterZone = CreateCoaster(
                root.transform,
                coasterCenter,
                "mug_to_coaster",
                controller,
                OrangeColor);
            controller.RegisterTarget(coasterZone);
            PickPlaceTaskObject mug = CreateYcbObject(
                root.transform,
                "YCB_Mug",
                "RoboticsObjects/YCB/025_mug/textured",
                "mug_to_coaster",
                -1002,
                mugStart,
                surfaceY,
                0.117f,
                0.24f,
                false,
                controller);
            if (mug != null)
            {
                createdTaskCount++;
            }
            CreateLabel(root.transform, "2  MUG  >  COASTER", origin + new Vector3(-0.175f, 0.009f, -0.205f), WhiteColor, 0.011f);

            // Task 3: cylindrical can into a rectangular tote.
            Vector3 canStart = origin + new Vector3(0.12f, 0f, -0.10f);
            Vector3 canBinCenter = origin + new Vector3(0.31f, 0f, 0.10f);
            CreateSourcePad(root.transform, canStart, sourceMaterial);
            PickPlaceTargetZone canBinZone = CreateBin(
                root.transform,
                canBinCenter,
                new Vector2(0.17f, 0.17f),
                0.105f,
                "can_to_bin",
                controller,
                structureMaterial,
                new Color(0.25f, 0.72f, 0.36f, 1f));
            controller.RegisterTarget(canBinZone);
            PickPlaceTaskObject can = CreateYcbObject(
                root.transform,
                "YCB_TomatoSoupCan",
                "RoboticsObjects/YCB/005_tomato_soup_can/textured",
                "can_to_bin",
                -1003,
                canStart,
                surfaceY,
                0.102f,
                0.32f,
                false,
                controller);
            if (can != null)
            {
                createdTaskCount++;
            }
            CreateLabel(root.transform, "3  CAN  >  BIN", origin + new Vector3(0.215f, 0.009f, -0.205f), WhiteColor, 0.011f);

            // Task 4: two-color sorting, represented as two independently scored placements.
            Vector3 redStart = origin + new Vector3(0.50f, 0f, -0.12f);
            Vector3 blueStart = origin + new Vector3(0.66f, 0f, -0.12f);
            Vector3 redBinCenter = origin + new Vector3(0.50f, 0f, 0.10f);
            Vector3 blueBinCenter = origin + new Vector3(0.68f, 0f, 0.10f);
            CreateSourcePad(root.transform, redStart, sourceMaterial);
            CreateSourcePad(root.transform, blueStart, sourceMaterial);
            controller.RegisterTarget(CreateBin(
                root.transform,
                redBinCenter,
                new Vector2(0.13f, 0.14f),
                0.08f,
                "red_block_to_red_bin",
                controller,
                structureMaterial,
                RedColor));
            controller.RegisterTarget(CreateBin(
                root.transform,
                blueBinCenter,
                new Vector2(0.13f, 0.14f),
                0.08f,
                "blue_block_to_blue_bin",
                controller,
                structureMaterial,
                BlueColor));

            PickPlaceTaskObject redBlock = CreateBlock(
                root.transform,
                "Red_Sorting_Block",
                "red_block_to_red_bin",
                -1004,
                redStart,
                surfaceY,
                RedColor,
                controller);
            PickPlaceTaskObject blueBlock = CreateBlock(
                root.transform,
                "Blue_Sorting_Block",
                "blue_block_to_blue_bin",
                -1005,
                blueStart,
                surfaceY,
                BlueColor,
                controller);
            if (redBlock != null)
            {
                createdTaskCount++;
            }
            if (blueBlock != null)
            {
                createdTaskCount++;
            }
            CreateLabel(root.transform, "4  SORT  RED / BLUE", origin + new Vector3(0.59f, 0.009f, -0.225f), WhiteColor, 0.0105f);

            controller.Initialize(createdTaskCount);
            return root;
        }

        private static Vector3 FindTaskAnchor()
        {
            GameObject anchorObject = GameObject.Find("Interaction_Simple_Object_3");
            return anchorObject != null ? anchorObject.transform.position : new Vector3(0.08f, 0.79f, 0.41f);
        }

        private static float FindSurfaceHeight(Vector3 anchor)
        {
            GameObject anchorObject = GameObject.Find("Interaction_Simple_Object_3");
            Renderer anchorRenderer = anchorObject != null ? anchorObject.GetComponentInChildren<Renderer>() : null;
            if (anchorRenderer != null)
            {
                return anchorRenderer.bounds.min.y;
            }

            return anchor.y - 0.10f;
        }

        private static PickPlaceTaskObject CreateYcbObject(
            Transform parent,
            string objectName,
            string resourcePath,
            string taskId,
            int objectId,
            Vector3 horizontalStart,
            float surfaceY,
            float desiredLargestDimension,
            float mass,
            bool sphericalCollider,
            PickPlaceTaskSceneController controller)
        {
            GameObject source = Resources.Load<GameObject>(resourcePath);
            if (source == null)
            {
                Debug.LogError("[PickPlaceTasks] Missing YCB model at Resources/" + resourcePath + ".");
                return null;
            }

            GameObject root = new GameObject(objectName);
            root.SetActive(false);
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(horizontalStart.x, 0f, horizontalStart.z);
            SetLayerRecursively(root, Hi5ObjectLayer);

            GameObject visual = Object.Instantiate(source, root.transform);
            visual.name = "YCB_Visual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            SetLayerRecursively(visual, Hi5ObjectLayer);

            Bounds visualBounds;
            if (!TryGetRendererBounds(visual, out visualBounds))
            {
                Object.Destroy(root);
                Debug.LogError("[PickPlaceTasks] YCB model has no renderer: " + resourcePath);
                return null;
            }

            float currentLargestDimension = Mathf.Max(visualBounds.size.x, visualBounds.size.y, visualBounds.size.z);
            if (currentLargestDimension > 0.0001f)
            {
                visual.transform.localScale *= desiredLargestDimension / currentLargestDimension;
            }

            TryGetRendererBounds(visual, out visualBounds);
            Vector3 centerOffset = root.transform.InverseTransformVector(visualBounds.center - root.transform.position);
            visual.transform.localPosition -= centerOffset;
            TryGetRendererBounds(visual, out visualBounds);

            Collider physicalCollider;
            if (sphericalCollider)
            {
                SphereCollider sphere = root.AddComponent<SphereCollider>();
                sphere.center = root.transform.InverseTransformPoint(visualBounds.center);
                sphere.radius = Mathf.Max(visualBounds.extents.x, visualBounds.extents.y, visualBounds.extents.z) * 0.96f;
                physicalCollider = sphere;
            }
            else
            {
                BoxCollider box = root.AddComponent<BoxCollider>();
                box.center = root.transform.InverseTransformPoint(visualBounds.center);
                box.size = visualBounds.size * 0.94f;
                physicalCollider = box;
            }

            root.transform.position = new Vector3(
                horizontalStart.x,
                surfaceY + physicalCollider.bounds.extents.y + 0.008f,
                horizontalStart.z);

            ConfigureRigidbody(root, mass);
            PickPlaceTaskObject taskObject = root.AddComponent<PickPlaceTaskObject>();
            taskObject.Configure(taskId);

            if (!Hi5RuntimeBridge.AddSimpleObjectComponents(root, objectId, objectName))
            {
                Object.Destroy(root);
                return null;
            }

            root.SetActive(true);
            controller.RegisterObject(taskObject);
            return taskObject;
        }

        private static PickPlaceTaskObject CreateBlock(
            Transform parent,
            string objectName,
            string taskId,
            int objectId,
            Vector3 horizontalStart,
            float surfaceY,
            Color color,
            PickPlaceTaskSceneController controller)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = objectName;
            block.SetActive(false);
            block.transform.SetParent(parent, false);
            block.transform.position = new Vector3(horizontalStart.x, surfaceY + 0.041f, horizontalStart.z);
            block.transform.localScale = new Vector3(0.065f, 0.065f, 0.065f);
            block.layer = Hi5ObjectLayer;
            block.GetComponent<Renderer>().material = CreateMaterial(objectName, color, 0.05f, 0.25f);

            ConfigureRigidbody(block, 0.18f);
            PickPlaceTaskObject taskObject = block.AddComponent<PickPlaceTaskObject>();
            taskObject.Configure(taskId);

            if (!Hi5RuntimeBridge.AddSimpleObjectComponents(block, objectId, objectName))
            {
                Object.Destroy(block);
                return null;
            }

            block.SetActive(true);
            controller.RegisterObject(taskObject);
            return taskObject;
        }

        private static void ConfigureRigidbody(GameObject target, float mass)
        {
            Rigidbody body = target.AddComponent<Rigidbody>();
            body.mass = mass;
            body.drag = 0.5f;
            body.angularDrag = 0.08f;
            body.useGravity = true;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        private static void CreateSourcePad(Transform parent, Vector3 center, Material material)
        {
            CreatePrimitivePart(
                parent,
                "Pick_Source_Pad",
                PrimitiveType.Cylinder,
                center + new Vector3(0f, 0.004f, 0f),
                new Vector3(0.13f, 0.004f, 0.13f),
                Quaternion.identity,
                material,
                Hi5PlaneLayer);
        }

        private static PickPlaceTargetZone CreateBucket(
            Transform parent,
            Vector3 center,
            string taskId,
            PickPlaceTaskSceneController controller,
            Material wallMaterial,
            Color targetColor,
            out Renderer indicator)
        {
            GameObject bucketRoot = new GameObject("Target_Bucket");
            bucketRoot.transform.SetParent(parent, false);

            Material indicatorMaterial = CreateMaterial("Bucket target", targetColor, 0f, 0.25f);
            GameObject bottom = CreatePrimitivePart(
                bucketRoot.transform,
                "Bucket_Bottom",
                PrimitiveType.Cylinder,
                center + new Vector3(0f, 0.009f, 0f),
                new Vector3(0.17f, 0.009f, 0.17f),
                Quaternion.identity,
                indicatorMaterial,
                Hi5PlaneLayer);
            indicator = bottom.GetComponent<Renderer>();

            const int wallCount = 12;
            const float radius = 0.082f;
            for (int index = 0; index < wallCount; index++)
            {
                float angle = index * Mathf.PI * 2f / wallCount;
                Vector3 wallPosition = center + new Vector3(
                    Mathf.Cos(angle) * radius,
                    0.068f,
                    Mathf.Sin(angle) * radius);
                CreatePrimitivePart(
                    bucketRoot.transform,
                    "Bucket_Wall_" + index,
                    PrimitiveType.Cube,
                    wallPosition,
                    new Vector3(0.047f, 0.13f, 0.023f),
                    Quaternion.Euler(0f, -angle * Mathf.Rad2Deg - 90f, 0f),
                    wallMaterial,
                    Hi5PlaneLayer);
            }

            return CreateTargetZone(
                bucketRoot.transform,
                "Bucket_Success_Zone",
                center + new Vector3(0f, 0.057f, 0f),
                new Vector3(0.12f, 0.095f, 0.12f),
                taskId,
                controller,
                indicator,
                targetColor);
        }

        private static PickPlaceTargetZone CreateCoaster(
            Transform parent,
            Vector3 center,
            string taskId,
            PickPlaceTaskSceneController controller,
            Color targetColor)
        {
            Material targetMaterial = CreateMaterial("Coaster target", targetColor, 0.05f, 0.32f);
            GameObject coaster = CreatePrimitivePart(
                parent,
                "Target_Coaster",
                PrimitiveType.Cylinder,
                center + new Vector3(0f, 0.007f, 0f),
                new Vector3(0.14f, 0.007f, 0.14f),
                Quaternion.identity,
                targetMaterial,
                Hi5PlaneLayer);

            return CreateTargetZone(
                parent,
                "Coaster_Success_Zone",
                center + new Vector3(0f, 0.055f, 0f),
                new Vector3(0.13f, 0.09f, 0.13f),
                taskId,
                controller,
                coaster.GetComponent<Renderer>(),
                targetColor);
        }

        private static PickPlaceTargetZone CreateBin(
            Transform parent,
            Vector3 center,
            Vector2 footprint,
            float wallHeight,
            string taskId,
            PickPlaceTaskSceneController controller,
            Material wallMaterial,
            Color targetColor)
        {
            GameObject binRoot = new GameObject("Target_Bin_" + taskId);
            binRoot.transform.SetParent(parent, false);

            const float wallThickness = 0.018f;
            Material indicatorMaterial = CreateMaterial(taskId + " target", targetColor, 0f, 0.22f);
            GameObject bottom = CreatePrimitivePart(
                binRoot.transform,
                "Bin_Bottom",
                PrimitiveType.Cube,
                center + new Vector3(0f, 0.008f, 0f),
                new Vector3(footprint.x, 0.016f, footprint.y),
                Quaternion.identity,
                indicatorMaterial,
                Hi5PlaneLayer);

            float halfX = footprint.x * 0.5f;
            float halfZ = footprint.y * 0.5f;
            float wallY = wallHeight * 0.5f;
            CreatePrimitivePart(parent: binRoot.transform, name: "Bin_Wall_Left", primitiveType: PrimitiveType.Cube,
                position: center + new Vector3(-halfX, wallY, 0f), scale: new Vector3(wallThickness, wallHeight, footprint.y),
                rotation: Quaternion.identity, material: wallMaterial, layer: Hi5PlaneLayer);
            CreatePrimitivePart(parent: binRoot.transform, name: "Bin_Wall_Right", primitiveType: PrimitiveType.Cube,
                position: center + new Vector3(halfX, wallY, 0f), scale: new Vector3(wallThickness, wallHeight, footprint.y),
                rotation: Quaternion.identity, material: wallMaterial, layer: Hi5PlaneLayer);
            CreatePrimitivePart(parent: binRoot.transform, name: "Bin_Wall_Front", primitiveType: PrimitiveType.Cube,
                position: center + new Vector3(0f, wallY, -halfZ), scale: new Vector3(footprint.x, wallHeight, wallThickness),
                rotation: Quaternion.identity, material: wallMaterial, layer: Hi5PlaneLayer);
            CreatePrimitivePart(parent: binRoot.transform, name: "Bin_Wall_Back", primitiveType: PrimitiveType.Cube,
                position: center + new Vector3(0f, wallY, halfZ), scale: new Vector3(footprint.x, wallHeight, wallThickness),
                rotation: Quaternion.identity, material: wallMaterial, layer: Hi5PlaneLayer);

            return CreateTargetZone(
                binRoot.transform,
                "Bin_Success_Zone",
                center + new Vector3(0f, wallHeight * 0.46f, 0f),
                new Vector3(footprint.x * 0.75f, wallHeight * 0.72f, footprint.y * 0.75f),
                taskId,
                controller,
                bottom.GetComponent<Renderer>(),
                targetColor);
        }

        private static PickPlaceTargetZone CreateTargetZone(
            Transform parent,
            string name,
            Vector3 position,
            Vector3 size,
            string taskId,
            PickPlaceTaskSceneController controller,
            Renderer indicator,
            Color readyColor)
        {
            GameObject zoneObject = new GameObject(name);
            zoneObject.transform.SetParent(parent, false);
            zoneObject.transform.position = position;
            zoneObject.layer = Hi5TriggerLayer;
            BoxCollider trigger = zoneObject.AddComponent<BoxCollider>();
            trigger.size = size;
            trigger.isTrigger = true;
            PickPlaceTargetZone zone = zoneObject.AddComponent<PickPlaceTargetZone>();
            zone.Configure(taskId, controller, indicator, readyColor);
            return zone;
        }

        private static GameObject CreatePrimitivePart(
            Transform parent,
            string name,
            PrimitiveType primitiveType,
            Vector3 position,
            Vector3 scale,
            Quaternion rotation,
            Material material,
            int layer)
        {
            GameObject part = GameObject.CreatePrimitive(primitiveType);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.position = position;
            part.transform.rotation = rotation;
            part.transform.localScale = scale;
            part.layer = layer;
            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = material;
            }
            return part;
        }

        private static void CreateHeader(Transform parent, Vector3 position)
        {
            CreateLabel(parent, "ROBOT PICK & PLACE BENCHMARKS     RESET: SCENE BUTTON / F8", position, YellowColor, 0.0105f);
        }

        private static void CreateLabel(Transform parent, string text, Vector3 position, Color color, float characterSize)
        {
            GameObject labelObject = new GameObject("Task_Label_" + text);
            labelObject.transform.SetParent(parent, false);
            labelObject.transform.position = position;
            labelObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            TextMesh label = labelObject.AddComponent<TextMesh>();
            label.text = text;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.characterSize = characterSize;
            label.fontSize = 48;
            label.color = color;
        }

        private static Material CreateMaterial(string name, Color color, float metallic, float smoothness)
        {
            Shader shader = Shader.Find("Standard");
            Material material = new Material(shader);
            material.name = name;
            material.color = color;
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", smoothness);
            return material;
        }

        private static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                bounds = new Bounds();
                return false;
            }

            bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }
            return true;
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            for (int index = 0; index < root.transform.childCount; index++)
            {
                SetLayerRecursively(root.transform.GetChild(index).gameObject, layer);
            }
        }
    }
}
