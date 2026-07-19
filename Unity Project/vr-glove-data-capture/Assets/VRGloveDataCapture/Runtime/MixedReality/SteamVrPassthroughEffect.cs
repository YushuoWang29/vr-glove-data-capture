using UnityEngine;
using UnityEngine.Rendering;
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

        private enum TrackedCameraLayout
        {
            Mono = 0,
            VerticalStereo = 1,
            HorizontalStereo = 2
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
        private KeyCode keyboardToggle = KeyCode.P;

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
        private TrackedCameraLayout detectedLayout = TrackedCameraLayout.Mono;
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
                hideFlags = HideFlags.HideAndDontSave
            };
            fullscreenMesh = BuildFullscreenMesh();
            backgroundCommands = new CommandBuffer
            {
                name = CommandBufferName
            };
            backgroundCommands.DrawMesh(
                fullscreenMesh,
                Matrix4x4.identity,
                backgroundMaterial,
                0,
                0);
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
            UpdateTextureBounds();
            ApplyCameraOverride();
            InstallBackgroundCommands();

            uint frameId = videoSource.frameId;
            if (frameId != lastFrameId)
            {
                lastFrameId = frameId;
                lastFrameTime = Time.unscaledTime;
                SetState(PassthroughState.Streaming,
                    "LIVE: " + detectedCameraCount + " camera(s), " + detectedLayout +
                    ", texture " + texture.width + "x" + texture.height + ".");
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
            detectedLayout = TrackedCameraLayout.Mono;
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
                detectedLayout = TrackedCameraLayout.VerticalStereo;
            }
            else if (stereo && (layoutFlags & OpenVrHorizontalFlag) != 0)
            {
                detectedLayout = TrackedCameraLayout.HorizontalStereo;
            }
            else if (stereo && texture != null)
            {
                detectedLayout = texture.height > texture.width
                    ? TrackedCameraLayout.VerticalStereo
                    : TrackedCameraLayout.HorizontalStereo;
            }
            else if (texture != null && texture.height > texture.width * 1.2f)
            {
                // Some older OpenVR drivers omit the layout property but still
                // expose the documented top/bottom stereo texture.
                detectedLayout = TrackedCameraLayout.VerticalStereo;
                stereo = true;
            }
            else
            {
                detectedLayout = TrackedCameraLayout.Mono;
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
