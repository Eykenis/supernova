#ifndef SUPERNOVA_SOFT_FALLOFF_LIT_FORWARD_PASS_INCLUDED
#define SUPERNOVA_SOFT_FALLOFF_LIT_FORWARD_PASS_INCLUDED

// Global rather than per-material so the shader remains SRP Batcher compatible.
// x: attenuation exponent, y: near attenuation cap, z: multiplier.
float4 _SupernovaSoftFalloffParams;

half SoftenedPunctualAttenuation(half attenuation)
{
    float falloffPower = _SupernovaSoftFalloffParams.x > 0.0
        ? _SupernovaSoftFalloffParams.x
        : 1.0;
    float attenuationLimit = _SupernovaSoftFalloffParams.y > 0.0
        ? _SupernovaSoftFalloffParams.y
        : HALF_MAX;
    float lightMultiplier = _SupernovaSoftFalloffParams.z > 0.0
        ? _SupernovaSoftFalloffParams.z
        : 1.0;

    float safeAttenuation = max((float)attenuation, 0.0);
    float softened = pow(safeAttenuation, falloffPower) * lightMultiplier;
    return (half)min(softened, attenuationLimit);
}

half4 SoftFalloffUniversalFragmentPBR(
    InputData inputData,
    SurfaceData surfaceData)
{
#if defined(_SPECULARHIGHLIGHTS_OFF)
    bool specularHighlightsOff = true;
#else
    bool specularHighlightsOff = false;
#endif

    BRDFData brdfData;
    InitializeBRDFData(surfaceData, brdfData);

#if defined(DEBUG_DISPLAY)
    half4 debugColor;
    if (CanDebugOverrideOutputColor(
        inputData,
        surfaceData,
        brdfData,
        debugColor))
    {
        return debugColor;
    }
#endif

    BRDFData brdfDataClearCoat =
        CreateClearCoatBRDFData(surfaceData, brdfData);
    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor aoFactor =
        CreateAmbientOcclusionFactor(inputData, surfaceData);
    uint meshRenderingLayers = GetMeshRenderingLayer();
    Light mainLight =
        GetMainLight(inputData, shadowMask, aoFactor);

    MixRealtimeAndBakedGI(
        mainLight,
        inputData.normalWS,
        inputData.bakedGI);

    LightingData lightingData = CreateLightingData(inputData, surfaceData);
    lightingData.giColor = GlobalIllumination(
        brdfData,
        brdfDataClearCoat,
        surfaceData.clearCoatMask,
        inputData.bakedGI,
        aoFactor.indirectAmbientOcclusion,
        inputData.positionWS,
        inputData.normalWS,
        inputData.viewDirectionWS,
        inputData.normalizedScreenSpaceUV
        UNITY_GDRP_INSTANCE_ZERO_ARGUMENT);

#ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
#endif
    {
        lightingData.mainLightColor = LightingPhysicallyBased(
            brdfData,
            brdfDataClearCoat,
            mainLight,
            inputData.normalWS,
            inputData.viewDirectionWS,
            surfaceData.clearCoatMask,
            specularHighlightsOff);
    }

#if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();

#if USE_CLUSTERED_LIGHTING
    // Forward+ puts extra directional lights in this loop. Keep their standard
    // attenuation because this shader only changes point and spot lights.
    for (uint lightIndex = 0;
        lightIndex < min(
            URP_FP_DIRECTIONAL_LIGHTS_COUNT,
            MAX_VISIBLE_LIGHTS);
        lightIndex++)
    {
        FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK

        Light light =
            GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);

#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            lightingData.additionalLightsColor += LightingPhysicallyBased(
                brdfData,
                brdfDataClearCoat,
                light,
                inputData.normalWS,
                inputData.viewDirectionWS,
                surfaceData.clearCoatMask,
                specularHighlightsOff);
        }
    }
#endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light =
            GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
        light.distanceAttenuation =
            SoftenedPunctualAttenuation(light.distanceAttenuation);

#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            lightingData.additionalLightsColor += LightingPhysicallyBased(
                brdfData,
                brdfDataClearCoat,
                light,
                inputData.normalWS,
                inputData.viewDirectionWS,
                surfaceData.clearCoatMask,
                specularHighlightsOff);
        }
    LIGHT_LOOP_END
#endif

#if defined(_ADDITIONAL_LIGHTS_VERTEX)
    lightingData.vertexLightingColor +=
        inputData.vertexLighting * brdfData.diffuse;
#endif

#if REAL_IS_HALF
    return min(
        CalculateFinalColor(lightingData, surfaceData.alpha),
        HALF_MAX);
#else
    return CalculateFinalColor(lightingData, surfaceData.alpha);
#endif
}

void SoftFalloffLitPassFragment(
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

    half4 color =
        SoftFalloffUniversalFragmentPBR(inputData, surfaceData);
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
