#ifndef SUPERNOVA_CAVE_BIOME_SURFACE_LIT_FORWARD_PASS_INCLUDED
#define SUPERNOVA_CAVE_BIOME_SURFACE_LIT_FORWARD_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

#if defined(_PARALLAXMAP) && !defined(SHADER_API_GLES)
#define REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR
#endif

#if (defined(_NORMALMAP) \
    || (defined(_PARALLAXMAP) \
        && !defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR))) \
    || defined(_DETAIL)
#define REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR
#endif

// The biome overlay uses world position for seamless macro variation, so this
// interpolator is required even in lighting variants that would omit it.
#ifndef REQUIRES_WORLD_SPACE_POS_INTERPOLATOR
#define REQUIRES_WORLD_SPACE_POS_INTERPOLATOR
#endif

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 texcoord : TEXCOORD0;
    float2 staticLightmapUV : TEXCOORD1;
    float2 dynamicLightmapUV : TEXCOORD2;
    half4 surfaceStyle : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    float3 normalWS : TEXCOORD2;

#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR)
    half4 tangentWS : TEXCOORD3;
#endif

    half4 surfaceStyle : COLOR;

#ifdef _ADDITIONAL_LIGHTS_VERTEX
    half4 fogFactorAndVertexLight : TEXCOORD5;
#else
    half fogFactor : TEXCOORD5;
#endif

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    float4 shadowCoord : TEXCOORD6;
#endif

#if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    half3 viewDirTS : TEXCOORD7;
#endif

    DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 8);
#ifdef DYNAMICLIGHTMAP_ON
    float2 dynamicLightmapUV : TEXCOORD9;
#endif

#if defined(REQUIRES_VERTEX_PROBE_SHADOW_MASK)
    half4 probeShadowMask : TEXCOORD10;
#endif

    float4 positionCS : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

void InitializeInputData(
    Varyings input,
    half3 normalTS,
    out InputData inputData)
{
    inputData = (InputData)0;
    inputData.positionWS = input.positionWS;

    half3 viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
#if defined(_NORMALMAP) || defined(_DETAIL)
    float tangentSign = input.tangentWS.w;
    float3 bitangent = tangentSign * cross(
        input.normalWS.xyz,
        input.tangentWS.xyz);
    half3x3 tangentToWorld = half3x3(
        input.tangentWS.xyz,
        bitangent.xyz,
        input.normalWS.xyz);

#if defined(_NORMALMAP)
    inputData.tangentToWorld = tangentToWorld;
#endif
    inputData.normalWS = TransformTangentToWorld(normalTS, tangentToWorld);
#else
    inputData.normalWS = input.normalWS;
#endif

    inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);
    inputData.viewDirectionWS = viewDirectionWS;

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    inputData.shadowCoord = input.shadowCoord;
#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
#else
    inputData.shadowCoord = float4(0, 0, 0, 0);
#endif

#ifdef _ADDITIONAL_LIGHTS_VERTEX
    inputData.fogCoord = InitializeInputDataFog(
        float4(input.positionWS, 1.0),
        input.fogFactorAndVertexLight.x);
    inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
#else
    inputData.fogCoord = InitializeInputDataFog(
        float4(input.positionWS, 1.0),
        input.fogFactor);
#endif

    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(
        input.positionCS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);

#if defined(DYNAMICLIGHTMAP_ON)
    inputData.bakedGI = SAMPLE_GI(
        input.staticLightmapUV,
        input.dynamicLightmapUV,
        input.vertexSH,
        inputData.normalWS);
#elif defined(USE_PROBE_SYSTEM) && !defined(LIGHTMAP_ON)
#if defined(EVALUATE_SH_VERTEX)
    inputData.bakedGI = input.vertexSH;
#if defined(REQUIRES_VERTEX_PROBE_SHADOW_MASK)
    inputData.shadowMask = input.probeShadowMask;
#else
    inputData.shadowMask = 1.0;
#endif
#else
    inputData.bakedGI = SAMPLE_GI(
        inputData.positionWS,
        inputData.normalWS,
        inputData.normalizedScreenSpaceUV,
        inputData.shadowMask);
#endif
#else
    inputData.bakedGI = SAMPLE_GI(
        input.staticLightmapUV,
        input.vertexSH,
        inputData.normalWS);
#endif

#if defined(DEBUG_DISPLAY)
#if defined(DYNAMICLIGHTMAP_ON)
    inputData.dynamicLightmapUV = input.dynamicLightmapUV;
#endif
#if defined(LIGHTMAP_ON)
    inputData.staticLightmapUV = input.staticLightmapUV;
#else
    inputData.vertexSH = input.vertexSH;
#endif
#endif
}

Varyings CaveBiomeSurfaceLitPassVertex(Attributes input)
{
    Varyings output = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs vertexInput = GetVertexPositionInputs(
        input.positionOS.xyz);
    VertexNormalInputs normalInput = GetVertexNormalInputs(
        input.normalOS,
        input.tangentOS);
    half3 vertexLight = VertexLighting(
        vertexInput.positionWS,
        normalInput.normalWS);

    half fogFactor = 0;
#if !defined(_FOG_FRAGMENT)
    fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
#endif

    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
    output.positionWS = vertexInput.positionWS;
    output.normalWS = normalInput.normalWS;
    output.surfaceStyle = input.surfaceStyle;

#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR) \
    || defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    real tangentSign = input.tangentOS.w * GetOddNegativeScale();
    half4 tangentWS = half4(normalInput.tangentWS.xyz, tangentSign);
#endif
#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR)
    output.tangentWS = tangentWS;
#endif

#if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    half3 viewDirectionWS = GetWorldSpaceNormalizeViewDir(
        vertexInput.positionWS);
    output.viewDirTS = GetViewDirectionTangentSpace(
        tangentWS,
        output.normalWS,
        viewDirectionWS);
