#ifndef SUPERNOVA_CAVE_GRASS_BLADE_INPUT_INCLUDED
#define SUPERNOVA_CAVE_GRASS_BLADE_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Assets/Game/Materials/Lighting/SoftFalloffAttenuation.hlsl"

// Declared as plain uniforms rather than inside a UnityPerMaterial constant
// buffer on purpose. Grass is submitted through Graphics.DrawMeshInstanced, which
// does not go through the SRP Batcher, and the renderer overrides the colour and
// wind values per (brush, biome) group with a MaterialPropertyBlock. Properties
// living in UnityPerMaterial are not reliably overridable that way.
half4 _RootColor;
half4 _TipColor;
half4 _RimColor;
half _RootOcclusion;
half _RimPower;
half _RimStrength;
half _TintVariation;
half _HueJitter;
half _WindStrength;
half _WindFrequency;
half _WindScrollSpeed;
half _WindBendExponent;
half4 _WindDirection;
half _HeightJitter;
half _NormalBlend;
float _FadeStartDistance;
float _FadeEndDistance;
// xy: horizontal and vertical clump cell size in world units. Blades sharing a
// cell share a tint, producing visible colour patches rather than per-blade noise.
float4 _ClumpCellSize;

// Reserved for a future interaction pass (trample, cut, burn). Declared so the
// material layout is stable, deliberately unused for now.
float4 _SupernovaGrassInteractionParams;

struct GrassAttributes
{
    float4 positionOS   : POSITION;
    float3 normalOS     : NORMAL;
    // x: blade width ratio, y: root-to-tip ratio.
    float2 bladeUv      : TEXCOORD0;
    // x: blade index within the instance, y: that blade's height multiplier.
    float2 bladeData    : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct GrassVaryings
{
    float4 positionCS   : SV_POSITION;
    float3 positionWS   : TEXCOORD0;
    half3 normalWS      : TEXCOORD1;
    // x: root-to-tip ratio, y: per-instance tint offset.
    half2 shading       : TEXCOORD2;
    half fogFactor      : TEXCOORD3;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

// Integer bit-mix hash. Deliberately not frac(sin(dot(...))): that visibly bands
// once world coordinates reach the thousands, which this cave world does.
uint GrassHashUint(uint value)
{
    value ^= value >> 16;
    value *= 0x7FEB352DU;
    value ^= value >> 15;
    value *= 0x846CA68BU;
    value ^= value >> 16;
    return value;
}

// Stable 0..1 value per instance, derived from the quantised instance origin so
// it is identical from any camera and continuous across chunk boundaries.
half GrassInstanceHash(float3 instanceOriginWS, uint channel)
{
    int3 quantised = int3(floor(instanceOriginWS * 16.0));
    uint hash = GrassHashUint((uint)quantised.x * 73856093U
        ^ (uint)quantised.y * 19349663U
        ^ (uint)quantised.z * 83492791U
        ^ GrassHashUint(channel * 2654435761U));
    return (half)(hash & 0x00FFFFFFU) / (half)0x01000000U;
}

// Stable 0..1 value shared by every instance inside one clump cell, matching the
// cell quantisation CaveSurfaceClumpField uses on the CPU. Sharing the tint across
// a cell is what makes colour read as patches of grass rather than per-blade noise.
half GrassClumpHash(float3 positionWS, uint channel)
{
    float horizontal = max(_ClumpCellSize.x, 0.05);
    float vertical = max(_ClumpCellSize.y, 0.05);
    int3 cell = int3(floor(float3(
        positionWS.x / horizontal,
        positionWS.y / vertical,
        positionWS.z / horizontal)));
    uint hash = GrassHashUint((uint)cell.x * 73856093U
        ^ (uint)cell.y * 19349663U
        ^ (uint)cell.z * 83492791U
        ^ GrassHashUint(channel * 2654435761U));
    return (half)(hash & 0x00FFFFFFU) / (half)0x01000000U;
}

// Two-octave value noise over world XZ, scrolled along the wind direction.
half GrassWindField(float2 worldXz, half phaseOffset)
{
    float2 scroll = _WindDirection.xy * (_WindScrollSpeed * _Time.y);
    float2 sample = worldXz * _WindFrequency - scroll;
    half primary = sin(sample.x + sample.y * 0.7 + phaseOffset * TWO_PI);
    half secondary = sin(sample.x * 2.3 - sample.y * 1.7
        + phaseOffset * TWO_PI * 1.7);
    return primary * 0.65 + secondary * 0.35;
}

/// Applies wind, per-instance variation and the distance height fade.
/// Root vertices (heightRatio == 0) are never displaced, so blades stay rooted.
void GrassAnimateVertex(
    inout float3 positionWS,
    float3 instanceOriginWS,
    half heightRatio,
    half bladeIndex,
    half bladeHeightScale,
    out half tintOffset)
{
    half heightHash = GrassInstanceHash(instanceOriginWS, 1u);
    half phaseHash = GrassInstanceHash(instanceOriginWS, 2u);

    // Per-instance and per-blade height variation, applied about the root.
    half heightScale = 1.0 + (heightHash * 2.0 - 1.0) * _HeightJitter;

    // Distance fade shrinks blades to nothing instead of popping them out.
    float viewDistance = distance(instanceOriginWS, _WorldSpaceCameraPos);
    float fadeRange = max(_FadeEndDistance - _FadeStartDistance, 1e-4);
    half fade = saturate(1.0 - (viewDistance - _FadeStartDistance) / fadeRange);
    heightScale *= fade;

    float3 rootWS = instanceOriginWS;
    float3 offsetFromRoot = positionWS - rootWS;
    offsetFromRoot *= heightScale;

    // Bend weight keeps the base stiff and concentrates motion at the tip.
    half bendWeight = pow(max(heightRatio, 0.0), _WindBendExponent);
    half bladePhase = phaseHash + bladeIndex * 0.137;
    half wind = GrassWindField(rootWS.xz, bladePhase);
    half bend = wind * _WindStrength * bendWeight * bladeHeightScale * fade;

    offsetFromRoot.xz += _WindDirection.xy * bend;

    // Bending along an arc would stretch the blade; pull the tip down by the
    // horizontal excursion so its length stays roughly constant.
    offsetFromRoot.y -= abs(bend) * bendWeight * 0.35;

    positionWS = rootWS + offsetFromRoot;

    // Coarse tint varies per clump so patches share a colour; the finer jitter is
    // per instance so individual blades inside a patch still differ.
    half clumpTint = GrassClumpHash(rootWS, 3u) * 2.0 - 1.0;
    half instanceTint = GrassInstanceHash(instanceOriginWS, 4u) * 2.0 - 1.0;
    tintOffset = clumpTint * _TintVariation + instanceTint * _HueJitter;
}

#endif
