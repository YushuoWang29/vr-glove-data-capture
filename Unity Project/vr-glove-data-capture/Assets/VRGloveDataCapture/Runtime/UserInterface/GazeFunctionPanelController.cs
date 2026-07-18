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
            public TextMesh Label;
            public MeshRenderer Surface;
            public Material Material;
        }

        private const string InstallerName = "[VR Glove Gaze Control Panel]";
        private const string PanelName = "VRGlove_FunctionControlPanel";
        private static GazeFunctionPanelController instance;
        private static readonly Color PanelColor = new Color(0.025f, 0.055f, 0.085f, 0.97f);
        private static readonly Color ReadyColor = new Color(0.055f, 0.20f, 0.29f, 1.0f);
        private static readonly Color ActiveColor = new Color(0.02f, 0.48f, 0.43f, 1.0f);
        private static readonly Color RecordingColor = new Color(0.66f, 0.08f, 0.10f, 1.0f);
        private static readonly Color WarningColor = new Color(0.62f, 0.35f, 0.04f, 1.0f);
        private static readonly Color UnavailableColor = new Color(0.16f, 0.18f, 0.20f, 1.0f);

        private readonly List<GameObject> originalMainChildren = new List<GameObject>();
        private readonly List<ButtonView> buttons = new List<ButtonView>();
        private readonly List<Material> ownedMaterials = new List<Material>();

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

            CreateSurface("Background", panelRoot.transform, Vector3.zero, new Vector2(9.2f, 5.9f), PanelColor, -0.01f);
            CreateText(
                "Title",
                panelRoot.transform,
                new Vector3(0.0f, 2.35f, -0.06f),
                "VR GLOVE CONTROL CENTER",
                66,
                0.085f,
                Color.white);
            CreateText(
                "Instruction",
                panelRoot.transform,
                new Vector3(0.0f, 1.88f, -0.06f),
                "Hold gaze until the original radial completes. Keyboard shortcuts remain available.",
                42,
                0.052f,
                new Color(0.70f, 0.84f, 0.91f, 1.0f));

            CreateButton("passthrough", new Vector3(-2.15f, 1.08f, -0.04f), TogglePassthrough);
            CreateButton("video", new Vector3(2.15f, 1.08f, -0.04f), ToggleVideoRecording);
            CreateButton("trial", new Vector3(-2.15f, -0.03f, -0.04f), ToggleTrial);
            CreateButton("marker", new Vector3(2.15f, -0.03f, -0.04f), AddMarker);
            CreateButton("reset", new Vector3(-2.15f, -1.14f, -0.04f), ResetScene);
            CreateButton("recalibrate", new Vector3(2.15f, -1.14f, -0.04f), StartRecalibration);

            noticeText = CreateText(
                "Notice",
                panelRoot.transform,
                new Vector3(0.0f, -2.27f, -0.06f),
                notice,
                42,
                0.055f,
                new Color(0.94f, 0.78f, 0.33f, 1.0f));
        }

        private void CreateButton(string id, Vector3 localPosition, Action action)
        {
            GameObject buttonObject = new GameObject("GazeButton_" + id);
            buttonObject.transform.SetParent(panelRoot.transform, false);
            buttonObject.transform.localPosition = localPosition;

            BoxCollider collider = buttonObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(4.0f, 0.88f, 0.08f);

            MeshRenderer surface = CreateSurface(
                "Surface",
                buttonObject.transform,
                Vector3.zero,
                new Vector2(4.0f, 0.88f),
                ReadyColor,
                0.0f);
            TextMesh label = CreateText(
                "Label",
                buttonObject.transform,
                new Vector3(0.0f, 0.0f, -0.055f),
                id.ToUpperInvariant(),
                52,
                0.070f,
                Color.white);

            Component interactiveItem = buttonObject.AddComponent(interactiveItemType) as Component;
            GazeDwellButton dwell = buttonObject.AddComponent<GazeDwellButton>();
            dwell.Configure(interactiveItem, selectionRadial, id, action);

            ButtonView view = new ButtonView
            {
                Id = id,
                Label = label,
                Surface = surface,
                Material = surface.sharedMaterial
            };
            buttons.Add(view);
        }

        private MeshRenderer CreateSurface(
            string objectName,
            Transform parent,
            Vector3 localPosition,
            Vector2 size,
            Color color,
            float zOffset)
        {
            GameObject surfaceObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            surfaceObject.name = objectName;
            surfaceObject.transform.SetParent(parent, false);
            surfaceObject.transform.localPosition = localPosition + new Vector3(0.0f, 0.0f, zOffset);
            surfaceObject.transform.localScale = new Vector3(size.x, size.y, 1.0f);
            Collider generatedCollider = surfaceObject.GetComponent<Collider>();
            if (generatedCollider != null)
            {
                Destroy(generatedCollider);
            }

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
            MeshRenderer renderer = surfaceObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = 30;
            return renderer;
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
                passthroughText = "PASSTHROUGH\nWAITING FOR VR CAMERA";
                passthroughColor = UnavailableColor;
            }
            else if (!passthrough.IsPassthroughRequested)
            {
                passthroughText = "PASSTHROUGH\nOFF - GAZE TO ENABLE";
                passthroughColor = ReadyColor;
            }
            else if (passthrough.HasLiveFrames)
            {
                passthroughText = "PASSTHROUGH\nLIVE - GAZE TO DISABLE";
                passthroughColor = ActiveColor;
            }
            else
            {
                passthroughText = "PASSTHROUGH\n" + passthrough.State.ToString().ToUpperInvariant();
                passthroughColor = WarningColor;
            }
            UpdateButton("passthrough", passthroughText, passthroughColor);

            UpdateButton(
                "video",
                VrViewRecordingHotkey.IsRecording
                    ? "VR VIEW VIDEO\nRECORDING - GAZE TO STOP"
                    : "VR VIEW VIDEO\nIDLE - GAZE TO RECORD",
                VrViewRecordingHotkey.IsRecording ? RecordingColor : ReadyColor);

            CaptureSessionManager capture = CaptureSessionManager.Instance;
            bool trialActive = capture != null && capture.IsRecording;
            UpdateButton(
                "trial",
                trialActive
                    ? "TRIAL CAPTURE\nRECORDING - GAZE TO FINALIZE"
                    : "TRIAL CAPTURE\nREADY - GAZE TO START",
                trialActive ? RecordingColor : ReadyColor);
            UpdateButton(
                "marker",
                trialActive
                    ? "EVENT MARKER\nREADY - GAZE TO ADD"
                    : "EVENT MARKER\nSTART A TRIAL FIRST",
                trialActive ? ActiveColor : UnavailableColor);
            UpdateButton("reset", "RESET SCENE\nGAZE TO RESTORE OBJECTS", WarningColor);
            UpdateButton("recalibrate", "RECALIBRATE\nGAZE TO START V/B/P POSES", ReadyColor);

            if (noticeText != null)
            {
                noticeText.text = notice;
            }
        }

        private void UpdateButton(string id, string content, Color color)
        {
            for (int index = 0; index < buttons.Count; index++)
            {
                ButtonView button = buttons[index];
                if (!string.Equals(button.Id, id, StringComparison.Ordinal))
                {
                    continue;
                }

                button.Label.text = content;
                button.Material.color = color;
                return;
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

            panelRoot = null;
            mainStateRoot = null;
            menuStateMachine = null;
            calibrationStateMachine = null;
            selectionRadial = null;
            interactiveItemType = null;
            originalMainChildren.Clear();
            buttons.Clear();
            ownedMaterials.Clear();
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
