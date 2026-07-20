using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;
using Valve.VR;

namespace VRGloveDataCapture.MixedReality
{
    /// <summary>
    /// Draws the OpenVR HMD tracked-camera stream as the stereo camera background.
    /// This is experimental video see-through, not optical see-through AR.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public sealed class SteamVrPassthroughEffect : MonoBehaviour
    {
        public enum StereoFrameLayout
        {
            Mono = 0,
            VerticalStereo = 1,
            HorizontalStereo = 2
        }

        public enum PerEyeOrientation
        {
            Normal = 0,
            Rotate180 = 1
        }

        public enum PassthroughState
        {
            Disabled,
            WaitingForSteamVr,
            CameraUnavailable,
            WaitingForFrame,
            Streaming,
            FrameStalled,
            Error
        }

        [SerializeField]
        private bool startEnabled;

        [SerializeField]
        private bool undistorted = true;

        [SerializeField]
        private bool cropInvalidEdges = true;

        [SerializeField]
        [Range(0.0f, 1.0f)]
        private float opacity = 1.0f;

        [SerializeField]
        private bool swapStereoEyes;

        [SerializeField]
        private PerEyeOrientation perEyeOrientation = PerEyeOrientation.Normal;

        [SerializeField]
        private KeyCode keyboardToggle = KeyCode.P;

        [SerializeField]
        private KeyCode orientationToggle = KeyCode.O;

        [SerializeField]
        [Min(0.1f)]
        private float stalledFrameSeconds = 1.0f;

        private const int OpenVrStereoFlag = 0x0002;
        private const int OpenVrVerticalFlag = 0x0010;
        private const int OpenVrHorizontalFlag = 0x0020;
        private const string CommandBufferName = "VR Glove Stereo Passthrough Background";

        private static readonly int CameraTextureId = Shader.PropertyToID("_CameraTex");
        private static readonly int CameraUvTransformId = Shader.PropertyToID("_CameraUvTransform");
        private static readonly int CameraFrameLayoutId = Shader.PropertyToID("_CameraFrameLayout");
        private static readonly int SwapStereoEyesId = Shader.PropertyToID("_SwapStereoEyes");
        private static readonly int RotateEachEye180Id = Shader.PropertyToID("_RotateEachEye180");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");

        private Camera targetCamera;
        private Material backgroundMaterial;
        private Mesh fullscreenMesh;
        private CommandBuffer backgroundCommands;
        private SteamVR_TrackedCamera.VideoStreamTexture videoSource;
        private bool requested;
        private bool acquired;
        private bool commandsInstalled;
        private bool cameraOverrideApplied;
        private bool frameLayoutDetected;
        private CameraClearFlags originalClearFlags;
        private Color originalBackgroundColor;
        private CameraEvent installedCameraEvent = CameraEvent.BeforeForwardOpaque;
        private uint lastFrameId;
        private float lastFrameTime;
        private float nextAcquireAttemptTime;
        private int detectedCameraCount = 1;
        private StereoFrameLayout detectedLayout = StereoFrameLayout.Mono;
        private Vector4 currentUvTransform = new Vector4(1.0f, -1.0f, 0.0f, 1.0f);
        private bool singlePassInstancedBackground;
        private bool hasConfiguredStereoMode;
        private XRSettings.StereoRenderingMode configuredStereoMode;
        private string stereoDrawMode = "Pending XR initialization";
        private PassthroughState state = PassthroughState.Disabled;
        private string status = "Passthrough is disabled.";

        public bool IsPassthroughRequested
        {
            get { return requested; }
        }

        public PassthroughState State
        {
            get { return state; }
        }

        public string Status
        {
            get { return status; }
        }

        public bool HasLiveFrames
        {
            get { return state == PassthroughState.Streaming; }
        }

        public uint LastFrameId
        {
            get { return lastFrameId; }
        }

        public int DetectedCameraCount
        {
            get { return detectedCameraCount; }
        }

        public string DetectedFrameLayout
        {
            get { return detectedLayout.ToString(); }
        }

        public string CurrentPerEyeOrientation
        {
            get { return perEyeOrientation.ToString(); }
        }

        public string CurrentStereoDrawMode
        {
            get { return stereoDrawMode; }
        }

        private void Awake()
        {
            targetCamera = GetComponent<Camera>();
            Shader shader = Shader.Find("Hidden/VRGloveDataCapture/PassthroughBackground");
            if (shader == null)
            {
                SetState(PassthroughState.Error,
                    "Passthrough background shader was not found.");
                return;
            }

            backgroundMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true
            };
            fullscreenMesh = BuildFullscreenMesh();
            backgroundCommands = new CommandBuffer
            {
                name = CommandBufferName
            };
        }

