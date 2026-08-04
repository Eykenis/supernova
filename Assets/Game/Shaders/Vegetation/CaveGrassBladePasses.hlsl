#ifndef SUPERNOVA_CAVE_GRASS_BLADE_PASSES_INCLUDED
#define SUPERNOVA_CAVE_GRASS_BLADE_PASSES_INCLUDED

#include "Assets/Game/Shaders/Vegetation/CaveGrassBladeInput.hlsl"

// Extracts the instance origin in world space. Every blade in an instance shares
// it, which is what makes the per-instance hashes and the wind phase coherent.
float3 GrassInstanceOriginWS()
{
    return float3(
        UNITY_MATRIX_M[0][3],
        UNITY_MATRIX_M[1][3],
        UNITY_MATRIX_M[2][3]);
}

// The stance normal is baked into the instance matrix by the placement pass, so
// the instance's local up is the (upright-biased) ground normal. Shading with it
// instead of the blade's own geometric normal is the Breath of the Wild trick:
// a whole patch shades as one continuous surface and stops shimmering over the
// curvature of the marching-cubes terrain.
half3 GrassStanceNormalWS()
{
    return normalize(half3(
        UNITY_MATRIX_M[0][1],
        UNITY_MATRIX_M[1][1],
        UNITY_MATRIX_M[2][1]));
}

GrassVaryings CaveGrassBladeVertex(GrassAttributes input)
{
    GrassVaryings output = (GrassVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float3 instanceOriginWS = GrassInstanceOriginWS();
    half heightRatio = (half)input.bladeUv.y;

    half tintOffset;
    GrassAnimateVertex(
        positionWS,
        instanceOriginWS,
        heightRatio,
        (half)input.bladeData.x,
        (half)input.bladeData.y,
        tintOffset);

    half3 bladeNormalWS = normalize(
        (half3)TransformObjectToWorldNormal(input.normalOS));
    half3 stanceNormalWS = GrassStanceNormalWS();

    output.positionWS = positionWS;
    output.positionCS = TransformWorldToHClip(positionWS);
    output.normalWS = normalize(
        lerp(bladeNormalWS, stanceNormalWS, _NormalBlend));
    output.shading = half2(heightRatio, tintOffset);
    output.fogFactor = (half)ComputeFogFactor(output.positionCS.z);
    return output;
}

/// Accumulates punctual lighting for one light using a wrapped diffuse term.
/// Grass is thin, so hard N.L makes a blade field read as noise; wrapping the
/// term and adding a translucent back-lobe approximates light passing through.
half3 GrassShadeLight(Light light, half3 normalWS, half3 viewDirectionWS)
{
    half attenuation = SoftenedPunctualAttenuation(
        (half)light.distanceAttenuation * light.shadowAttenuation);
    half wrapped = saturate(dot(normalWS, light.direction) * 0.5 + 0.5);

    // Thin-material translucency: light arriving from behind the blade.
    half backlight = saturate(dot(-viewDirectionWS, light.direction));
    half rim = pow(backlight, max(_RimPower, 1e-3)) * _RimStrength;

    return light.color * attenuation * (wrapped + rim * _RimColor.rgb);
}

half4 CaveGrassBladeFragment(GrassVaryings input, half facing : VFACE) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);

    // Blades are single sided and drawn with Cull Off, so flip the normal for
    // backfaces rather than paying for duplicated geometry.
    half3 normalWS = normalize(input.normalWS) * (facing >= 0.0 ? 1.0 : -1.0);
    half3 viewDirectionWS = (half3)normalize(
        GetWorldSpaceViewDir(input.positionWS));

    half heightRatio = saturate(input.shading.x);
    half tintOffset = input.shading.y;

    // Root-to-tip gradient, offset per instance/clump so a field is not uniform.
    half gradient = saturate(heightRatio + tintOffset);
    half3 albedo = lerp(_RootColor.rgb, _TipColor.rgb, gradient);

    // Darken the base to fake the self-shadowing of a dense canopy. This is what
    // stops a grass field from looking like a flat sheet of colour.
    half occlusion = lerp(1.0 - _RootOcclusion, 1.0, heightRatio);

    half3 lighting = SampleSH(normalWS);

    Light mainLight = GetMainLight();
    lighting += GrassShadeLight(mainLight, normalWS, viewDirectionWS);

    // Forward rendering hands punctual lights to a renderer through
    // unity_LightIndices, which Unity fills per renderer during culling.
    // Graphics.DrawMeshInstanced has no renderer entry, so unity_LightData.y is
    // zero and GetAdditionalLightsCount() returns nothing -- instanced grass
    // would be lit by ambient alone. Walk the global light arrays directly
    // instead; GetAdditionalPerObjectLight takes a raw global index, so URP's own
    // light unpacking and attenuation are reused rather than reimplemented.
    // See Assets/Game/Docs/洞穴草地渲染.md.
    int visibleLightCount = min((int)_AdditionalLightsCount.x, MAX_VISIBLE_LIGHTS);
    for (int lightIndex = 0; lightIndex < visibleLightCount; lightIndex++)
    {
        Light light = GetAdditionalPerObjectLight(lightIndex, input.positionWS);
        lighting += GrassShadeLight(light, normalWS, viewDirectionWS);
    }

    half3 color = albedo * lighting * occlusion;
    color = MixFog(color, input.fogFactor);
    return half4(color, 1.0);
}

