Shader "Slime/Glass/URP SSR Glass"
{
    Properties
    {
        [Header(Surface)]
        _BaseColor ("Glass Tint", Color) = (0.72, 0.95, 1.0, 0.34)
        _TransmissionColor ("Transmission Color", Color) = (0.68, 0.9, 1.0, 1.0)
        _Opacity ("Opacity", Range(0.02, 1.0)) = 0.34
        _RefractionStrength ("Refraction Strength", Range(0.0, 0.08)) = 0.022
        _Thickness ("Thickness", Range(0.0, 2.0)) = 0.35

        [Header(Reflection)]
        _ReflectionStrength ("SSR Strength", Range(0.0, 1.0)) = 0.65
        _SkyReflectionStrength ("Skybox Reflection", Range(0.0, 1.0)) = 0.45
        _SSRToSkyBlend ("SSR/Sky Blend", Range(0.0, 1.0)) = 0.25
        _FresnelPower ("Fresnel Power", Range(0.5, 8.0)) = 4.0
        _FresnelBias ("Fresnel Bias", Range(0.0, 1.0)) = 0.04
        _Roughness ("Roughness Blur", Range(0.0, 1.0)) = 0.18

        [Header(Screen Space Raymarch)]
        _SSRStepSize ("Step Size", Range(0.02, 1.5)) = 0.18
        _SSRMaxDistance ("Max Distance", Range(0.5, 80.0)) = 18.0
        _SSRThickness ("Hit Thickness", Range(0.001, 1.0)) = 0.16
        _SSRMaxSteps ("Max Steps", Range(4, 96)) = 48
        _SSREdgeFade ("Edge Fade", Range(0.0, 0.3)) = 0.08

        [Header(Normal)]
        [NoScaleOffset] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0.0, 2.0)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SSRGlass"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
                float2 uv : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _TransmissionColor;
                half _Opacity;
                half _RefractionStrength;
                half _Thickness;
                half _ReflectionStrength;
                half _SkyReflectionStrength;
                half _SSRToSkyBlend;
                half _FresnelPower;
                half _FresnelBias;
                half _Roughness;
                half _SSRStepSize;
                half _SSRMaxDistance;
                half _SSRThickness;
                half _SSRMaxSteps;
                half _SSREdgeFade;
                half _BumpScale;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = float4(normalInputs.tangentWS, input.tangentOS.w);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.uv = input.uv;
                return output;
            }

            float3 ApplyNormalMap(float3 normalWS, float4 tangentWS, float2 uv)
            {
                float3 tangent = normalize(tangentWS.xyz);
                float3 bitangent = normalize(cross(normalWS, tangent) * tangentWS.w);
                float3x3 tangentToWorld = float3x3(tangent, bitangent, normalize(normalWS));

                float4 packedNormal = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv);
                float3 normalTS = UnpackNormalScale(packedNormal, _BumpScale);
                return normalize(mul(normalTS, tangentToWorld));
            }

            float EdgeFade(float2 uv)
            {
                float2 edgeDistance = min(uv, 1.0 - uv);
                float edge = min(edgeDistance.x, edgeDistance.y);
                return saturate(edge / max(_SSREdgeFade, 0.0001));
            }

            bool IsInsideScreen(float2 uv)
            {
                return all(uv > 0.001) && all(uv < 0.999);
            }

            float3 ReconstructViewPosition(float2 uv, float rawDepth)
            {
                float deviceDepth = rawDepth;
            #if UNITY_REVERSED_Z
                deviceDepth = rawDepth;
            #else
                deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
            #endif

                float3 positionWS = ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
                return TransformWorldToView(positionWS);
            }

            float4 ProjectViewToClip(float3 positionVS)
            {
                return mul(GetViewToHClipMatrix(), float4(positionVS, 1.0));
            }

            half3 SampleOpaqueWithBlur(float2 uv, half roughness)
            {
                float2 texel = _CameraOpaqueTexture_TexelSize.xy;
                half3 color = SampleSceneColor(uv).rgb;

                UNITY_BRANCH
                if (roughness > 0.001)
                {
                    float radius = roughness * 4.0;
                    color += SampleSceneColor(uv + texel * float2(radius, 0.0)).rgb;
                    color += SampleSceneColor(uv + texel * float2(-radius, 0.0)).rgb;
                    color += SampleSceneColor(uv + texel * float2(0.0, radius)).rgb;
                    color += SampleSceneColor(uv + texel * float2(0.0, -radius)).rgb;
                    color *= 0.2;
                }

                return color;
            }

            // Sample the environment cubemap directly to avoid pulling in URP's full PBR GI stack.
            half3 SampleSkyReflection(float3 reflectionDirWS)
            {
                half mip = PerceptualRoughnessToMipmapLevel(saturate(_Roughness));
                half4 encodedReflection = SAMPLE_TEXTURECUBE_LOD(
                    unity_SpecCube0,
                    samplerunity_SpecCube0,
                    normalize(reflectionDirWS),
                    mip);
                half3 skyReflection = DecodeHDREnvironment(encodedReflection, unity_SpecCube0_HDR);
                return skyReflection * _SkyReflectionStrength;
            }

            half4 RaymarchSSR(float3 startVS, float3 reflectionDirVS)
            {
                reflectionDirVS = normalize(reflectionDirVS);

                UNITY_BRANCH
                if (reflectionDirVS.z >= -0.001)
                {
                    return half4(0.0, 0.0, 0.0, 0.0);
                }

                float maxSteps = clamp(_SSRMaxSteps, 4.0, 96.0);
                float stride = max(_SSRStepSize, 0.001);
                float hitAlpha = 0.0;
                float2 hitUV = 0.0;

                [loop]
                for (int i = 1; i <= 96; i++)
                {
                    UNITY_BRANCH
                    if (i > maxSteps)
                    {
                        break;
                    }

                    float distance = i * stride;
                    UNITY_BRANCH
                    if (distance > _SSRMaxDistance)
                    {
                        break;
                    }

                    float3 sampleVS = startVS + reflectionDirVS * distance;
                    float4 clip = ProjectViewToClip(sampleVS);
                    float4 screenPos = ComputeScreenPos(clip);
                    float2 uv = screenPos.xy / max(screenPos.w, 0.0001);

                    UNITY_BRANCH
                    if (!IsInsideScreen(uv))
                    {
                        break;
                    }

                    float rawDepth = SampleSceneDepth(uv);
                    UNITY_BRANCH
                    if (rawDepth <= 0.000001)
                    {
                        continue;
                    }

                    float3 sceneVS = ReconstructViewPosition(uv, rawDepth);
                    float depthDelta = sceneVS.z - sampleVS.z;

                    UNITY_BRANCH
                    if (depthDelta >= 0.0 && depthDelta < _SSRThickness)
                    {
                        float distanceFade = saturate(1.0 - distance / max(_SSRMaxDistance, 0.0001));
                        hitAlpha = distanceFade * EdgeFade(uv);
                        hitUV = uv;
                        break;
                    }
                }

                half3 reflection = SampleOpaqueWithBlur(hitUV, _Roughness);
                return half4(reflection, hitAlpha);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float3 normalWS = ApplyNormalMap(normalize(input.normalWS), input.tangentWS, input.uv);
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 viewDirVS = normalize(TransformWorldToViewDir(viewDirWS, true));
                float3 normalVS = normalize(TransformWorldToViewDir(normalWS, true));

                float2 refractOffset = normalVS.xy * _RefractionStrength * saturate(1.0 + normalVS.z);
                float2 refractUV = saturate(screenUV + refractOffset);
                half3 sceneColor = SampleOpaqueWithBlur(refractUV, _Roughness * 0.5);

                float3 positionVS = TransformWorldToView(input.positionWS);
                float3 reflectionDirVS = reflect(-viewDirVS, normalVS);
                float3 reflectionDirWS = reflect(-viewDirWS, normalWS);
                half4 ssr = RaymarchSSR(positionVS + normalVS * 0.03, reflectionDirVS);

                half fresnel = saturate(_FresnelBias + (1.0 - _FresnelBias) * pow(1.0 - saturate(dot(normalWS, viewDirWS)), _FresnelPower));
                half3 absorption = lerp(_TransmissionColor.rgb, _BaseColor.rgb, saturate(_Thickness));
                half3 refractedGlass = sceneColor * absorption;
                half3 surfaceTint = _BaseColor.rgb * (0.16 + fresnel * 0.5);
                half3 skyReflection = SampleSkyReflection(reflectionDirWS);
                half skyWeight = saturate((1.0h - ssr.a) + _SSRToSkyBlend);
                half3 reflectionColor = lerp(ssr.rgb, skyReflection, skyWeight);
                half reflectionWeight = saturate((_ReflectionStrength * ssr.a + _SkyReflectionStrength * skyWeight) * fresnel);
                half3 reflected = lerp(refractedGlass, reflectionColor, reflectionWeight);
                half3 color = reflected + surfaceTint;

                half alpha = saturate(_Opacity + fresnel * 0.36);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
