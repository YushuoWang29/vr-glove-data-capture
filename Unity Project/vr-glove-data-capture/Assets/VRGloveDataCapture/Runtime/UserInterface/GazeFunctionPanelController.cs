using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using VRGloveDataCapture.Capture;
using VRGloveDataCapture.MixedReality;
using VRGloveDataCapture.RoboticsTasks;

namespace VRGloveDataCapture.UserInterface
{
    /// <summary>
    /// Replaces the vendor main-menu content after full calibration with a
    /// gaze-operated control center. Calibration screens and vendor code remain
    /// untouched and are restored whenever recalibration starts.
    /// </summary>
    public sealed class GazeFunctionPanelController : MonoBehaviour
    {
        private sealed class ButtonView
        {
            public string Id;
            public TextMesh Title;
            public TextMesh Status;
            public MeshRenderer Surface;
            public Material Material;
        }

        private const string InstallerName = "[VR Glove Gaze Control Panel]";
        private const string PanelName = "VRGlove_FunctionControlPanel";
        private static GazeFunctionPanelController instance;
        private static readonly Color PanelColor = new Color(0.945f, 0.952f, 0.955f, 0.985f);
        private static readonly Color BorderColor = new Color(0.72f, 0.75f, 0.77f, 1.0f);
        private static readonly Color TextColor = new Color(0.17f, 0.18f, 0.19f, 1.0f);
        private static readonly Color SecondaryTextColor = new Color(0.34f, 0.36f, 0.38f, 1.0f);
        private static readonly Color ReadyColor = new Color(0.985f, 0.988f, 0.99f, 1.0f);
        private static readonly Color ActiveColor = new Color(0.82f, 0.94f, 0.90f, 1.0f);
        private static readonly Color RecordingColor = new Color(0.98f, 0.84f, 0.84f, 1.0f);
        private static readonly Color WarningColor = new Color(0.99f, 0.92f, 0.76f, 1.0f);
        private static readonly Color UnavailableColor = new Color(0.88f, 0.89f, 0.90f, 1.0f);

        private readonly List<GameObject> originalMainChildren = new List<GameObject>();
        private readonly List<ButtonView> buttons = new List<ButtonView>();
        private readonly List<Material> ownedMaterials = new List<Material>();
        private readonly List<Mesh> ownedMeshes = new List<Mesh>();

        private Component menuStateMachine;
        private Component calibrationStateMachine;
        private Component selectionRadial;
        private Type interactiveItemType;
        private Transform mainStateRoot;
        private GameObject panelRoot;
        private TextMesh noticeText;
        private bool featureUnlocked;
        private bool waitingForRecalibration;
        private bool sawRecalibrationInProgress;
        private float nextDiscoveryTime;
        private float nextStatusUpdateTime;
        private string notice = "CALIBRATION COMPLETE - gaze at a control to activate it.";