// Depth-only variants share the vertex animation so the depth, shadow and colour
// passes agree on where each blade actually is. Without this the blades would
// self-shadow and depth-test against their unanimated positions.
struct GrassDepthVaryings
{
    float4 positionCS : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

float3 CaveGrassBladeAnimatedPositionWS(GrassAttributes input)
{
    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    half tintOffset;
    GrassAnimateVertex(
        positionWS,
        GrassInstanceOriginWS(),
        (half)input.bladeUv.y,
        (half)input.bladeData.x,
        (half)input.bladeData.y,
        tintOffset);
    return positionWS;
}

GrassDepthVaryings CaveGrassBladeDepthVertex(GrassAttributes input)
{
    GrassDepthVaryings output = (GrassDepthVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    output.positionCS = TransformWorldToHClip(
        CaveGrassBladeAnimatedPositionWS(input));
    return output;
}

half4 CaveGrassBladeDepthFragment(GrassDepthVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    return 0;
}

float3 _LightDirection;
float3 _LightPosition;

GrassDepthVaryings CaveGrassBladeShadowVertex(GrassAttributes input)
{
    GrassDepthVaryings output = (GrassDepthVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float3 positionWS = CaveGrassBladeAnimatedPositionWS(input);
    half3 normalWS = GrassStanceNormalWS();

#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif

    positionWS = ApplyShadowBias(positionWS, normalWS, lightDirectionWS);
    output.positionCS = TransformWorldToHClip(positionWS);

#if UNITY_REVERSED_Z
    output.positionCS.z = min(
        output.positionCS.z,
        UNITY_NEAR_CLIP_VALUE >= 0 ? output.positionCS.w : output.positionCS.z);
#else
    output.positionCS.z = max(
        output.positionCS.z,
        UNITY_NEAR_CLIP_VALUE >= 0 ? output.positionCS.w : output.positionCS.z);
#endif
    return output;
}

// DepthNormals feeds screen-space ambient occlusion; omitting it would make the
// grass punch holes in the AO buffer.
struct GrassDepthNormalsVaryings
{
    float4 positionCS : SV_POSITION;
    half3 normalWS    : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

GrassDepthNormalsVaryings CaveGrassBladeDepthNormalsVertex(GrassAttributes input)
{
    GrassDepthNormalsVaryings output = (GrassDepthNormalsVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    output.positionCS = TransformWorldToHClip(
        CaveGrassBladeAnimatedPositionWS(input));
    output.normalWS = normalize(lerp(
        normalize((half3)TransformObjectToWorldNormal(input.normalOS)),
        GrassStanceNormalWS(),
        _NormalBlend));
    return output;
}

half4 CaveGrassBladeDepthNormalsFragment(
    GrassDepthNormalsVaryings input,
    half facing : VFACE) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    half3 normalWS = normalize(input.normalWS) * (facing >= 0.0 ? 1.0 : -1.0);
    return half4(normalWS, 0.0);
}

#endif