        private void OnEnable()
        {
            SetPassthroughEnabled(startEnabled);
        }

        private void OnDisable()
        {
            ReleaseStream();
            RestoreCamera();
        }

        private void OnDestroy()
        {
            RemoveBackgroundCommands();
            if (backgroundCommands != null)
            {
                backgroundCommands.Release();
                backgroundCommands = null;
            }
            if (backgroundMaterial != null)
            {
                Destroy(backgroundMaterial);
            }
            if (fullscreenMesh != null)
            {
                Destroy(fullscreenMesh);
            }
        }

        private void Update()
        {
            if (keyboardToggle != KeyCode.None && Input.GetKeyDown(keyboardToggle))
            {
                TogglePassthrough();
            }

            if (orientationToggle != KeyCode.None && Input.GetKeyDown(orientationToggle))
            {
                TogglePerEyeOrientation();
            }

            if (!requested || backgroundMaterial == null)
            {
                return;
            }

            if (!acquired && Time.unscaledTime < nextAcquireAttemptTime)
            {
                return;
            }

            if (!acquired && !TryAcquireStream())
            {
                nextAcquireAttemptTime = Time.unscaledTime + 2.0f;
                return;
            }

            Texture2D texture = videoSource.texture;
            if (texture == null)
            {
                SetState(PassthroughState.WaitingForFrame,
                    "Camera service is active, waiting for the first frame.");
                return;
            }

            if (!frameLayoutDetected)
            {
                DetectFrameLayout(texture);
            }
            backgroundMaterial.SetTexture(CameraTextureId, texture);
            backgroundMaterial.SetFloat(OpacityId, opacity);
            backgroundMaterial.SetFloat(CameraFrameLayoutId, (float)detectedLayout);
            backgroundMaterial.SetFloat(SwapStereoEyesId, swapStereoEyes ? 1.0f : 0.0f);
            backgroundMaterial.SetFloat(
                RotateEachEye180Id,
                perEyeOrientation == PerEyeOrientation.Rotate180 ? 1.0f : 0.0f);
            UpdateTextureBounds();
            RefreshBackgroundCommandsForCurrentStereoMode();
            ApplyCameraOverride();
            InstallBackgroundCommands();

            uint frameId = videoSource.frameId;
            if (frameId != lastFrameId)
            {
                lastFrameId = frameId;
                lastFrameTime = Time.unscaledTime;
                SetState(PassthroughState.Streaming,
                    "LIVE: " + detectedCameraCount + " camera(s), " + detectedLayout +
                    ", texture " + texture.width + "x" + texture.height +
                    ", per-eye " + perEyeOrientation +
                    ", UV " + FormatUvTransform(currentUvTransform) +
                    ", XR draw " + stereoDrawMode +
                    ", " + SystemInfo.graphicsDeviceType + ".");
            }
            else if (Time.unscaledTime - lastFrameTime > stalledFrameSeconds)
            {
                SetState(PassthroughState.FrameStalled,
                    "The tracked-camera stream is available but frames have stalled.");
            }
        }

        public void TogglePassthrough()
        {
            SetPassthroughEnabled(!requested);
        }

        public void TogglePerEyeOrientation()
        {
            perEyeOrientation = perEyeOrientation == PerEyeOrientation.Normal
                ? PerEyeOrientation.Rotate180
                : PerEyeOrientation.Normal;
            Debug.Log(
                "[VRGloveDataCapture] Passthrough per-eye orientation: " +
                perEyeOrientation + ". This rotates each eye inside its own camera region " +
                "without exchanging the stereo cameras.",
                this);
        }

        /// <summary>
        /// Enables or disables stereo video see-through. This method can be wired
        /// to a Unity UI event or a SteamVR input action.
        /// </summary>
        public void SetPassthroughEnabled(bool value)
        {
            requested = value;
            nextAcquireAttemptTime = 0.0f;
            if (!requested)
            {
                ReleaseStream();
                RestoreCamera();
                SetState(PassthroughState.Disabled, "Passthrough is disabled.");
                return;
            }

            SetState(PassthroughState.WaitingForSteamVr,
                "Waiting for SteamVR and the OpenVR tracked-camera service.");
        }