        public bool IsFeaturePanelVisible
        {
            get { return panelRoot != null && panelRoot.activeInHierarchy; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (instance != null)
            {
                return;
            }

            GazeFunctionPanelController[] existing =
                Resources.FindObjectsOfTypeAll<GazeFunctionPanelController>();
            if (existing.Length > 0)
            {
                instance = existing[0];
                return;
            }

            GameObject installer = new GameObject(InstallerName);
            installer.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(installer);
            instance = installer.AddComponent<GazeFunctionPanelController>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (!HasValidPanel())
            {
                if (Time.unscaledTime >= nextDiscoveryTime)
                {
                    nextDiscoveryTime = Time.unscaledTime + 0.5f;
                    DiscoverAndBuild();
                }
                return;
            }

            UpdateCalibrationTransition();
            if (Time.unscaledTime >= nextStatusUpdateTime)
            {
                nextStatusUpdateTime = Time.unscaledTime + 0.15f;
                RefreshStatus();
            }
        }

        private bool HasValidPanel()
        {
            return menuStateMachine != null && mainStateRoot != null && panelRoot != null;
        }

        private void DiscoverAndBuild()
        {
            Component nextMenu = FindSceneComponent("HI5.VRCalibration.MenuStateMachine");
            if (nextMenu == null)
            {
                return;
            }

            if (menuStateMachine == nextMenu && HasValidPanel())
            {
                return;
            }

            TearDownPanel();
            menuStateMachine = nextMenu;
            calibrationStateMachine = FindSceneComponent("HI5.VRCalibration.CalibrationStateMachine");
            Component raycaster = FindSceneComponent("HI5.VRInteraction.VREyeRaycaster");
            interactiveItemType = FindType("HI5.VRInteraction.VRInteractiveItem");
            selectionRadial = ReadField(raycaster, "m_SelectionRadial") as Component;
            mainStateRoot = FindMainStateRoot(menuStateMachine);

            if (interactiveItemType == null || selectionRadial == null || mainStateRoot == null)
            {
                TearDownPanel();
                return;
            }

            for (int index = 0; index < mainStateRoot.childCount; index++)
            {
                originalMainChildren.Add(mainStateRoot.GetChild(index).gameObject);
            }

            BuildPanel();
            featureUnlocked = IsManagerCalibrationComplete() ||
                              string.Equals(GetCalibrationStateName(), "Finish", StringComparison.Ordinal);
            SetFeaturePanel(featureUnlocked);
            if (featureUnlocked)
            {
                SetMenuState("Main");
            }

            Debug.Log(
                "[GazeControlPanel] Installed on the vendor main menu; calibration UI remains unchanged.",
                this);
        }

        private void UpdateCalibrationTransition()
        {
            string state = GetCalibrationStateName();
            if (waitingForRecalibration)
            {
                if (!string.IsNullOrEmpty(state) &&
                    !string.Equals(state, "Finish", StringComparison.Ordinal) &&
                    !string.Equals(state, "Exit", StringComparison.Ordinal))
                {
                    sawRecalibrationInProgress = true;
                }

                if (sawRecalibrationInProgress &&
                    string.Equals(state, "Finish", StringComparison.Ordinal))
                {
                    waitingForRecalibration = false;
                    featureUnlocked = true;
                    notice = "RECALIBRATION COMPLETE - controls are ready.";
                    SetFeaturePanel(true);
                    SetMenuState("Main");
                }
                return;
            }

            if (!featureUnlocked &&
                (IsManagerCalibrationComplete() ||
                 string.Equals(state, "Finish", StringComparison.Ordinal)))
            {
                featureUnlocked = true;
                notice = "CALIBRATION COMPLETE - controls are ready.";
                SetFeaturePanel(true);
                SetMenuState("Main");
            }
        }

        private void BuildPanel()
        {
            panelRoot = new GameObject(PanelName);
            panelRoot.transform.SetParent(mainStateRoot, false);
            panelRoot.transform.localPosition = new Vector3(0.0f, 0.0f, -0.08f);

            CreateRoundedSurface(
                "BackgroundBorder",
                panelRoot.transform,
                Vector3.zero,
                new Vector2(9.5f, 5.85f),
                0.24f,
                BorderColor,
                -0.004f);
            CreateRoundedSurface(
                "Background",
                panelRoot.transform,
                Vector3.zero,
                new Vector2(9.42f, 5.77f),
                0.21f,
                PanelColor,
                -0.012f);
            CreateText(
                "Title",
                panelRoot.transform,
                new Vector3(0.0f, 2.30f, -0.06f),
                "VR GLOVE CONTROL CENTER",
                54,
                0.052f,
                TextColor);
            CreateText(
                "Instruction",
                panelRoot.transform,
                new Vector3(0.0f, 1.87f, -0.06f),
                "GAZE UNTIL THE RADIAL COMPLETES",
                38,
                0.036f,
                SecondaryTextColor);

            CreateButton("passthrough", new Vector3(-2.15f, 1.08f, -0.04f), TogglePassthrough);
            CreateButton("video", new Vector3(2.15f, 1.08f, -0.04f), ToggleVideoRecording);
            CreateButton("trial", new Vector3(-2.15f, -0.03f, -0.04f), ToggleTrial);
            CreateButton("marker", new Vector3(2.15f, -0.03f, -0.04f), AddMarker);
            CreateButton("reset", new Vector3(-2.15f, -1.14f, -0.04f), ResetScene);
            CreateButton("recalibrate", new Vector3(2.15f, -1.14f, -0.04f), StartRecalibration);

            noticeText = CreateText(
                "Notice",
                panelRoot.transform,
                new Vector3(0.0f, -2.34f, -0.06f),
                notice,
                36,
                0.032f,
                SecondaryTextColor);
        }

        private void CreateButton(string id, Vector3 localPosition, Action action)
        {
            GameObject buttonObject = new GameObject("GazeButton_" + id);
            buttonObject.transform.SetParent(panelRoot.transform, false);
            buttonObject.transform.localPosition = localPosition;

            BoxCollider collider = buttonObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(4.0f, 0.84f, 0.08f);

            CreateRoundedSurface(
                "Border",
                buttonObject.transform,
                Vector3.zero,
                new Vector2(4.08f, 0.92f),
                0.18f,
                BorderColor,
                0.008f);

            MeshRenderer surface = CreateRoundedSurface(
                "Surface",
                buttonObject.transform,
                Vector3.zero,
                new Vector2(4.0f, 0.84f),
                0.15f,
                ReadyColor,
                0.0f);
            TextMesh title = CreateText(
                "Title",
                buttonObject.transform,
                new Vector3(0.0f, 0.13f, -0.055f),
                id.ToUpperInvariant(),
                44,
                0.046f,
                TextColor);
            TextMesh status = CreateText(
                "Status",
                buttonObject.transform,
                new Vector3(0.0f, -0.17f, -0.055f),
                "READY",
                34,
                0.035f,
                SecondaryTextColor);

            Component interactiveItem = buttonObject.AddComponent(interactiveItemType) as Component;
            GazeDwellButton dwell = buttonObject.AddComponent<GazeDwellButton>();
            dwell.Configure(interactiveItem, selectionRadial, id, action);

            ButtonView view = new ButtonView
            {
                Id = id,
                Title = title,
                Status = status,
                Surface = surface,
                Material = surface.sharedMaterial
            };
            buttons.Add(view);
            FitButtonText(view);
        }

        private MeshRenderer CreateRoundedSurface(
            string objectName,
            Transform parent,
            Vector3 localPosition,
            Vector2 size,
            float cornerRadius,
            Color color,
            float zOffset)
        {
            GameObject surfaceObject = new GameObject(objectName);
            surfaceObject.transform.SetParent(parent, false);
            surfaceObject.transform.localPosition = localPosition + new Vector3(0.0f, 0.0f, zOffset);
            Mesh mesh = BuildRoundedRectangleMesh(size, cornerRadius, 6);
            ownedMeshes.Add(mesh);
            MeshFilter filter = surfaceObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }
            Material material = new Material(shader)
            {
                color = color,
                hideFlags = HideFlags.DontSave
            };
            ownedMaterials.Add(material);
            MeshRenderer renderer = surfaceObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = 30;
            return renderer;
        }

