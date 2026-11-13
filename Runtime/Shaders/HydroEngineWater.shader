Shader "HydroEngine/Water (URP)"
{
    Properties
    {
        [Header(Water Body)]
        _ShallowColor ("Shallow Water Color", Color) = (0.12, 0.55, 0.58, 1)
        _DeepColor ("Deep Water Color", Color) = (0.015, 0.09, 0.17, 1)
        [Header(Reflection)]
        _WaterIOR ("Water Index of Refraction", Range(1, 1.6)) = 1.333
        _SurfaceRoughness ("Reflection Roughness", Range(0.04, 0.8)) = 0.16
        _PlanarReflectionStrength ("Planar Reflection Blend", Range(0, 1)) = 0.85
        _PlanarReflectionDistortion ("Wave Reflection Warp", Range(-0.1, 0.1)) = 0.015
        [Toggle] _ScreenSpaceReflections ("Trace Screen-Space Reflections", Float) = 1
        _ScreenSpaceReflectionStrength ("Screen-Space Reflection Strength", Range(0, 1)) = 0.85
        _ScreenSpaceReflectionMaxDistance ("Screen-Space Reflection Distance", Range(2, 100)) = 40
        _ScreenSpaceReflectionThickness ("Screen-Space Reflection Thickness", Range(0.02, 2)) = 0.35
        _ScreenSpaceReflectionSteps ("Screen-Space Reflection Steps", Range(8, 48)) = 24
        _ScreenSpaceReflectionRoughnessFade ("SSR Roughness Fade", Range(0, 1)) = 0.8
        _EnvironmentReflectionStrength ("Environment Reflection", Range(0, 1)) = 1
        _ReflectionFallback ("Reflection Fallback Color", Color) = (0.12, 0.28, 0.4, 1)
        _RefractionStrength ("Refraction Amount", Range(0, 0.08)) = 0.025
        [Header(Subsurface Scattering)]
        _SubsurfaceColor ("Scattered Water Color", Color) = (0.08, 0.38, 0.48, 1)
        _SubsurfaceStrength ("Crest Scattering", Range(0, 2)) = 0.65
        _SubsurfacePeakThreshold ("Peak Compression", Range(0, 0.8)) = 0.04
        _SubsurfacePeakSoftness ("Peak Feather", Range(0.01, 0.5)) = 0.22
        _SubsurfacePhasePower ("Sun Direction Focus", Range(1, 16)) = 4
        [Header(Sun Highlight)]
        _SunHighlightStrength ("Broad Sun Glint", Range(0, 2)) = 0.5
        _SunHighlightWidth ("Sun Glint Width", Range(0.04, 0.5)) = 0.2
        [Header(Depth and Transmission)]
        _AbsorptionSpectrum ("Water Absorption Spectrum", Vector) = (1.4, 0.35, 0.12, 0)
        _DepthAbsorption ("Absorption Strength", Range(0.01, 2)) = 0.22
        _ShoreFadeDistance ("Shallow Water Distance", Range(0.05, 5)) = 0.8
        [Header(Foam)]
        _FoamColor ("Foam Color", Color) = (0.82, 0.95, 1, 1)
        _FoamThreshold ("Crest Compression Threshold", Range(0, 0.8)) = 0.42
        _FoamSoftness ("Foam Edge Softness", Range(0.01, 0.5)) = 0.09
        _FoamStrength ("Foam Amount", Range(0, 1)) = 0.24
        _FoamBreakup ("Foam Breakup", Range(0, 0.8)) = 0.12
        _FoamNoiseScale ("Foam Pattern Scale", Range(0.05, 4)) = 0.65
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        Pass
        {
            Name "ForwardWater"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On
            // The shader composites opaque-scene transmission and reflection itself.
            // Writing the completed surface avoids alpha-blending the refracted scene twice.
            Blend One Zero

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GlobalIllumination.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _DeepColor, _FoamColor, _SubsurfaceColor;
                float _FoamThreshold, _FoamSoftness, _FoamStrength, _FoamBreakup, _FoamNoiseScale;
                float _WaterIOR, _SurfaceRoughness, _EnvironmentReflectionStrength, _RefractionStrength;
                float _PlanarReflectionStrength, _PlanarReflectionDistortion;
                float _ScreenSpaceReflections, _ScreenSpaceReflectionStrength;
                float _ScreenSpaceReflectionMaxDistance, _ScreenSpaceReflectionThickness, _ScreenSpaceReflectionSteps;
                float _ScreenSpaceReflectionRoughnessFade;
                float _SubsurfaceStrength, _SubsurfacePeakThreshold, _SubsurfacePeakSoftness, _SubsurfacePhasePower;
                float _SunHighlightStrength, _SunHighlightWidth;
                float _DepthAbsorption, _ShoreFadeDistance;
                float4 _AbsorptionSpectrum;
                half4 _ReflectionFallback;
                float _HydroSpectralEnabled, _HydroSpectralPatchLength, _HydroSpectralResolution;
            CBUFFER_END

            TEXTURE2D(_HydroPlanarReflectionTexture);
            SAMPLER(sampler_HydroPlanarReflectionTexture);
            TEXTURE2D(_HydroSpectralDisplacement);
            SAMPLER(sampler_HydroSpectralDisplacement);
            float _HydroPlanarReflectionEnabled;
            float _HydroRenderingPlanarReflection;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float surfaceCompression : TEXCOORD2;
            };

            float3 FresnelSchlick(float cosine, float3 f0)
            {
                float factor = pow(1.0 - saturate(cosine), 5.0);
                return f0 + (1.0 - f0) * factor;
            }

            float DistributionGGX(float nDotH, float alphaSquared)
            {
                float denominator = nDotH * nDotH * (alphaSquared - 1.0) + 1.0;
                return alphaSquared / max(1e-6, 3.14159265 * denominator * denominator);
            }

            float VisibilitySmithGGX(float nDotV, float nDotL, float alphaSquared)
            {
                float viewTerm = nDotL * sqrt(max(1e-6, nDotV * nDotV * (1.0 - alphaSquared) + alphaSquared));
                float lightTerm = nDotV * sqrt(max(1e-6, nDotL * nDotL * (1.0 - alphaSquared) + alphaSquared));
                return 0.5 / max(1e-5, viewTerm + lightTerm);
            }

            float3 EvaluateDirectWaterSpecular(float3 N, float3 V, float3 L, float3 f0, float roughness)
            {
                float nDotV = saturate(dot(N, V));
                float nDotL = saturate(dot(N, L));
                if (nDotV <= 0.0 || nDotL <= 0.0) return 0.0;

                float3 H = SafeNormalize(V + L);
                float nDotH = saturate(dot(N, H));
                float vDotH = saturate(dot(V, H));
                float alpha = max(0.0016, roughness * roughness);
                float alphaSquared = alpha * alpha;
                float distribution = DistributionGGX(nDotH, alphaSquared);
                float visibility = VisibilitySmithGGX(nDotV, nDotL, alphaSquared);
                float3 fresnel = FresnelSchlick(vDotH, f0);
                return distribution * visibility * fresnel;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(cell);
                float b = Hash21(cell + float2(1, 0));
                float c = Hash21(cell + float2(0, 1));
                float d = Hash21(cell + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float2 ProjectViewPositionToUV(float3 positionVS, out float clipW)
            {
                float4 positionCS = mul(UNITY_MATRIX_P, float4(positionVS, 1.0));
                clipW = positionCS.w;
                float4 screenPosition = ComputeScreenPos(positionCS);
                return screenPosition.xy / max(1e-5, screenPosition.w);
            }

            half3 TraceScreenSpaceReflection(float3 positionWS, float3 normalWS, float3 viewDirectionWS,
                float2 startUV, float startSceneEyeDepth, out float confidence)
            {
                confidence = 0.0;
                float3 originVS = TransformWorldToView(positionWS);
                float3 normalVS = SafeNormalize(TransformWorldToViewDir(normalWS, true));
                float3 reflectedDirectionWS = reflect(-viewDirectionWS, normalWS);
                float3 rayDirectionVS = SafeNormalize(TransformWorldToViewDir(reflectedDirectionWS, true));

                // Do not trace rays that point behind the view camera.
                if (rayDirectionVS.z >= -0.001) return 0.0h;

                float3 rayOriginVS = originVS + normalVS * 0.04 + rayDirectionVS * 0.08;
                float previousDistance = 0.0;
                float previousDelta = -max(0.01, startSceneEyeDepth + originVS.z);
                float hitDistance = 0.0;
                float2 hitUV = startUV;
                bool hit = false;
                int stepCount = clamp((int)_ScreenSpaceReflectionSteps, 8, 48);

                [loop] for (int stepIndex = 0; stepIndex < 48; stepIndex++)
                {
                    if (stepIndex >= stepCount) break;
                    float rayDistance = _ScreenSpaceReflectionMaxDistance
                        * ((float)(stepIndex + 1) / (float)stepCount);
                    float3 rayPositionVS = rayOriginVS + rayDirectionVS * rayDistance;
                    float rayEyeDepth = -rayPositionVS.z;
                    if (rayEyeDepth <= 0.0) break;

                    float clipW;
                    float2 rayUV = ProjectViewPositionToUV(rayPositionVS, clipW);
                    if (clipW <= 0.0 || any(rayUV <= 0.001) || any(rayUV >= 0.999)) break;

                    float sceneEyeDepth = LinearEyeDepth(SampleSceneDepth(rayUV), _ZBufferParams);
                    float depthDelta = rayEyeDepth - sceneEyeDepth;
                    if (depthDelta >= 0.0 && previousDelta < 0.0)
                    {
                        // Refine the first front-to-back depth crossing to reduce
                        // overshooting thin props at coarse trace distances.
                        float nearDistance = previousDistance;
                        float farDistance = rayDistance;
                        [unroll] for (int refineIndex = 0; refineIndex < 4; refineIndex++)
                        {
                            float midDistance = (nearDistance + farDistance) * 0.5;
                            float3 midPositionVS = rayOriginVS + rayDirectionVS * midDistance;
                            float midClipW;
                            float2 midUV = ProjectViewPositionToUV(midPositionVS, midClipW);
                            float midRayDepth = -midPositionVS.z;
                            float midSceneDepth = LinearEyeDepth(SampleSceneDepth(saturate(midUV)), _ZBufferParams);
                            if (midRayDepth >= midSceneDepth) farDistance = midDistance;
                            else nearDistance = midDistance;
                        }

                        hitDistance = farDistance;
                        float3 hitPositionVS = rayOriginVS + rayDirectionVS * hitDistance;
                        float hitClipW;
                        hitUV = ProjectViewPositionToUV(hitPositionVS, hitClipW);
                        float finalSceneDepth = LinearEyeDepth(SampleSceneDepth(saturate(hitUV)), _ZBufferParams);
                        float finalRayDepth = -hitPositionVS.z;
                        hit = hitClipW > 0.0 && abs(finalRayDepth - finalSceneDepth) <= _ScreenSpaceReflectionThickness;
                        break;
                    }

                    previousDistance = rayDistance;
                    previousDelta = depthDelta;
                }

                if (!hit) return 0.0h;

                float edgeDistance = min(min(hitUV.x, hitUV.y), min(1.0 - hitUV.x, 1.0 - hitUV.y));
                float edgeFade = smoothstep(0.0, 0.08, edgeDistance);
                float distanceFade = 1.0 - smoothstep(_ScreenSpaceReflectionMaxDistance * 0.65,
                    _ScreenSpaceReflectionMaxDistance, hitDistance);
                confidence = edgeFade * distanceFade;
                return SampleSceneColor(saturate(hitUV)).rgb;
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 basePosition = TransformObjectToWorld(input.positionOS.xyz);
                float3 positionWS = basePosition;
                float3 tangentX = float3(1, 0, 0);
                float3 tangentZ = float3(0, 0, 1);
                float compression = 0.0;

                if (_HydroSpectralEnabled > 0.5)
                {
                    float patchLength = max(16.0, _HydroSpectralPatchLength);
                    float2 uv = basePosition.xz / patchLength;
                    float2 texel = 1.0 / max(64.0, _HydroSpectralResolution);
                    float4 displacement = SAMPLE_TEXTURE2D_LOD(_HydroSpectralDisplacement,
                        sampler_HydroSpectralDisplacement, uv, 0);
                    float3 left = SAMPLE_TEXTURE2D_LOD(_HydroSpectralDisplacement,
                        sampler_HydroSpectralDisplacement, uv - float2(texel.x, 0), 0).xyz;
                    float3 right = SAMPLE_TEXTURE2D_LOD(_HydroSpectralDisplacement,
                        sampler_HydroSpectralDisplacement, uv + float2(texel.x, 0), 0).xyz;
                    float3 down = SAMPLE_TEXTURE2D_LOD(_HydroSpectralDisplacement,
                        sampler_HydroSpectralDisplacement, uv - float2(0, texel.y), 0).xyz;
                    float3 up = SAMPLE_TEXTURE2D_LOD(_HydroSpectralDisplacement,
                        sampler_HydroSpectralDisplacement, uv + float2(0, texel.y), 0).xyz;
                    positionWS += displacement;
                    tangentX += (right - left) * (0.5 * _HydroSpectralResolution / patchLength);
                    tangentZ += (up - down) * (0.5 * _HydroSpectralResolution / patchLength);
                    compression = displacement.a;
                }

                o.positionCS = TransformWorldToHClip(positionWS);
                o.worldPos = positionWS;
                o.normalWS = normalize(cross(tangentZ, tangentX));
                o.surfaceCompression = compression;
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                // Keep water out of the planar-reflection capture to prevent recursive views.
                clip(0.5 - _HydroRenderingPlanarReflection);
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                float3 V = SafeNormalize(_WorldSpaceCameraPos - i.worldPos);
                float3 N = normalize(i.normalWS);

                float rawSceneDepth = SampleSceneDepth(screenUV);
                float sceneEyeDepth = LinearEyeDepth(rawSceneDepth, _ZBufferParams);
                float waterEyeDepth = -TransformWorldToView(i.worldPos).z;
                float waterDepth = max(0, sceneEyeDepth - waterEyeDepth);
                float3 absorption = max(_AbsorptionSpectrum.rgb, 0.0) * _DepthAbsorption;
                float3 transmittance = exp(-waterDepth * absorption);

                float3 normalVS = TransformWorldToViewDir(N, true);
                float refractionDepthWeight = saturate(waterDepth / max(0.1, _ShoreFadeDistance));
                float2 refractedUV = saturate(screenUV + normalVS.xy * _RefractionStrength * refractionDepthWeight);
                float refractedEyeDepth = LinearEyeDepth(SampleSceneDepth(refractedUV), _ZBufferParams);
                // Do not pull foreground pixels across the water silhouette. Soften the
                // transition to avoid a hard discontinuity at the depth edge.
                float refractionDepthValid = smoothstep(waterEyeDepth + 0.01, waterEyeDepth + 0.12, refractedEyeDepth);
                refractedUV = lerp(screenUV, refractedUV, refractionDepthValid);
                half3 refractedScene = SampleSceneColor(refractedUV);
                half3 transmittedScene = refractedScene * transmittance + _DeepColor.rgb * (1.0 - transmittance);
                float shoreWeight = saturate(waterDepth / max(0.01, _ShoreFadeDistance));
                half3 bodyColor = lerp(_ShallowColor.rgb, transmittedScene, shoreWeight);

                float ndotv = saturate(dot(N, V));
                float safeIOR = max(1.001, _WaterIOR);
                float f0Scalar = ((safeIOR - 1.0) / (safeIOR + 1.0));
                f0Scalar *= f0Scalar;
                float3 f0 = f0Scalar.xxx;
                float3 fresnel = FresnelSchlick(ndotv, f0);
                float3 reflectionVector = reflect(-V, N);
                half3 environmentReflection = GlossyEnvironmentReflection(reflectionVector, i.worldPos,
                    saturate(_SurfaceRoughness), 1.0, screenUV);
                environmentReflection = lerp(_ReflectionFallback.rgb, environmentReflection,
                    saturate(_EnvironmentReflectionStrength));
                if (_HydroPlanarReflectionEnabled > 0.5)
                {
                    // Planar captures are projections of a flat mirror plane. Keep the
                    // base mapping screen-aligned; approximate wave curvature by gently
                    // warping with the geometric normal instead of reprojecting height.
                    float2 planarUV = screenUV;
                    // Fade the warp at scene intersections to keep object contact edges
                    // stable while retaining wave breakup over open water.
                    float reflectionIntersection = smoothstep(0.02, 0.2, waterDepth);
                    float3 normalVS = TransformWorldToViewDir(N, true);
                    float3 flatNormalVS = TransformWorldToViewDir(float3(0.0, 1.0, 0.0), true);
                    planarUV += (normalVS.xy - flatNormalVS.xy) * _PlanarReflectionDistortion
                        * reflectionIntersection;
                    float edgeDistance = min(min(planarUV.x, planarUV.y), min(1.0 - planarUV.x, 1.0 - planarUV.y));
                    float edgeValidity = smoothstep(-0.002, 0.002, edgeDistance);
                    // Reduce planar object reflections at water/object intersections to
                    // avoid harsh contact seams; the environment source remains intact.
                    float reflectionWeight = saturate(_PlanarReflectionStrength)
                        * edgeValidity * reflectionIntersection;
                    half3 planarReflection = SAMPLE_TEXTURE2D(_HydroPlanarReflectionTexture,
                        sampler_HydroPlanarReflectionTexture, saturate(planarUV)).rgb;
                    environmentReflection = lerp(environmentReflection, planarReflection, reflectionWeight);
                }
                if (_ScreenSpaceReflections > 0.5 && _ScreenSpaceReflectionStrength > 0.001)
                {
                    float ssrConfidence;
                    half3 screenSpaceReflection = TraceScreenSpaceReflection(i.worldPos, N, V,
                        screenUV, sceneEyeDepth, ssrConfidence);
                    float roughnessConfidence = 1.0 - saturate(_SurfaceRoughness * _ScreenSpaceReflectionRoughnessFade);
                    float ssrWeight = saturate(_ScreenSpaceReflectionStrength * ssrConfidence * roughnessConfidence);
                    environmentReflection = lerp(environmentReflection, screenSpaceReflection, ssrWeight);
                }
                float4 shadowCoord = TransformWorldToShadowCoord(i.worldPos);
                Light mainLight = GetMainLight(shadowCoord);
                float3 directSpecular = EvaluateDirectWaterSpecular(N, V, mainLight.direction, f0,
                    max(0.04, _SurfaceRoughness));
                directSpecular *= mainLight.color * mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                float reflectedSunAlignment = saturate(dot(reflectionVector, mainLight.direction));
                float sunLobeExponent = max(4.0, 2.0 / max(0.0016, _SunHighlightWidth * _SunHighlightWidth));
                float broadSunLobe = pow(reflectedSunAlignment, sunLobeExponent);
                directSpecular += mainLight.color * (_SunHighlightStrength * broadSunLobe)
                    * mainLight.distanceAttenuation * mainLight.shadowAttenuation * fresnel;
                half3 color = bodyColor * (1.0 - fresnel) + environmentReflection * fresnel + directSpecular;

                // Lightweight water-volume scattering approximation. Horizontal
                // Gerstner compression marks thin, pinched peaks where light can
                // contribute more strongly through the water.
                float peakMask = smoothstep(_SubsurfacePeakThreshold,
                    _SubsurfacePeakThreshold + max(0.01, _SubsurfacePeakSoftness),
                    max(0.0, i.surfaceCompression));
                float sunViewAlignment = saturate(dot(V, mainLight.direction));
                float scatteringPhase = pow(sunViewAlignment, max(1.0, _SubsurfacePhasePower));
                float scatteringLight = (0.18 + 0.82 * scatteringPhase) * peakMask * _SubsurfaceStrength;
                scatteringLight *= mainLight.distanceAttenuation * mainLight.shadowAttenuation * (1.0 - fresnel);
                color += _SubsurfaceColor.rgb * mainLight.color * scatteringLight;

                float2 foamUV = i.worldPos.xz * _FoamNoiseScale;
                float foamNoise = lerp(ValueNoise(foamUV), ValueNoise(foamUV * 2.13 + 8.7), 0.35);
                float threshold = _FoamThreshold + (foamNoise - 0.5) * _FoamBreakup;
                float crestFoam = smoothstep(threshold, threshold + max(0.01, _FoamSoftness), i.surfaceCompression);
                float shorelineFoam = (1.0 - smoothstep(0.02, max(0.03, _ShoreFadeDistance), waterDepth))
                    * smoothstep(0.25, 0.75, foamNoise);
                half foam = max(crestFoam, shorelineFoam) * _FoamStrength;
                color = lerp(color, _FoamColor.rgb, foam);

                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
