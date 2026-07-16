Shader "Hidden/VRGloveDataCapture/PassthroughComposite"
{
    Properties
    {
        _MainTex ("Virtual Scene", 2D) = "black" {}
        _CameraTex ("Tracked Camera", 2D) = "black" {}
        _Opacity ("Passthrough Opacity", Range(0, 1)) = 1
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            sampler2D _CameraTex;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4 _CameraUvTransform;
            float _Opacity;

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 virtualScene = tex2D(_MainTex, input.uv);
                float2 cameraUv = input.uv * _CameraUvTransform.xy + _CameraUvTransform.zw;
                fixed4 realWorld = tex2D(_CameraTex, cameraUv);

                float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, input.uv);
                float linearDepth = Linear01Depth(rawDepth);
                float geometryCoverage = 1.0 - step(0.9999, linearDepth);
                float sceneCoverage = max(geometryCoverage, saturate(virtualScene.a));

                fixed3 mixedReality = lerp(realWorld.rgb, virtualScene.rgb, sceneCoverage);
                fixed3 result = lerp(virtualScene.rgb, mixedReality, saturate(_Opacity));
                return fixed4(result, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