#endif

    OUTPUT_LIGHTMAP_UV(
        input.staticLightmapUV,
        unity_LightmapST,
        output.staticLightmapUV);
#ifdef DYNAMICLIGHTMAP_ON
    output.dynamicLightmapUV = input.dynamicLightmapUV.xy
        * unity_DynamicLightmapST.xy
        + unity_DynamicLightmapST.zw;
#endif

#if defined(USE_PROBE_SYSTEM) \
    && defined(EVALUATE_SH_VERTEX) \
    && !defined(LIGHTMAP_ON)
    // Adaptive probe sampling is deferred until positionCS is available below.
#else
    OUTPUT_SH(output.normalWS.xyz, output.vertexSH);
#endif

#ifdef _ADDITIONAL_LIGHTS_VERTEX
    output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
#else
    output.fogFactor = fogFactor;
#endif

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    output.shadowCoord = GetShadowCoord(vertexInput);
#endif
    output.positionCS = vertexInput.positionCS;

#if defined(USE_PROBE_SYSTEM) \
    && defined(EVALUATE_SH_VERTEX) \
    && !defined(LIGHTMAP_ON)
    float2 positionSS = GetNormalizedScreenSpaceUV(output.positionCS);
#if defined(REQUIRES_VERTEX_PROBE_SHADOW_MASK)
    output.vertexSH = SampleAdaptiveProbeSystem(
        vertexInput.positionWS,
        normalInput.normalWS,
        positionSS,
        output.probeShadowMask);
#else
    float4 unusedShadowMask;
    output.vertexSH = SampleAdaptiveProbeSystem(
        vertexInput.positionWS,
        normalInput.normalWS,
        positionSS,
        unusedShadowMask);
#endif
#endif

    return output;
}

half CaveBiomeSurfaceHash(float2 cell)
{
    return frac(sin(dot(cell, float2(127.1, 311.7))) * 43758.5453);
}

half CaveBiomeSurfaceMacroNoise(float2 position)
{
    float2 cell = floor(position);
    float2 fraction = frac(position);
    fraction = fraction * fraction * (3.0 - 2.0 * fraction);
    half first = lerp(
        CaveBiomeSurfaceHash(cell),
        CaveBiomeSurfaceHash(cell + float2(1.0, 0.0)),
        fraction.x);
    half second = lerp(
        CaveBiomeSurfaceHash(cell + float2(0.0, 1.0)),
        CaveBiomeSurfaceHash(cell + float2(1.0, 1.0)),
        fraction.x);
    return lerp(first, second, fraction.y);
}

void InitializeCaveGrassTurfSurface(
    Varyings input,
    out SurfaceData surfaceData)
{
    surfaceData = (SurfaceData)0;
    const half macroScale = 0.32h;
    const half macroVariation = 0.12h;

    half macroNoise = CaveBiomeSurfaceMacroNoise(
        input.positionWS.xz * macroScale);
    half colorVariation = lerp(
        1.0h - macroVariation,
        1.0h + macroVariation,
        macroNoise);
    surfaceData.albedo = saturate(
        input.surfaceStyle.rgb * colorVariation);
    surfaceData.alpha = saturate(input.surfaceStyle.a);
    surfaceData.normalTS = half3(0.0h, 0.0h, 1.0h);
    surfaceData.metallic = 0.0h;
    surfaceData.specular = half3(0.04h, 0.04h, 0.04h);
    surfaceData.smoothness = 0.18h;
    surfaceData.occlusion = 1.0h;
}

// Reuse the project's softened point/spot-light PBR implementation so terrain
// lighting remains consistent with CaveGrassBlade and the other cave materials.
#include "Assets/Game/Materials/Lighting/SoftFalloffLitForwardPass.hlsl"

void CaveBiomeSurfaceLitPassFragment(
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
    half3 viewDirectionTS = input.viewDirTS;
#else
    half3 viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    half3 viewDirectionTS = GetViewDirectionTangentSpace(
        input.tangentWS,
        input.normalWS,
        viewDirectionWS);
#endif
    ApplyPerPixelDisplacement(
        viewDirectionTS,
        input.uv
        UNITY_GDRP_MATERIAL_PAGE_OFFSET_ARGUMENT);
#endif

    SurfaceData surfaceData;
    InitializeCaveGrassTurfSurface(input, surfaceData);
    clip(surfaceData.alpha - 0.005h);

#ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(input.positionCS);
#endif

    InputData inputData;
    InitializeInputData(input, surfaceData.normalTS, inputData);
    SETUP_DEBUG_TEXTURE_DATA(inputData, input.uv, _BaseMap);

#ifdef _DBUFFER
    ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
#endif

    half4 color = SoftFalloffUniversalFragmentPBR(inputData, surfaceData);
    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    color.a = surfaceData.alpha;
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