        private bool TryAcquireStream()
        {
            if (SteamVR.initializedState != SteamVR.InitializedStates.InitializeSuccess)
            {
                SetState(PassthroughState.WaitingForSteamVr,
                    "SteamVR is not initialized; camera acquisition is paused without forcing retries.");
                return false;
            }

            if (SteamVR.instance == null || OpenVR.TrackedCamera == null)
            {
                SetState(PassthroughState.WaitingForSteamVr,
                    "SteamVR/OpenVR tracked-camera service is not ready.");
                return false;
            }

            videoSource = SteamVR_TrackedCamera.Source(undistorted);
            if (!videoSource.hasCamera)
            {
                SetState(PassthroughState.CameraUnavailable,
                    "The HMD camera is unavailable. Enable Camera in SteamVR and restart SteamVR.");
                return false;
            }

            videoSource.Acquire();
            acquired = true;
            lastFrameId = 0;
            lastFrameTime = Time.unscaledTime;
            detectedCameraCount = 1;
            detectedLayout = StereoFrameLayout.Mono;
            frameLayoutDetected = false;
            SetState(PassthroughState.WaitingForFrame,
                "Tracked-camera service acquired; waiting for stereo video.");
            return true;
        }

        private void DetectFrameLayout(Texture texture)
        {
            int cameraCount = 0;
            int layoutFlags = 0;
            CVRSystem system = OpenVR.System;
            if (system != null)
            {
                ETrackedPropertyError cameraCountError = ETrackedPropertyError.TrackedProp_Success;
                cameraCount = system.GetInt32TrackedDeviceProperty(
                    OpenVR.k_unTrackedDeviceIndex_Hmd,
                    ETrackedDeviceProperty.Prop_NumCameras_Int32,
                    ref cameraCountError);
                if (cameraCountError != ETrackedPropertyError.TrackedProp_Success)
                {
                    cameraCount = 0;
                }

                ETrackedPropertyError layoutError = ETrackedPropertyError.TrackedProp_Success;
                layoutFlags = system.GetInt32TrackedDeviceProperty(
                    OpenVR.k_unTrackedDeviceIndex_Hmd,
                    ETrackedDeviceProperty.Prop_CameraFrameLayout_Int32,
                    ref layoutError);
                if (layoutError != ETrackedPropertyError.TrackedProp_Success)
                {
                    layoutFlags = 0;
                }
            }

            bool stereo = cameraCount > 1 || (layoutFlags & OpenVrStereoFlag) != 0;
            if (stereo && (layoutFlags & OpenVrVerticalFlag) != 0)
            {
                detectedLayout = StereoFrameLayout.VerticalStereo;
            }
            else if (stereo && (layoutFlags & OpenVrHorizontalFlag) != 0)
            {
                detectedLayout = StereoFrameLayout.HorizontalStereo;
            }
            else if (stereo && texture != null)
            {
                detectedLayout = texture.height > texture.width
                    ? StereoFrameLayout.VerticalStereo
                    : StereoFrameLayout.HorizontalStereo;
            }
            else if (texture != null && texture.height > texture.width * 1.2f)
            {
                // Some older OpenVR drivers omit the layout property but still
                // expose the documented top/bottom stereo texture.
                detectedLayout = StereoFrameLayout.VerticalStereo;
                stereo = true;
            }
            else
            {
                detectedLayout = StereoFrameLayout.Mono;
            }

            detectedCameraCount = cameraCount > 0 ? cameraCount : (stereo ? 2 : 1);
            frameLayoutDetected = true;
        }

        private void SetState(PassthroughState nextState, string nextStatus)
        {
            if (state == nextState && status == nextStatus)
            {
                return;
            }

            state = nextState;
            status = nextStatus;
            string message = "[VRGloveDataCapture] Passthrough " + state + ": " + status;

            if (state == PassthroughState.Error)
            {
                Debug.LogError(message, this);
            }
            else if (state == PassthroughState.CameraUnavailable ||
                     state == PassthroughState.FrameStalled)
            {
                Debug.LogWarning(message, this);
            }
            else
            {
                Debug.Log(message, this);
            }
        }

        private void ReleaseStream()
        {
            RemoveBackgroundCommands();
            if (acquired && videoSource != null)
            {
                videoSource.Release();
            }

            acquired = false;
            videoSource = null;
            frameLayoutDetected = false;
            if (backgroundMaterial != null)
            {
                backgroundMaterial.SetTexture(CameraTextureId, null);
            }
        }

