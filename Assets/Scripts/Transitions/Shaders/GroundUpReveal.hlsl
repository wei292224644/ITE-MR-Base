#ifndef MRBASE_GROUND_UP_REVEAL_INCLUDED
#define MRBASE_GROUND_UP_REVEAL_INCLUDED

// Intentionally global rather than UnityPerMaterial: one controller moves the front for every
// participating renderer without cloning materials or breaking SRP batching.
float _MRVT_TransitionEnabled;
float _MRVT_RevealHeight;
float _MRVT_NoiseScale;
float _MRVT_NoiseStrength;
float _MRVT_EdgeWidth;
float4 _MRVT_EdgeColor;
float _MRVT_EdgeIntensity;
float _MRVT_GridScale;
float _MRVT_GridWidth;
float _MRVT_GridIntensity;
float _MRVT_Time;

float MRBaseRevealHash(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float MRBaseRevealNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);

    float a = MRBaseRevealHash(i);
    float b = MRBaseRevealHash(i + float2(1.0, 0.0));
    float c = MRBaseRevealHash(i + float2(0.0, 1.0));
    float d = MRBaseRevealHash(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float MRBaseRevealKeep(float3 positionWS)
{
    float2 noiseUV = positionWS.xz * max(_MRVT_NoiseScale, 0.001);
    float noise = MRBaseRevealNoise(noiseUV) * 0.68
                + MRBaseRevealNoise(noiseUV * 2.07 + 17.31) * 0.32;
    noise = noise * 2.0 - 1.0;
    return _MRVT_RevealHeight + noise * _MRVT_NoiseStrength - positionWS.y;
}

float MRBaseRevealGrid2D(float2 coordinates)
{
    float2 gridUV = coordinates * max(_MRVT_GridScale, 0.001);
    float2 cell = abs(frac(gridUV - 0.5) - 0.5);
    float2 derivatives = max(fwidth(gridUV), float2(0.0001, 0.0001));
    float2 lines = cell / (derivatives * max(_MRVT_GridWidth, 0.01));
    return 1.0 - saturate(min(lines.x, lines.y));
}

float MRBaseRevealGrid(float3 positionWS, float3 normalWS)
{
    float3 axis = abs(normalWS);
    if (axis.y >= axis.x && axis.y >= axis.z)
        return MRBaseRevealGrid2D(positionWS.xz);
    if (axis.x >= axis.z)
        return MRBaseRevealGrid2D(positionWS.zy);
    return MRBaseRevealGrid2D(positionWS.xy);
}

void MRBaseGroundUpReveal_float(
    float3 positionWS,
    float3 normalWS,
    out float keep,
    out float edge,
    out float grid)
{
    if (_MRVT_TransitionEnabled < 0.5)
    {
        keep = 1.0;
        edge = 0.0;
        grid = 0.0;
        return;
    }

    keep = MRBaseRevealKeep(positionWS);
    float width = max(_MRVT_EdgeWidth, 0.0001);
    edge = 1.0 - smoothstep(0.0, width, keep);

    float trail = 1.0 - smoothstep(width, width * 5.0, keep);
    float pulse = 0.8 + 0.2 * sin(_MRVT_Time * 5.0 - positionWS.y * 3.0);
    grid = MRBaseRevealGrid(positionWS, normalize(normalWS)) * trail * pulse;
}

void MRBaseGroundUpReveal_half(
    half3 positionWS,
    half3 normalWS,
    out half keep,
    out half edge,
    out half grid)
{
    float keepFloat;
    float edgeFloat;
    float gridFloat;
    MRBaseGroundUpReveal_float(positionWS, normalWS, keepFloat, edgeFloat, gridFloat);
    keep = (half)keepFloat;
    edge = (half)edgeFloat;
    grid = (half)gridFloat;
}

#endif
