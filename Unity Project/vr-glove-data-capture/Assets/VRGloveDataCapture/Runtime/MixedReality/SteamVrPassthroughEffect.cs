using UnityEngine;
using Valve.VR;

namespace VRGloveDataCapture.MixedReality
{
    /// <summary>
    /// Composites the OpenVR HMD tracked-camera stream behind Unity geometry.
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
        private KeyCode keyboardToggle = KeyCode.P;

        [SerializeField]
        [Min(0.1f)]
        private float stalledFrameSeconds = 1.0f;

        private static readonly int CameraTextureId = Shader.PropertyToID("_CameraTex");
        private static readonly int CameraUvTransformId = Shader.PropertyToID("_CameraUvTransform");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");

        private Camera targetCamera;
        private Material compositeMaterial;
        private SteamVR_TrackedCamera.VideoStreamTexture videoSource;
        private bool requested;
        private bool acquired;
        private bool cameraOverrideApplied;
        private CameraClearFlags originalClearFlags;
        private Color originalBackgroundColor;
        private DepthTextureMode originalDepthTextureMode;
        private uint lastFrameId;
        private float lastFrameTime;
        private float nextAcquireAttemptTime;
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

        private void Awake()
        {
            targetCamera = GetComponent<Camera>();
            Shader shader = Shader.Find("Hidden/VRGloveDataCapture/PassthroughComposite");
            if (shader == null)
            {
                SetState(PassthroughState.Error,
                    "Passthrough composite shader was not found.");
                return;
            }

            compositeMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
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
            if (compositeMaterial != null)
            {
                Destroy(compositeMaterial);
            }
        }

        private void Update()
        {
            if (keyboardToggle != KeyCode.None && Input.GetKeyDown(keyboardToggle))
            {
                TogglePassthrough();
            }

            if (!requested || compositeMaterial == null)
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

            compositeMaterial.SetTexture(CameraTextureId, texture);
            compositeMaterial.SetFloat(OpacityId, opacity);
            UpdateTextureBounds();
            ApplyCameraOverride();

            uint frameId = videoSource.frameId;
            if (frameId != lastFrameId)
            {
                lastFrameId = frameId;
                lastFrameTime = Time.unscaledTime;
                SetState(PassthroughState.Streaming,
                    "LIVE: VIVE tracked-camera frames are being composited (" +
                    texture.width + "x" + texture.height + ").");
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
        /// Enables or disables video see-through. This method can be wired to
        /// a Unity UI event or a SteamVR input action.
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
            SetState(PassthroughState.WaitingForFrame,
                "Tracked-camera service acquired; waiting for video.");
            return true;
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
            if (acquired && videoSource != null)
            {
                videoSource.Release();
            }

            acquired = false;
            videoSource = null;
            if (compositeMaterial != null)
            {
                compositeMaterial.SetTexture(CameraTextureId, null);
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

            compositeMaterial.SetVector(CameraUvTransformId, uvTransform);
        }

        private void ApplyCameraOverride()
        {
            if (cameraOverrideApplied || targetCamera == null)
            {
                return;
            }

            originalClearFlags = targetCamera.clearFlags;
            originalBackgroundColor = targetCamera.backgroundColor;
            originalDepthTextureMode = targetCamera.depthTextureMode;
            targetCamera.clearFlags = CameraClearFlags.SolidColor;
            Color transparentBackground = originalBackgroundColor;
            transparentBackground.a = 0.0f;
            targetCamera.backgroundColor = transparentBackground;
            targetCamera.depthTextureMode |= DepthTextureMode.Depth;
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
            targetCamera.depthTextureMode = originalDepthTextureMode;
            cameraOverrideApplied = false;
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (requested && acquired && compositeMaterial != null &&
                compositeMaterial.GetTexture(CameraTextureId) != null)
            {
                Graphics.Blit(source, destination, compositeMaterial);
            }
            else
            {
                Graphics.Blit(source, destination);
            }
        }
    }
}
