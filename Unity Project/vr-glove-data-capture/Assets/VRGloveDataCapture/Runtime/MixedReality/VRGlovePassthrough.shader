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
                output.uv = input.uv;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float eyeIndex = (float)unity_StereoEyeIndex;
                eyeIndex = lerp(eyeIndex, 1.0 - eyeIndex, step(0.5, _SwapStereoEyes));

                float2 layoutUv = input.uv;
                if (_CameraFrameLayout > 0.5 && _CameraFrameLayout < 1.5)
                {
                    // OpenVR VerticalLayout is top/bottom = left/right. The base
                    // UV transform performs the API-to-Unity vertical flip after
                    // this split, preserving that documented eye order.
                    layoutUv.y = layoutUv.y * 0.5 + eyeIndex * 0.5;
                }
                else if (_CameraFrameLayout >= 1.5)
                {
                    // OpenVR HorizontalLayout is left/right.
                    layoutUv.x = layoutUv.x * 0.5 + eyeIndex * 0.5;
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