        private static Mesh BuildRoundedRectangleMesh(Vector2 size, float radius, int cornerSegments)
        {
            float halfWidth = size.x * 0.5f;
            float halfHeight = size.y * 0.5f;
            float safeRadius = Mathf.Clamp(radius, 0.0f, Mathf.Min(halfWidth, halfHeight));
            int perimeterCount = cornerSegments * 4 + 4;
            Vector3[] vertices = new Vector3[perimeterCount + 1];
            int[] triangles = new int[perimeterCount * 3];
            vertices[0] = Vector3.zero;

            int vertexIndex = 1;
            for (int corner = 0; corner < 4; corner++)
            {
                float centerX = corner == 0 || corner == 3
                    ? halfWidth - safeRadius
                    : -halfWidth + safeRadius;
                float centerY = corner < 2
                    ? halfHeight - safeRadius
                    : -halfHeight + safeRadius;
                float startDegrees = corner * 90.0f;
                for (int segment = 0; segment <= cornerSegments; segment++)
                {
                    float angle = (startDegrees + segment * 90.0f / cornerSegments) * Mathf.Deg2Rad;
                    vertices[vertexIndex++] = new Vector3(
                        centerX + Mathf.Cos(angle) * safeRadius,
                        centerY + Mathf.Sin(angle) * safeRadius,
                        0.0f);
                }
            }

            for (int index = 0; index < perimeterCount; index++)
            {
                int triangle = index * 3;
                triangles[triangle] = 0;
                triangles[triangle + 1] = index + 1 == perimeterCount ? 1 : index + 2;
                triangles[triangle + 2] = index + 1;
            }

            Mesh mesh = new Mesh
            {
                name = "VRGloveRoundedRectangle",
                hideFlags = HideFlags.DontSave,
                vertices = vertices,
                triangles = triangles
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static TextMesh CreateText(
            string objectName,
            Transform parent,
            Vector3 localPosition,
            string content,
            int fontSize,
            float characterSize,
            Color color)
        {
            GameObject textObject = new GameObject(objectName);
            textObject.transform.SetParent(parent, false);
            textObject.transform.localPosition = localPosition;
            TextMesh text = textObject.AddComponent<TextMesh>();
            text.text = content;
            text.fontSize = fontSize;
            text.characterSize = characterSize;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = color;
            text.lineSpacing = 0.82f;
            MeshRenderer renderer = textObject.GetComponent<MeshRenderer>();
            renderer.sortingOrder = 50;
            return text;
        }

        private void SetFeaturePanel(bool visible)
        {
            for (int index = 0; index < originalMainChildren.Count; index++)
            {
                if (originalMainChildren[index] != null)
                {
                    originalMainChildren[index].SetActive(!visible);
                }
            }

            if (panelRoot != null)
            {
                panelRoot.SetActive(visible);
            }
        }

        private void RefreshStatus()
        {
            SteamVrPassthroughEffect passthrough = FindObjectOfType<SteamVrPassthroughEffect>();
            string passthroughText;
            Color passthroughColor;
            if (passthrough == null)
            {
                passthroughText = "WAITING FOR VR CAMERA";
                passthroughColor = UnavailableColor;
            }
            else if (!passthrough.IsPassthroughRequested)
            {
                passthroughText = "OFF  ·  GAZE TO ENABLE";
                passthroughColor = ReadyColor;
            }
            else if (passthrough.HasLiveFrames)
            {
                passthroughText = "LIVE  ·  GAZE TO DISABLE";
                passthroughColor = ActiveColor;
            }
            else
            {
                passthroughText = passthrough.State.ToString().ToUpperInvariant();
                passthroughColor = WarningColor;
            }
            UpdateButton("passthrough", "PASSTHROUGH", passthroughText, passthroughColor);

            UpdateButton(
                "video",
                "VR VIEW VIDEO",
                VrViewRecordingHotkey.IsRecording
                    ? "RECORDING  ·  GAZE TO STOP"
                    : "IDLE  ·  GAZE TO RECORD",
                VrViewRecordingHotkey.IsRecording ? RecordingColor : ReadyColor);

            CaptureSessionManager capture = CaptureSessionManager.Instance;
            bool trialActive = capture != null && capture.IsRecording;
            UpdateButton(
                "trial",
                "TRIAL CAPTURE",
                trialActive
                    ? "RECORDING  ·  GAZE TO STOP"
                    : "READY  ·  GAZE TO START",
                trialActive ? RecordingColor : ReadyColor);
            UpdateButton(
                "marker",
                "EVENT MARKER",
                trialActive
                    ? "READY  ·  GAZE TO ADD"
                    : "START A TRIAL FIRST",
                trialActive ? ActiveColor : UnavailableColor);
            UpdateButton("reset", "RESET SCENE", "GAZE TO RESTORE OBJECTS", WarningColor);
            UpdateButton("recalibrate", "RECALIBRATE", "GAZE TO START V/B/P POSES", ReadyColor);

            if (noticeText != null)
            {
                noticeText.text = notice;
                FitTextToWidth(noticeText, 8.4f);
            }
        }

        private void UpdateButton(string id, string title, string status, Color color)
        {
            for (int index = 0; index < buttons.Count; index++)
            {
                ButtonView button = buttons[index];
                if (!string.Equals(button.Id, id, StringComparison.Ordinal))
                {
                    continue;
                }

                button.Title.text = title;
                button.Status.text = status;
                button.Material.color = color;
                FitButtonText(button);
                return;
            }
        }

        private static void FitButtonText(ButtonView button)
        {
            if (button == null || button.Surface == null)
            {
                return;
            }

            FitTextToRenderer(button.Title, button.Surface, 0.88f, 0.36f);
            FitTextToRenderer(button.Status, button.Surface, 0.88f, 0.30f);
        }

        private static void FitTextToRenderer(
            TextMesh text,
            Renderer container,
            float widthFraction,
            float heightFraction)
        {
            if (text == null || container == null)
            {
                return;
            }

            Renderer textRenderer = text.GetComponent<Renderer>();
            if (textRenderer == null)
            {
                return;
            }

            text.transform.localScale = Vector3.one;
            Bounds textBounds = textRenderer.bounds;
            Bounds containerBounds = container.bounds;
            if (textBounds.size.x <= 0.0001f || textBounds.size.y <= 0.0001f)
            {
                return;
            }

            float widthScale = containerBounds.size.x * widthFraction / textBounds.size.x;
            float heightScale = containerBounds.size.y * heightFraction / textBounds.size.y;
            float scale = Mathf.Min(1.0f, widthScale, heightScale);
            text.transform.localScale = Vector3.one * Mathf.Max(0.05f, scale);
        }

        private static void FitTextToWidth(TextMesh text, float localWidth)
        {
            if (text == null)
            {
                return;
            }

            Renderer renderer = text.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            text.transform.localScale = Vector3.one;
            float worldWidth = text.transform.parent == null
                ? localWidth
                : text.transform.parent.TransformVector(Vector3.right * localWidth).magnitude;
            if (renderer.bounds.size.x > worldWidth && renderer.bounds.size.x > 0.0001f)
            {
                text.transform.localScale = Vector3.one * (worldWidth / renderer.bounds.size.x);
            }
        }

        private void TogglePassthrough()
        {
            SteamVrPassthroughEffect effect = FindObjectOfType<SteamVrPassthroughEffect>();
            if (effect == null)
            {
                notice = "PASSTHROUGH UNAVAILABLE - wait for the stereo camera installer.";
                return;
            }

            effect.TogglePassthrough();
            notice = effect.IsPassthroughRequested
                ? "PASSTHROUGH REQUESTED - LIVE confirms real camera frames."
                : "PASSTHROUGH DISABLED.";
        }

        private void ToggleVideoRecording()
        {
            if (VrViewRecordingHotkey.RequestToggle())
            {
                notice = "VR VIDEO COMMAND SENT - status is shown on the button.";
            }
            else
            {
                notice = VrViewRecordingHotkey.Status.ToUpperInvariant();
            }
        }

        private void ToggleTrial()
        {
            CaptureSessionManager capture = CaptureSessionManager.Instance;
            if (capture == null)
            {
                notice = "TRIAL CAPTURE UNAVAILABLE - manager is not installed.";
                return;
            }

            bool wasRecording = capture.IsRecording;
            capture.ToggleTrial();
            if (!wasRecording && !capture.IsRecording)
            {
                notice = "TRIAL NOT STARTED - " + SafeUpper(capture.LastError, "check Capture Control");
            }
            else
            {
                notice = wasRecording
                    ? "TRIAL FINALIZED - files and checksums have been written."
                    : "TRIAL RECORDING - gaze EVENT MARKER to label an event.";
            }
        }

        private void AddMarker()
        {
            CaptureSessionManager capture = CaptureSessionManager.Instance;
            if (capture == null || !capture.IsRecording)
            {
                notice = "EVENT MARKER REQUIRES AN ACTIVE TRIAL.";
                return;
            }

            capture.AddMarker("gaze_panel");
            notice = "EVENT MARKER ADDED WITH THE SHARED TRIAL TIMESTAMP.";
        }

        private void ResetScene()
        {
            PickPlaceTaskSceneController taskController = FindObjectOfType<PickPlaceTaskSceneController>();
            if (taskController != null)
            {
                taskController.RequestReset();
                notice = "SCENE OBJECTS AND TASK PROGRESS RESET.";
                return;
            }

            notice = SceneResetCommand.TryResetAll()
                ? "VENDOR FULL-SCENE RESET MESSAGE SENT."
                : "SCENE RESET UNAVAILABLE IN THIS SCENE.";
        }

        private void StartRecalibration()
        {
            CaptureSessionManager capture = CaptureSessionManager.Instance;
            if ((capture != null && capture.IsRecording) || VrViewRecordingHotkey.IsRecording)
            {
                notice = "STOP TRIAL AND VIDEO RECORDING BEFORE RECALIBRATION.";
                return;
            }

            waitingForRecalibration = true;
            sawRecalibrationInProgress = false;
            featureUnlocked = false;
            notice = "RECALIBRATION STARTED - follow the original V/B/P-pose panel.";
            SetFeaturePanel(false);
            if (!SetMenuState("Calibration"))
            {
                waitingForRecalibration = false;
                featureUnlocked = true;
                SetFeaturePanel(true);
                notice = "RECALIBRATION UNAVAILABLE - vendor menu state was not found.";
            }
        }

        private bool SetMenuState(string stateName)
        {
            if (menuStateMachine == null)
            {
                return false;
            }

            PropertyInfo stateProperty = menuStateMachine.GetType().GetProperty("State");
            if (stateProperty == null || !stateProperty.CanWrite)
            {
                return false;
            }

            try
            {
                object value = Enum.Parse(stateProperty.PropertyType, stateName);
                stateProperty.SetValue(menuStateMachine, value, null);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("[GazeControlPanel] Failed to enter menu state " + stateName + ": " + exception, this);
                return false;
            }
        }

        private string GetCalibrationStateName()
        {
            if (calibrationStateMachine == null)
            {
                calibrationStateMachine = FindSceneComponent("HI5.VRCalibration.CalibrationStateMachine");
            }
            if (calibrationStateMachine == null)
            {
                return string.Empty;
            }

            PropertyInfo stateProperty = calibrationStateMachine.GetType().GetProperty("State");
            object value = stateProperty == null
                ? null
                : stateProperty.GetValue(calibrationStateMachine, null);
            return value == null ? string.Empty : value.ToString();
        }

        private static bool IsManagerCalibrationComplete()
        {
            Type managerType = FindType("HI5.HI5_Manager_Thread") ?? FindType("HI5_Manager_Thread");
            if (managerType == null)
            {
                return false;
            }

            MethodInfo instanceMethod = managerType.GetMethod(
                "Instance",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            object manager = instanceMethod == null ? null : instanceMethod.Invoke(null, null);
            if (manager == null)
            {
                return false;
            }

            FieldInfo completeField = managerType.GetField(
                "IsCalibrationComplete",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return completeField != null && completeField.GetValue(manager) is bool &&
                   (bool)completeField.GetValue(manager);
        }

        private static Transform FindMainStateRoot(Component menu)
        {
            object value = ReadField(menu, "m_StateItems");
            Array states = value as Array;
            if (states != null && states.Length > 0)
            {
                GameObject main = states.GetValue(0) as GameObject;
                if (main != null)
                {
                    return main.transform;
                }
            }

            return menu == null ? null : menu.transform.Find("Main");
        }

        private static object ReadField(Component component, string fieldName)
        {
            if (component == null)
            {
                return null;
            }

            FieldInfo field = component.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(component);
        }

        private static Component FindSceneComponent(string fullTypeName)
        {
            Type type = FindType(fullTypeName);
            if (type == null)
            {
                return null;
            }

            UnityEngine.Object found = FindObjectOfType(type);
            return found as Component;
        }

        private static Type FindType(string fullTypeName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int index = 0; index < assemblies.Length; index++)
            {
                Type type = assemblies[index].GetType(fullTypeName, false);
                if (type != null)
                {
                    return type;
                }
            }
            return null;
        }

        private static string SafeUpper(string value, string fallback)
        {
            return string.IsNullOrEmpty(value) ? fallback.ToUpperInvariant() : value.ToUpperInvariant();
        }

        private void TearDownPanel()
        {
            if (panelRoot != null)
            {
                Destroy(panelRoot);
            }
            for (int index = 0; index < ownedMaterials.Count; index++)
            {
                if (ownedMaterials[index] != null)
                {
                    Destroy(ownedMaterials[index]);
                }
            }
            for (int index = 0; index < ownedMeshes.Count; index++)
            {
                if (ownedMeshes[index] != null)
                {
                    Destroy(ownedMeshes[index]);
                }
            }

            panelRoot = null;
            mainStateRoot = null;
            menuStateMachine = null;
            calibrationStateMachine = null;
            selectionRadial = null;
            interactiveItemType = null;
            originalMainChildren.Clear();
            buttons.Clear();
            ownedMaterials.Clear();
            ownedMeshes.Clear();
            noticeText = null;
        }

        private void OnDestroy()
        {
            TearDownPanel();
            if (instance == this)
            {
                instance = null;
            }
        }
    }
}
