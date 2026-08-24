// Opaque bass that dither away with water column and view distance.
// Uses geometric depth (world Y vs surface), not gameplay depth.
Shader "Wilo/Fish"
{
    Properties
    {
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        _ForceVisible("Force Visible", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _GPU_SKINNING
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                half _ForceVisible;
            CBUFFER_END

            float _WiloWaterY;
            float _WiloFishVisibility;
            float _WiloFishViewDistance;
            float _WiloFishFadePower;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fogCoord : TEXCOORD3;
            };

            float Dither(float2 pixel)
            {
                return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                if (_ForceVisible < 0.5 && _WiloFishVisibility > 0.05)
                {
                    float waterAbove = max(0.0, _WiloWaterY - IN.positionWS.y);
                    float depthFade = saturate(waterAbove / _WiloFishVisibility);
                    float dist = length(IN.positionWS.xz - _WorldSpaceCameraPos.xz);
                    float view = max(_WiloFishViewDistance, 1.0);
                    float distFade = saturate((dist - view * 0.45) / (view * 0.55));
                    float hide = pow(max(depthFade, distFade), max(_WiloFishFadePower, 0.4));
                    hide = saturate(hide * 0.92);
                    clip(Dither(IN.positionCS.xy) - hide);
                }

                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                float3 n = normalize(IN.normalWS);
                Light mainLight = GetMainLight();
                half NdotL = saturate(dot(n, mainLight.direction));
                half3 ambient = SampleSH(n);
                half3 lit = albedo.rgb * (ambient + mainLight.color * (0.35 + 0.65 * NdotL));
                lit = MixFog(lit, IN.fogCoord);
                return half4(lit, 1);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
