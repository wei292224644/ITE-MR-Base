Shader "MRBase/SacredRelic/ShellRadialDissolve"
{
    Properties
    {
        _BaseColor ("Shell Color", Color) = (0.18, 0.14, 0.10, 1)
        _NoiseTex ("Crack Noise", 2D) = "gray" {}
        _NoiseScale ("Noise Scale", Float) = 3.5
        _NoiseStrength ("Noise Strength", Range(0, 0.5)) = 0.22
        _DissolveAmount ("Dissolve Amount", Range(0, 1.25)) = 0
        _EdgeColor ("Edge Gold", Color) = (8, 4.2, 0.9, 1)
        _EdgeWidth ("Edge Width", Range(0.01, 0.35)) = 0.12
        _RadialScale ("Radial Scale", Float) = 1.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _NoiseTex_ST;
                float4 _EdgeColor;
                float _NoiseScale;
                float _NoiseStrength;
                float _DissolveAmount;
                float _EdgeWidth;
                float _RadialScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = TRANSFORM_TEX(input.uv, _NoiseTex);
                o.positionOS = input.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Radial from object center on the tablet face (XY). 0 = center, 1 = outer.
                float2 face = input.positionOS.xy * _RadialScale;
                float radial = length(face);

                float noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, input.uv * _NoiseScale).r;
                noise = (noise - 0.5) * 2.0 * _NoiseStrength;

                // Dissolve expands from center: when radial < threshold, shell is gone.
                float threshold = _DissolveAmount + noise;
                float edge = threshold + _EdgeWidth;

                clip(radial - threshold);

                half3 col = _BaseColor.rgb;
                // Gold rim near the crack frontier
                float edgeFactor = 1.0 - saturate((radial - threshold) / max(_EdgeWidth, 1e-4));
                edgeFactor = pow(edgeFactor, 1.5);
                col = lerp(col, _EdgeColor.rgb, edgeFactor);

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
