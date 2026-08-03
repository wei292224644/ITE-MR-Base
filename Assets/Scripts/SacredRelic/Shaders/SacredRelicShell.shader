Shader "MRBase/Sacred Relic Shell"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (0.17, 0.15, 0.12, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.1

        [Header(Crack)]
        _CrackMask ("Crack Mask (R shape, G arrival, B cell)", 2D) = "black" {}
        _CrackStrength ("Crack Strength", Range(0, 1)) = 1
        _Progress ("Crack Progress", Range(0, 1)) = 0
        _CrackSoftness ("Crack Softness", Range(0.001, 0.3)) = 0.05
        // 0 = mask G channel (manifest centre-out); 1 = world axis from C# spreadFrom→spreadTo.
        _CrackSpreadMode ("Crack Spread Mode", Float) = 0
        _SpreadStartWS ("Spread Start WS", Vector) = (0, 0, 0, 0)
        _SpreadEndWS ("Spread End WS", Vector) = (0, 0, 0, 0)
        _GrooveColor ("Groove Color", Color) = (0.015, 0.012, 0.009, 1)
        _GrooveDepth ("Groove Depth", Range(0, 1)) = 0.9

        [Header(Sacred Light)]
        _GlowColor ("Glow Color", Color) = (1, 0.63, 0.22, 1)
        _GoldIntensity ("Gold Intensity", Range(0, 1)) = 0
        _GlowStrength ("Glow Strength", Range(0, 24)) = 7
        _TipBoost ("Growth Tip Boost", Range(0, 12)) = 5
        _TipWidth ("Growth Tip Width", Range(0.005, 0.25)) = 0.05

        [Header(Dissolve To Dust)]
        _Dissolve ("Dissolve", Range(0, 1)) = 0
        _EdgeWidth ("Edge Width", Range(0.001, 0.5)) = 0.09
        _EdgeColor ("Edge Color", Color) = (1, 0.55, 0.18, 1)
        _EdgeStrength ("Edge Strength", Range(0, 24)) = 2.1

        [Header(Dissolve Mode 0 Noise 1 Axis)]
        _DissolveMode ("Dissolve Mode", Float) = 0
        // Cells across one shard, not per metre — see DissolveNoise.
        _NoiseScale ("Noise Scale (cells per shard)", Float) = 9
        _GrainScale ("Grain Scale", Float) = 2.6
        _GrainStrength ("Grain Strength", Range(0, 1)) = 0.75
        // Filled per shard by SacredRelicFracture so the noise can be normalised by size.
        _ShardCentreOS ("Shard Centre (OS)", Vector) = (0, 0, 0, 0)
        _ShardSizeOS ("Shard Size (OS)", Vector) = (1, 1, 1, 0)

        // Axis mode mirrors INab's "Object Axis Mask" so a VFX Graph fed the same
        // numbers spawns its dust exactly on this surface's eroding front.
        _AxisDir ("Axis Direction (OS)", Vector) = (0, 1, 0, 0)
        _AxisMin ("Axis Min", Float) = -0.1
        _AxisMax ("Axis Max", Float) = 0.1
        _GuideTex ("Guide Texture", 2D) = "gray" {}
        _GuideTilling ("Guide Tilling", Float) = 8
        _GuideStrength ("Guide Strength", Float) = 0.035
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Smoothness;
            half _CrackStrength;
            half _Progress;
            half _CrackSoftness;
            float _CrackSpreadMode;
            float4 _SpreadStartWS;
            float4 _SpreadEndWS;
            half4 _GrooveColor;
            half _GrooveDepth;
            half4 _GlowColor;
            half _GoldIntensity;
            half _GlowStrength;
            half _TipBoost;
            half _TipWidth;
            half _Dissolve;
            half _EdgeWidth;
            half4 _EdgeColor;
            half _EdgeStrength;
            float _DissolveMode;
            float _NoiseScale;
            float _GrainScale;
            float _GrainStrength;
            float4 _ShardCentreOS;
            float4 _ShardSizeOS;
            float4 _AxisDir;
            float _AxisMin;
            float _AxisMax;
            float _GuideTilling;
            float _GuideStrength;
        CBUFFER_END

        TEXTURE2D(_BaseMap);        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_CrackMask);      SAMPLER(sampler_CrackMask);
        TEXTURE2D(_GuideTex);       SAMPLER(sampler_GuideTex);

        float Hash13(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.yzx + 33.33);
            return frac((p.x + p.y) * p.z);
        }

        float VNoise(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float n00 = lerp(Hash13(i), Hash13(i + float3(1, 0, 0)), f.x);
            float n10 = lerp(Hash13(i + float3(0, 1, 0)), Hash13(i + float3(1, 1, 0)), f.x);
            float n01 = lerp(Hash13(i + float3(0, 0, 1)), Hash13(i + float3(1, 0, 1)), f.x);
            float n11 = lerp(Hash13(i + float3(0, 1, 1)), Hash13(i + float3(1, 1, 1)), f.x);
            return lerp(lerp(n00, n10, f.y), lerp(n01, n11, f.y), f.z);
        }

        // Deliberately sin-free. Worley below calls this 27 times per fragment, so the obvious
        // frac(sin(dot(...))) form costs 81 transcendentals per pixel while the crust dissolves —
        // in three passes, on a Quest. It is also the one construct whose result genuinely differs
        // between this shader and RelicDustBakedPoints' CPU twin: sin of a few-thousand argument
        // has no accurate low bits left in fp32, and the *43758 amplifies whatever is there. Pure
        // frac/mul/add stays bit-comparable, so the dust can sit exactly on the eroding edge.
        float3 Hash33(float3 p)
        {
            p = frac(p * float3(0.1031, 0.1030, 0.0973));
            p += dot(p, p.yxz + 33.33);
            return frac((p.xxy + p.yxx) * p.zyx);
        }

        // Distance to the nearest of a jittered lattice of points. Sharp cell walls are what
        // give the eroding edge a grain of sand rather than a soft blob.
        float Worley(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            float best = 1e9;
            [unroll] for (int x = -1; x <= 1; ++x)
            [unroll] for (int y = -1; y <= 1; ++y)
            [unroll] for (int z = -1; z <= 1; ++z)
            {
                float3 g = float3(x, y, z);
                float3 o = Hash33(i + g);
                float3 d = g + o - f;
                best = min(best, dot(d, d));
            }
            return saturate(sqrt(best));
        }

        // Object space so the erosion pattern stays welded to the shard as it tumbles.
        //
        // Normalised by the shard's own size first. These pieces are authored with baked vertex
        // offsets, so positionOS is really world space — metres, running to 16 on a tall stele.
        // Scaling that directly meant _NoiseScale 42 produced cells 2.4 cm across, far under a
        // pixel when the whole stele is in frame, and clip() turned it into crawling static.
        // Dividing by the bounds makes _NoiseScale mean "cells across this shard" instead.
        float DissolveNoise(float3 positionOS)
        {
            float3 p = (positionOS - _ShardCentreOS.xyz) / max(1e-4, _ShardSizeOS.x) * _NoiseScale;
            float fbm = VNoise(p) * 0.6 + VNoise(p * 2.3) * 0.27 + VNoise(p * 5.1) * 0.13;
            // Worley pulls the iso-surface into grain-shaped clumps; the fbm keeps those clumps
            // from tiling into a visible lattice.
            float grain = Worley(p * _GrainScale);
            return saturate(lerp(fbm, fbm * 0.55 + grain * 0.45, _GrainStrength));
        }

        // Object space so the erosion front stays welded to the shard as it tumbles.
        // Mirrors INab's Object Axis Mask: an axis slice pushed around by a guide texture.
        float DissolveAxis(float3 positionOS, float2 uv)
        {
            float axis = dot(positionOS, normalize(_AxisDir.xyz));
            float guide = (SAMPLE_TEXTURE2D(_GuideTex, sampler_GuideTex,
                                            uv * _GuideTilling).r - 0.5) * 2.0 * _GuideStrength;
            float threshold = lerp(_AxisMin, _AxisMax, _Dissolve);
            // Normalised by the span so _EdgeWidth means the same thing in both modes.
            return (axis + guide - threshold) / max(1e-5, abs(_AxisMax - _AxisMin));
        }

        /// Positive keeps the pixel; the magnitude drives the glowing rim.
        float DissolveKeep(float3 positionOS, float2 uv)
        {
            return _DissolveMode < 0.5
                ? DissolveNoise(positionOS) - _Dissolve
                : DissolveAxis(positionOS, uv);
        }

        struct CrackTerms
        {
            half crack;
            half tip;
        };

        CrackTerms SampleCrack(float2 uv, float3 positionWS)
        {
            half4 m = SAMPLE_TEXTURE2D(_CrackMask, sampler_CrackMask, uv);
            half shape = m.r * _CrackStrength;
            CrackTerms o;

            if (_CrackSpreadMode > 0.5)
            {
                // Per-shard local _Progress (0-1): grow this piece's painted crack lines (R)
                // so peel follows the Voronoi seams, not a global wipe across the whole face.
                o.crack = shape * smoothstep(0.0h, 0.85h, _Progress);
                o.tip = shape * saturate(1.0h - abs(_Progress - 0.72h) / max(_TipWidth, 0.01h));
            }
            else
            {
                // Manifest mode: green holds when the fracture reaches this point along the
                // baked centre-out network.
                half arrival = m.g;
                half reveal = smoothstep(_Progress, _Progress - _CrackSoftness, arrival);
                o.crack = shape * reveal;
                o.tip = shape * saturate(1.0h - abs(arrival - _Progress) / max(_TipWidth, 0.01h));
            }
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(input.normalOS);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.positionOS = input.positionOS.xyz;
                o.normalWS = nrm.normalWS;
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half3 Shade(float3 positionWS, float3 normalWS, half3 albedo)
            {
                float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
                Light main = GetMainLight(shadowCoord);
                half3 lit = main.color * (saturate(dot(normalWS, main.direction)) *
                                          main.distanceAttenuation * main.shadowAttenuation);

                #ifdef _ADDITIONAL_LIGHTS
                uint count = GetAdditionalLightsCount();
                for (uint i = 0u; i < count; ++i)
                {
                    Light add = GetAdditionalLight(i, positionWS);
                    lit += add.color * (saturate(dot(normalWS, add.direction)) *
                                        add.distanceAttenuation * add.shadowAttenuation);
                }
                #endif

                return albedo * (lit + SampleSH(normalWS));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half edge = 0;
                if (_Dissolve > 0.0001)
                {
                    float keep = DissolveKeep(input.positionOS, input.uv);
                    clip(keep);
                    edge = saturate(1.0 - keep / _EdgeWidth);
                }

                CrackTerms c = SampleCrack(input.uv, input.positionWS);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                albedo = lerp(albedo, _GrooveColor.rgb, saturate(c.crack * _GrooveDepth));

                half3 colour = Shade(input.positionWS, normalize(input.normalWS), albedo);
                colour += _GlowColor.rgb * (c.crack * _GlowStrength * _GoldIntensity);
                colour += _GlowColor.rgb * (c.tip * _TipBoost * _GoldIntensity);
                colour += _EdgeColor.rgb * (edge * _EdgeStrength);
                return half4(colour, 1);
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

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings o = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                float3 lightDir = _LightDirection;
                #endif

                positionWS = ApplyShadowBias(positionWS, normalWS, lightDir);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.positionOS = input.positionOS.xyz;
                o.uv = input.uv;
                return o;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                if (_Dissolve > 0.0001)
                {
                    clip(DissolveKeep(input.positionOS, input.uv));
                }
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

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma target 3.0
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings o = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.positionOS = input.positionOS.xyz;
                o.uv = input.uv;
                return o;
            }

            half4 DepthFrag(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                if (_Dissolve > 0.0001)
                {
                    clip(DissolveKeep(input.positionOS, input.uv));
                }
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
