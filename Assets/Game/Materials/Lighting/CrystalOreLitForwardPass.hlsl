#ifndef SUPERNOVA_CRYSTAL_ORE_LIT_FORWARD_PASS_INCLUDED
#define SUPERNOVA_CRYSTAL_ORE_LIT_FORWARD_PASS_INCLUDED

#include "Assets/Game/Materials/Lighting/SoftFalloffLitForwardPass.hlsl"

#define CRYSTAL_ORE_SPARKLE_MARKER 4.0h

float CrystalOreSparkleHash(float3 value)
{
    return frac(
        sin(dot(value, float3(12.9898, 78.233, 37.719)))
        * 43758.5453);
}

Varyings BuildCrystalOreSparkleVertex(
    Varyings source,
    float3 positionWS,
    float2 sparkleUv,
    half pulse)
{
    Varyings output = source;
    output.positionCS = TransformWorldToHClip(positionWS);
    output.positionWS = positionWS;
    output.uv = sparkleUv;
    output.normalWS = half3(
        pulse,
        0.0h,
        CRYSTAL_ORE_SPARKLE_MARKER);
    return output;
}

[maxvertexcount(7)]
void CrystalOreGeometry(
    triangle Varyings input[3],
    uint primitiveID : SV_PrimitiveID,
    inout TriangleStream<Varyings> outputStream)
{
    outputStream.Append(input[0]);
    outputStream.Append(input[1]);
    outputStream.Append(input[2]);
    outputStream.RestartStrip();

    float3 centreWS = (
        input[0].positionWS
        + input[1].positionWS
        + input[2].positionWS) / 3.0;
    float primitiveValue = (float)primitiveID;
    float3 stableCell = floor(centreWS * 4.0);
    float selection = CrystalOreSparkleHash(
        stableCell
        + primitiveValue * float3(0.731, 1.137, 1.913));
    float sparkleDensity = saturate(_DetailAlbedoMapScale);
    if (sparkleDensity <= 0.0 || selection > sparkleDensity)
    {
        return;
    }

    float phaseSeed = CrystalOreSparkleHash(
        stableCell.zyx
        + primitiveValue * float3(2.417, 0.673, 1.291));
    float phase = frac(
        _Time.y * max(_DetailNormalMapScale, 0.05)
        + phaseSeed);
    half pulse = (half)(
        smoothstep(0.0, 0.08, phase)
        * (1.0 - smoothstep(0.28, 0.48, phase)));
    pulse *= pulse;
    if (pulse < 0.015h)
    {
        return;
    }

    float sizeVariation = lerp(
        0.72,
        1.18,
        CrystalOreSparkleHash(
            stableCell.yxz
            + primitiveValue * float3(1.619, 2.231, 0.419)));
    float halfSize = max(_ClearCoatSmoothness, 0.01)
        * sizeVariation;
    float3 viewDirectionWS = SafeNormalize(
        GetCameraPositionWS() - centreWS);
    float3 cameraRightWS = SafeNormalize(UNITY_MATRIX_V[0].xyz);
    float3 cameraUpWS = SafeNormalize(UNITY_MATRIX_V[1].xyz);
    float3 sparkleCentreWS = centreWS
        + viewDirectionWS * min(halfSize * 0.12, 0.018);

    // Unity treats clockwise triangles as front-facing. Emit the
    // billboard in clockwise strip order so ore back-face culling
    // does not discard every sparkle.
    outputStream.Append(BuildCrystalOreSparkleVertex(
        input[0],
        sparkleCentreWS - cameraRightWS * halfSize
            - cameraUpWS * halfSize,
        float2(0.0, 0.0),
        pulse));
    outputStream.Append(BuildCrystalOreSparkleVertex(
        input[0],
        sparkleCentreWS - cameraRightWS * halfSize
            + cameraUpWS * halfSize,
        float2(0.0, 1.0),
        pulse));
    outputStream.Append(BuildCrystalOreSparkleVertex(
        input[0],
        sparkleCentreWS + cameraRightWS * halfSize
            - cameraUpWS * halfSize,
        float2(1.0, 0.0),
        pulse));
    outputStream.Append(BuildCrystalOreSparkleVertex(
        input[0],
        sparkleCentreWS + cameraRightWS * halfSize
            + cameraUpWS * halfSize,
        float2(1.0, 1.0),
        pulse));
    outputStream.RestartStrip();
}

