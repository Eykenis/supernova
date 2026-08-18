#ifndef SUPERNOVA_CRYSTAL_ORE_LIT_COMPATIBLE_FORWARD_PASS_INCLUDED
#define SUPERNOVA_CRYSTAL_ORE_LIT_COMPATIBLE_FORWARD_PASS_INCLUDED

#include "Assets/Game/Materials/Lighting/SoftFalloffLitForwardPass.hlsl"

float CrystalOreSparkleHash(float3 value)
{
    return frac(
        sin(dot(value, float3(12.9898, 78.233, 37.719)))
        * 43758.5453);
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

    float3 absoluteNormal = abs(normalize(inputData.normalWS));
    float2 facePosition;
    float faceLayer;
    float faceSeed;
    if (absoluteNormal.x >= absoluteNormal.y
        && absoluteNormal.x >= absoluteNormal.z)
    {
        facePosition = inputData.positionWS.zy;
        faceLayer = floor(inputData.positionWS.x * 4.0);
        faceSeed = 11.0;
    }
    else if (absoluteNormal.y >= absoluteNormal.z)
    {
        facePosition = inputData.positionWS.xz;
        faceLayer = floor(inputData.positionWS.y * 4.0);
        faceSeed = 23.0;
    }
    else
    {
        facePosition = inputData.positionWS.xy;
        faceLayer = floor(inputData.positionWS.z * 4.0);
        faceSeed = 37.0;
    }

    float2 sparkleGrid = facePosition * 3.0;
    float2 sparkleCell = floor(sparkleGrid);
    float2 sparkleUv = abs(frac(sparkleGrid) * 2.0 - 1.0);
    float sparkleSelection = CrystalOreSparkleHash(float3(
        sparkleCell + faceSeed,
        faceLayer));
    half selected = step(
        1.0h - saturate((half)_DetailAlbedoMapScale) * 0.34h,
        (half)sparkleSelection);
    half phaseSeed = (half)CrystalOreSparkleHash(float3(
        sparkleCell.yx + faceSeed * 0.37,
        faceLayer + 19.0));
    half phase = frac(
        (half)_Time.y * max((half)_DetailNormalMapScale, 0.05h)
        + phaseSeed);
    half pulse = smoothstep(0.0h, 0.08h, phase)
        * (1.0h - smoothstep(0.28h, 0.48h, phase));
    pulse *= pulse;

    half horizontal = (1.0h - smoothstep(
            0.035h,
            0.11h,
            (half)sparkleUv.y))
        * (1.0h - smoothstep(
            0.62h,
            1.0h,
            (half)sparkleUv.x));
    half vertical = (1.0h - smoothstep(
            0.035h,
            0.11h,
            (half)sparkleUv.x))
        * (1.0h - smoothstep(
            0.62h,
            1.0h,
            (half)sparkleUv.y));
    half core = 1.0h - smoothstep(
        0.04h,
        0.18h,
        (half)length(sparkleUv));
    half crossMask = max(max(horizontal, vertical), core);
    half sparkle = selected * pulse * crossMask;
    half3 sparkleColor = lerp(
        prismColor,
        half3(1.0h, 1.0h, 1.0h),
        saturate(core * 0.82h));
    surfaceData.emission += sparkleColor
        * sparkle
        * (2.2h + crystalDepth * 3.2h);


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
