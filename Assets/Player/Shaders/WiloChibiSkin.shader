// Remaps the peach/tan atlas pixels to a chosen skin color so a single
// chibi texture can change complexion without tinting eyes or clothes.
Shader "Wilo/Chibi Skin"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        _SourceSkin("Source Skin", Color) = (0.93, 0.72, 0.58, 1)
        _SkinColor("Skin", Color) = (0.93, 0.72, 0.58, 1)
        _HueRange("Hue Range", Range(0.02, 0.35)) = 0.16
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _SourceSkin;
                half4 _SkinColor;
                half _HueRange;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float fogCoord : TEXCOORD3;
            };

            half3 RgbToHsv(half3 c)
            {
                half4 k = half4(0.0h, -1.0h / 3.0h, 2.0h / 3.0h, -1.0h);
                half4 p = lerp(half4(c.bg, k.wz), half4(c.gb, k.xy), step(c.b, c.g));
                half4 q = lerp(half4(p.xyw, c.r), half4(c.r, p.yzx), step(p.x, c.r));
                half d = q.x - min(q.w, q.y);
                half e = 1e-4h;
                return half3(abs(q.z + (q.w - q.y) / (6.0h * d + e)), d / (q.x + e), q.x);
            }

            half3 HsvToRgb(half3 c)
            {
                half4 k = half4(1.0h, 2.0h / 3.0h, 1.0h / 3.0h, 3.0h);
                half3 p = abs(frac(c.xxx + k.xyz) * 6.0h - k.www);
                return c.z * lerp(k.xxx, saturate(p - k.xxx), c.y);
            }

            half3 RemapSkin(half3 albedo)
            {
                half3 hsv = RgbToHsv(albedo);
                half3 src = RgbToHsv(_SourceSkin.rgb);
                half3 dst = RgbToHsv(_SkinColor.rgb);

                half hueDist = min(abs(hsv.x - src.x), 1.0h - abs(hsv.x - src.x));
                half hueMask = 1.0h - saturate(hueDist / max(_HueRange, 0.04h));
                half warmth = albedo.r - max(albedo.g, albedo.b);
                half warmMask = saturate(warmth * 6.0h);
                half satMask = saturate((hsv.y - 0.06h) / 0.10h);
                half valMask = saturate((hsv.z - 0.16h) / 0.16h);
                half mask = max(hueMask, warmMask) * satMask * valMask;

                half value = hsv.z * (dst.z / max(src.z, 0.08h));
                half3 remapped = HsvToRgb(half3(dst.x, dst.y, saturate(value)));
                return lerp(albedo, remapped, saturate(mask));
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
                half3 albedo = RemapSkin(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb);
                float3 n = normalize(IN.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half NdotL = saturate(dot(n, mainLight.direction));
                half wrap = NdotL * 0.65h + 0.35h;
                half3 ambient = SampleSH(n);
                half3 lit = albedo * (ambient + mainLight.color * wrap * mainLight.shadowAttenuation);
                lit = MixFog(lit, IN.fogCoord);
                return half4(lit, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            float4 GetShadowPositionHClip(Attributes IN)
            {
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            float4 ShadowVert(Attributes IN) : SV_POSITION
            {
                return GetShadowPositionHClip(IN);
            }

            half4 ShadowFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half DepthFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