        private void UpdateTextureBounds()
        {
            Vector4 uvTransform;
            if (cropInvalidEdges)
            {
                VRTextureBounds_t bounds = videoSource.frameBounds;
                float width = bounds.uMax - bounds.uMin;
                float height = bounds.vMax - bounds.vMin;
                uvTransform = Mathf.Abs(width) > 0.0001f && Mathf.Abs(height) > 0.0001f
                    ? new Vector4(width, height, bounds.uMin, bounds.vMin)
                    : new Vector4(1.0f, -1.0f, 0.0f, 1.0f);
            }
            else
            {
                uvTransform = new Vector4(1.0f, -1.0f, 0.0f, 1.0f);
            }

            backgroundMaterial.SetVector(CameraUvTransformId, uvTransform);
            currentUvTransform = uvTransform;
        }

        /// <summary>
        /// CPU reference for the shader's stereo-region mapping. OpenVR vertical
        /// frames are top/bottom = left/right; a negative frame-bounds scale
        /// reverses the packed-region index after Unity's texture flip.
        /// </summary>
        public static Vector2 CalculateStereoLayoutUv(
            Vector2 localUv,
            int renderEyeIndex,
            StereoFrameLayout layout,
            bool swapEyes,
            PerEyeOrientation orientation,
            Vector2 baseUvScale)
        {
            float cameraEye = renderEyeIndex > 0 ? 1.0f : 0.0f;
            if (swapEyes)
            {
                cameraEye = 1.0f - cameraEye;
            }

            Vector2 orientedUv = orientation == PerEyeOrientation.Rotate180
                ? Vector2.one - localUv
                : localUv;
            if (layout == StereoFrameLayout.VerticalStereo)
            {
                float region = baseUvScale.y < 0.0f ? 1.0f - cameraEye : cameraEye;
                orientedUv.y = orientedUv.y * 0.5f + region * 0.5f;
            }
            else if (layout == StereoFrameLayout.HorizontalStereo)
            {
                float region = baseUvScale.x < 0.0f ? 1.0f - cameraEye : cameraEye;
                orientedUv.x = orientedUv.x * 0.5f + region * 0.5f;
            }

            return orientedUv;
        }

        /// <summary>
        /// CPU reference for the complete shader UV path. The XR render target
        /// projection correction and Valve tracked-camera frame bounds are two
        /// independent transforms and must be applied in this order. The
        /// projection sign mirrors Unity's _ProjectionParams.x contract: +1 for
        /// a normal projection and -1 for a vertically flipped projection.
        /// </summary>
        public static Vector2 CalculateCameraSampleUv(
            Vector2 quadUv,
            int renderEyeIndex,
            StereoFrameLayout layout,
            bool swapEyes,
            PerEyeOrientation orientation,
            Vector4 frameBoundsTransform,
            float projectionFlipSign)
        {
            // This is the screen/target correction which Unity would normally
            // contribute through its projection matrix. The passthrough shader
            // emits clip-space vertices directly, so it must be explicit here.
            Vector2 viewUv = new Vector2(
                quadUv.x,
                0.5f + (quadUv.y - 0.5f) * projectionFlipSign);

            // Eye-region selection is performed in logical view UVs. Valve's
            // bounds transform remains last because it describes the external
            // tracked-camera texture, including its independent vertical flip.
            Vector2 layoutUv = CalculateStereoLayoutUv(
                viewUv,
                renderEyeIndex,
                layout,
                swapEyes,
                orientation,
                new Vector2(frameBoundsTransform.x, frameBoundsTransform.y));
            return new Vector2(
                layoutUv.x * frameBoundsTransform.x + frameBoundsTransform.z,
                layoutUv.y * frameBoundsTransform.y + frameBoundsTransform.w);
        }

        private static string FormatUvTransform(Vector4 transform)
        {
            return "(" + transform.x.ToString("F4") + "," +
                   transform.y.ToString("F4") + "," +
                   transform.z.ToString("F4") + "," +
                   transform.w.ToString("F4") + ")";
        }