half4 CrystalOreSparkleFragment(float2 uv, half pulse)
{
    float2 distanceFromCentre = abs(uv * 2.0 - 1.0);
    float horizontalTaper = smoothstep(0.0, 1.0,
        distanceFromCentre.x);
    float verticalTaper = smoothstep(0.0, 1.0,
        distanceFromCentre.y);
    float horizontalHalfWidth = lerp(
        0.09,
        0.004,
        horizontalTaper);
    float verticalHalfWidth = lerp(
        0.09,
        0.004,
        verticalTaper);
    half horizontal = (half)(
        (1.0 - smoothstep(
            horizontalHalfWidth * 0.32,
            horizontalHalfWidth,
            distanceFromCentre.y))
        * (1.0 - smoothstep(
            0.78,
            1.0,
            distanceFromCentre.x)));
    half vertical = (half)(
        (1.0 - smoothstep(
            verticalHalfWidth * 0.32,
            verticalHalfWidth,
            distanceFromCentre.x))
        * (1.0 - smoothstep(
            0.78,
            1.0,
            distanceFromCentre.y)));
    half core = (half)(
        1.0 - smoothstep(
            0.045,
            0.15,
            length(distanceFromCentre)));
    half sparkleMask = max(max(horizontal, vertical), core);
    half sparkleAlpha = saturate(sparkleMask * pulse);
    clip(sparkleAlpha - 0.002h);

    half3 glow = max(_EmissionColor.rgb, half3(0.02h, 0.02h, 0.02h));
    half glowPeak = max(max(glow.r, glow.g), glow.b);
    half3 hue = glow / max(glowPeak, 0.001h);
    half whiteCore = saturate(core * 0.82h);
    half3 sparkleColor = lerp(hue, half3(1.0h, 1.0h, 1.0h), whiteCore);
    half energy = 2.2h
        + saturate(_ClearCoatMask) * 3.2h
        + min(glowPeak, 2.0h) * 0.75h;
    half3 premultipliedColor =
        sparkleColor * energy * sparkleAlpha;
    return half4(premultipliedColor, sparkleAlpha);
}

void ApplyCrystalOreSurface(
    InputData inputData,
    inout SurfaceData surfaceData)
{
    half3 normalWS = normalize(inputData.normalWS);
    half3 viewDirectionWS = normalize(inputData.viewDirectionWS);
    half normalDotView = saturate(dot(normalWS, viewDirectionWS));
    half fresnel = pow(1.0h - normalDotView, 3.0h);
    half centreTransmission = normalDotView * normalDotView;
    half crystalDepth = saturate(_ClearCoatMask);

    half3 glowColor = max(
        _EmissionColor.rgb,
        surfaceData.albedo * 0.35h);
    half prismAmount = saturate(
        fresnel * 0.32h
        + sin(dot(inputData.positionWS, float3(1.7, 2.3, 1.1)))
            * 0.035h);
    half3 prismColor = lerp(glowColor, glowColor.gbr, prismAmount);

    surfaceData.albedo = lerp(
        surfaceData.albedo,
        sqrt(max(surfaceData.albedo * prismColor, 0.0h)),
        crystalDepth * 0.2h);
    surfaceData.smoothness = saturate(lerp(
        surfaceData.smoothness,
        0.96h,
        crystalDepth * 0.65h));

    half internalGlow = crystalDepth
        * (0.035h + centreTransmission * 0.13h + fresnel * 0.36h);
    surfaceData.emission += prismColor * internalGlow;


}

void CrystalOreLitPassFragment(
    Varyings input,
    out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out float4 outRenderingLayers : SV_Target1
#endif
)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    if (input.normalWS.z > 2.0h)
    {
        outColor = CrystalOreSparkleFragment(
            input.uv,
            input.normalWS.x);
#ifdef _WRITE_RENDERING_LAYERS
        uint sparkleRenderingLayers = GetMeshRenderingLayer();
        outRenderingLayers = float4(
            EncodeMeshRenderingLayer(sparkleRenderingLayers),
            0,
            0,
            0);
#endif
        return;
    }

#if defined(_PARALLAXMAP)
#if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    half3 viewDirTS = input.viewDirTS;
#else
    half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    half3 viewDirTS = GetViewDirectionTangentSpace(
        input.tangentWS,
        input.normalWS,
        viewDirWS);
#endif
    ApplyPerPixelDisplacement(
        viewDirTS,
        input.uv
        UNITY_GDRP_MATERIAL_PAGE_OFFSET_ARGUMENT);
#endif

    SurfaceData surfaceData;
    InitializeStandardLitSurfaceData(input.uv, surfaceData);

#ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(input.positionCS);
#endif

    InputData inputData;
    InitializeInputData(input, surfaceData.normalTS, inputData);
    SETUP_DEBUG_TEXTURE_DATA(inputData, input.uv, _BaseMap);

#ifdef _DBUFFER
    ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
#endif

    ApplyCrystalOreSurface(inputData, surfaceData);
    half4 color = SoftFalloffUniversalFragmentPBR(inputData, surfaceData);
    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    color.a = OutputAlpha(
        color.a,
        IsSurfaceTypeTransparent(_Surface));
    outColor = color;

#ifdef _WRITE_RENDERING_LAYERS
    uint renderingLayers = GetMeshRenderingLayer();
    outRenderingLayers = float4(
        EncodeMeshRenderingLayer(renderingLayers),
        0,
        0,
        0);
#endif
}

#endif
