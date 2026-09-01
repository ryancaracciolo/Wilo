// Stylized lake water for URP. Shallows stay see-through (about 1 gameplay
// foot) and the bed fades out by the hide-at depth (18 ft by default).
Shader "Wilo/Lake Water"
{
    Properties
    {
        [Header(Color)]
        [MainColor] _ShallowColor("Shallow Color", Color) = (0.15, 0.45, 0.54, 0.5)
        _DeepColor("Deep Color", Color) = (0.12, 0.40, 0.50, 1)

        [Header(Visibility)]
        _Visibility("Hide Bottom At (ft)", Range(2, 40)) = 18
        _ClearFeet("Clear Through (ft)", Range(0, 8)) = 1
        _DepthSoftness("Depth Softness", Range(0.4, 3)) = 1.25

        [Header(Ripples)]
        _RippleFoam("Ripple Foam", Color) = (0.82, 0.93, 0.95, 1)
        _RippleStrength("Ripple Strength", Range(0, 2)) = 1
        _FresnelPower("Fresnel Power", Range(1, 8)) = 5
        _FresnelStrength("Fresnel Strength", Range(0, 1)) = 0.4
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                float _Visibility;
                float _ClearFeet;
                float _DepthSoftness;
                half _Smoothness;
                half _FresnelPower;
                half _FresnelStrength;
                half4 _RippleFoam;
                half _RippleStrength;
            CBUFFER_END

            #define WILO_MAX_RIPPLES 32
            float4 _WiloRipplePos[WILO_MAX_RIPPLES];
            float4 _WiloRippleParams[WILO_MAX_RIPPLES];
            float _WiloRippleCount;
            float _WiloRippleTime;
            float _WiloGameplayDepthScale;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogCoord : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            float EyeDepth(float rawDepth)
            {
                if (unity_OrthoParams.w > 0.5)
                {
                    #if UNITY_REVERSED_Z
                    return lerp(_ProjectionParams.z, _ProjectionParams.y, rawDepth);
                    #else
                    return lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepth);
                    #endif
                }

                return LinearEyeDepth(rawDepth, _ZBufferParams);
            }

            float Hash21(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            void SampleRipples(float2 xz, out float foam, out float2 slope)
            {
                foam = 0.0;
                slope = 0.0;

                int rippleCount = (int)_WiloRippleCount;
                rippleCount = min(rippleCount, WILO_MAX_RIPPLES);
                if (rippleCount <= 0)
                    return;

                float now = _WiloRippleTime;
                for (int i = 0; i < rippleCount; i++)
                {
                    float4 pos = _WiloRipplePos[i];
                    float4 par = _WiloRippleParams[i];
                    float age = now - pos.z;
                    if (age < 0.0 || age > pos.w)
                        continue;

                    float2 delta = xz - pos.xy;
                    float dist = length(delta);
                    float2 dir = dist > 0.001 ? delta / dist : float2(0, 1);
                    float ang = atan2(delta.y, delta.x);
                    float circular = saturate(par.w);
                    float phase = Hash21(pos.xy) * 6.2831;

                    float warpAmt = 1.0 - circular;
                    float warp = 1.0
                        + warpAmt * 0.16 * sin(ang * 3.0 + phase)
                        + warpAmt * 0.08 * sin(ang * 5.0 + phase * 1.7);
                    float distW = dist / max(warp, 0.5);

                    float ring = par.x * age;
                    float offset = distW - ring;
                    float width = max(par.y, 0.05) * lerp(1.55, 1.12, circular);
                    float wave = exp(-offset * offset / (width * width));

                    float patch = 0.55 + 0.45 * sin(ang * 4.0 + phase * 2.0 + age);
                    patch *= 0.7 + 0.3 * Hash21(dir * 6.0 + pos.xy);
                    patch = lerp(saturate(patch), 1.0, circular);

                    float life = max(pos.w, 0.01);
                    float fade = 1.0 - saturate(age / life);
                    fade *= fade;
                    float born = saturate(age * 5.0);
                    float wash = saturate(1.0 - distW / max(ring + width, 0.05));
                    wash *= wash * exp(-age * 2.4) * lerp(0.28, 0.12, circular);

                    float sample = (wave * patch * lerp(0.75, 0.95, circular) + wash) * par.z * fade * born;
                    foam += sample;
                    slope += dir * sample * (-1.4 * offset / (width * width));
                }
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 screenUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                float sceneEye = EyeDepth(SampleSceneDepth(screenUV));
                float surfaceEye = EyeDepth(IN.positionCS.z);
                float viewThickness = max(0.0, sceneEye - surfaceEye);

                float3 viewDir = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                float verticalDepth = viewThickness * max(abs(viewDir.y), 0.2);
                // Same feet the sonar shows. 1 ft stays see-through; hide-at
                // (default 18) is solid water with no bed showing through.
                float scale = _WiloGameplayDepthScale > 0.05 ? _WiloGameplayDepthScale : 0.4;
                float depthFeet = verticalDepth * scale * 3.28084;
                float clearFeet = max(_ClearFeet, 0.0);
                float hideFeet = max(_Visibility, clearFeet + 0.01);
                float fade = saturate((depthFeet - clearFeet) / (hideFeet - clearFeet));
                fade = pow(fade, _DepthSoftness);

                half3 albedo = lerp(_ShallowColor.rgb, _DeepColor.rgb, fade);
                half alpha = lerp(_ShallowColor.a, 1.0h, fade);

                float foam;
                float2 slope;
                SampleRipples(IN.positionWS.xz, foam, slope);
                foam *= _RippleStrength;

                float3 n = normalize(IN.normalWS + float3(slope.x, 0.0, slope.y) * 0.45);
                Light mainLight = GetMainLight();
                half NdotL = saturate(dot(n, mainLight.direction));
                half3 ambient = SampleSH(n);
                // Keep the authored water hue. Multiplying blue water by warm
                // sunlight (or a green equator bounce) turns the lake green in Play.
                half sunLum = dot(mainLight.color, half3(0.2126, 0.7152, 0.0722));
                half3 lit = albedo * (ambient + sunLum * (0.4 + 0.6 * NdotL));

                half3 halfDir = SafeNormalize(mainLight.direction + viewDir);
                half spec = pow(saturate(dot(n, halfDir)), exp2(10.0 * _Smoothness + 1.0)) * _Smoothness;
                lit += mainLight.color * spec;

                half fresnel = pow(1.0 - saturate(dot(n, viewDir)), _FresnelPower);
                half perceptualRoughness = 1.0 - _Smoothness;
                half3 reflection = GlossyEnvironmentReflection(reflect(-viewDir, n), IN.positionWS, perceptualRoughness, 1.0h);
                lit = lerp(lit, reflection, fresnel * _FresnelStrength * _Smoothness);
                lit = lerp(lit, _RippleFoam.rgb * (ambient + mainLight.color), saturate(foam) * 0.38);
                alpha = saturate(alpha + fresnel * _FresnelStrength * 0.35 + foam * 0.12);

                // Let a boat hull show through a thin slice of water.
                float skin = saturate(verticalDepth / 0.12);
                alpha *= lerp(0.22, 1.0, skin * skin);

                lit = MixFog(lit, IN.fogCoord);
                return half4(lit, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