        /// <summary>
        /// Unity 2019's CommandBuffer.DrawMesh defaults to one instance and one
        /// texture-array slice. Single Pass Instanced therefore needs both the
        /// complete CameraTarget array and two draw instances explicitly.
        /// </summary>
        private void RefreshBackgroundCommandsForCurrentStereoMode()
        {
            XRSettings.StereoRenderingMode currentMode = XRSettings.stereoRenderingMode;
            if (hasConfiguredStereoMode && configuredStereoMode == currentMode)
            {
                return;
            }

            // XRSettings reports its final OpenVR mode only after the loader has
            // initialized. Awake is too early: caching MultiPass there while the
            // project later starts Single Pass Instanced leaves the right texture
            // array slice undrawn. Rebuild immediately before first install and
            // again if the runtime mode changes.
            bool reinstall = commandsInstalled;
            if (reinstall)
            {
                RemoveBackgroundCommands();
            }

            ConfigureBackgroundCommands(currentMode);
            if (reinstall)
            {
                InstallBackgroundCommands();
            }
        }

        private void ConfigureBackgroundCommands(XRSettings.StereoRenderingMode mode)
        {
            if (backgroundCommands == null || fullscreenMesh == null || backgroundMaterial == null)
            {
                return;
            }

            backgroundCommands.Clear();
            configuredStereoMode = mode;
            hasConfiguredStereoMode = true;
            singlePassInstancedBackground = RequiresInstancedStereoDraw(mode);
            stereoDrawMode = mode.ToString() +
                (singlePassInstancedBackground ? " x2/all-slices" : " x1");

            if (singlePassInstancedBackground)
            {
                backgroundCommands.SetRenderTarget(
                    BuiltinRenderTextureType.CameraTarget,
                    0,
                    CubemapFace.Unknown,
                    -1);
                backgroundCommands.SetInstanceMultiplier(2);
            }

            backgroundCommands.DrawMesh(
                fullscreenMesh,
                Matrix4x4.identity,
                backgroundMaterial,
                0,
                0);

            if (singlePassInstancedBackground)
            {
                // Do not leak the stereo multiplier into commands appended later.
                backgroundCommands.SetInstanceMultiplier(1);
            }
        }

        public static bool RequiresInstancedStereoDraw(XRSettings.StereoRenderingMode mode)
        {
            return mode == XRSettings.StereoRenderingMode.SinglePassInstanced;
        }

        private void ApplyCameraOverride()
        {
            if (cameraOverrideApplied || targetCamera == null)
            {
                return;
            }

            originalClearFlags = targetCamera.clearFlags;
            originalBackgroundColor = targetCamera.backgroundColor;
            targetCamera.clearFlags = CameraClearFlags.SolidColor;
            targetCamera.backgroundColor = Color.black;
            cameraOverrideApplied = true;
        }

        private void RestoreCamera()
        {
            if (!cameraOverrideApplied || targetCamera == null)
            {
                return;
            }

            targetCamera.clearFlags = originalClearFlags;
            targetCamera.backgroundColor = originalBackgroundColor;
            cameraOverrideApplied = false;
        }

        private void InstallBackgroundCommands()
        {
            if (commandsInstalled || targetCamera == null || backgroundCommands == null)
            {
                return;
            }

            RenderingPath path = targetCamera.actualRenderingPath;
            installedCameraEvent = path == RenderingPath.DeferredLighting ||
                                   path == RenderingPath.DeferredShading
                ? CameraEvent.BeforeGBuffer
                : CameraEvent.BeforeForwardOpaque;
            targetCamera.AddCommandBuffer(installedCameraEvent, backgroundCommands);
            commandsInstalled = true;
        }

        private void RemoveBackgroundCommands()
        {
            if (!commandsInstalled || targetCamera == null || backgroundCommands == null)
            {
                return;
            }

            targetCamera.RemoveCommandBuffer(installedCameraEvent, backgroundCommands);
            commandsInstalled = false;
        }

        private static Mesh BuildFullscreenMesh()
        {
            Mesh mesh = new Mesh
            {
                name = "VRGlovePassthroughFullscreenQuad",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = new[]
                {
                    new Vector3(-1.0f, -1.0f, 0.0f),
                    new Vector3(1.0f, -1.0f, 0.0f),
                    new Vector3(1.0f, 1.0f, 0.0f),
                    new Vector3(-1.0f, 1.0f, 0.0f)
                },
                uv = new[]
                {
                    new Vector2(0.0f, 0.0f),
                    new Vector2(1.0f, 0.0f),
                    new Vector2(1.0f, 1.0f),
                    new Vector2(0.0f, 1.0f)
                },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}
