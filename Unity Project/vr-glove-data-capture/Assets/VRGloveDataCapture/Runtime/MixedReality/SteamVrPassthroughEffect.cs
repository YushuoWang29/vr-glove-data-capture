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

        private void Awake()
        {
            targetCamera = GetComponent<Camera>();
            Shader shader = Shader.Find("Hidden/VRGloveDataCapture/PassthroughComposite");
            if (shader == null)
            {
                state = PassthroughState.Error;
                status = "Passthrough composite shader was not found.";
                Debug.LogError("[VRGloveDataCapture] " + status, this);
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

            if (!acquired && !TryAcquireStream())
            {
                return;
            }

            Texture2D texture = videoSource.texture;
            if (texture == null)
            {
                state = PassthroughState.WaitingForFrame;
                status = "Camera service is active, waiting for the first frame.";
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
                state = PassthroughState.Streaming;
                status = "VIVE tracked-camera frames are being composited.";
            }
            else if (Time.unscaledTime - lastFrameTime > stalledFrameSeconds)
            {
                state = PassthroughState.FrameStalled;
                status = "The tracked-camera stream is available but frames have stalled.";
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
            if (!requested)
            {
                ReleaseStream();
                RestoreCamera();
                state = PassthroughState.Disabled;
                status = "Passthrough is disabled.";
                return;
            }

            state = PassthroughState.WaitingForSteamVr;
            status = "Waiting for SteamVR and the OpenVR tracked-camera service.";
        }

        private bool TryAcquireStream()
        {
            if (SteamVR.instance == null || OpenVR.TrackedCamera == null)
            {
                state = PassthroughState.WaitingForSteamVr;
                status = "SteamVR/OpenVR tracked-camera service is not ready.";
                return false;
            }

            videoSource = SteamVR_TrackedCamera.Source(undistorted);
            if (!videoSource.hasCamera)
            {
                state = PassthroughState.CameraUnavailable;
                status = "The HMD camera is unavailable. Enable Camera in SteamVR and restart SteamVR.";
                return false;
            }

            videoSource.Acquire();
            acquired = true;
            lastFrameId = 0;
            lastFrameTime = Time.unscaledTime;
            state = PassthroughState.WaitingForFrame;
            status = "Tracked-camera service acquired; waiting for video.";
            return true;
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
