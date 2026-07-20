Shader "Hidden/VRGloveDataCapture/PassthroughBackground"
{
    Properties
    {
        _CameraTex ("Tracked Camera", 2D) = "black" {}
        _Opacity ("Passthrough Opacity", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _CameraTex;
            float4 _CameraUvTransform;
            float _CameraFrameLayout;
            float _SwapStereoEyes;
            float _RotateEachEye180;
            float _Opacity;

            v2f vert(appdata input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // The command buffer supplies clip-space vertices. Drawing this
                // pass at the camera's pre-opaque event makes the tracked camera
                // the background, so normal Unity depth rendering remains intact.
                output.vertex = float4(input.vertex.xy, 1.0, 1.0);

                // A D3D XR eye target can use a vertically flipped projection.
                // Since this pass writes clip space directly, Unity's projection
                // matrix cannot perform that correction for us. _ProjectionParams.x
                // is Unity's authoritative +1/-1 signal for the current camera.
                // Keep this view-target correction separate from Valve's later
                // frameBounds transform, which describes the external texture.
                output.uv = float2(
                    input.uv.x,
                    0.5 + (input.uv.y - 0.5) * _ProjectionParams.x);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float cameraEye = (float)unity_StereoEyeIndex;
                cameraEye = lerp(
                    cameraEye,
                    1.0 - cameraEye,
                    step(0.5, _SwapStereoEyes));

                // Orientation is deliberately corrected inside each eye image.
                // Rotating the combined stereo texture would exchange cameras.
                float2 layoutUv = lerp(
                    input.uv,
                    1.0 - input.uv,
                    step(0.5, _RotateEachEye180));
                if (_CameraFrameLayout > 0.5 && _CameraFrameLayout < 1.5)
                {
                    // OpenVR VerticalLayout is top/bottom = left/right. Valve's
                    // Unity wrapper normally supplies a negative V scale, so the
                    // packed region must be reversed to preserve left/right.
                    float regionEye = lerp(
                        cameraEye,
                        1.0 - cameraEye,
                        step(_CameraUvTransform.y, -0.000001));
                    layoutUv.y = layoutUv.y * 0.5 + regionEye * 0.5;
                }
                else if (_CameraFrameLayout >= 1.5)
                {
                    // OpenVR HorizontalLayout is left/right.
                    float regionEye = lerp(
                        cameraEye,
                        1.0 - cameraEye,
                        step(_CameraUvTransform.x, -0.000001));
                    layoutUv.x = layoutUv.x * 0.5 + regionEye * 0.5;
                }

                float2 cameraUv =
                    layoutUv * _CameraUvTransform.xy + _CameraUvTransform.zw;
                fixed4 realWorld = tex2D(_CameraTex, cameraUv);
                return fixed4(realWorld.rgb * saturate(_Opacity), 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
