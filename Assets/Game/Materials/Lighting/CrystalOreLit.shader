Shader "Supernova/Lighting/Crystal Ore Lit"
{
    Properties
    {
        [HideInInspector] _WorkflowMode("Workflow Mode", Float) = 1.0

        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5

        _Smoothness("Surface Smoothness", Range(0.0, 1.0)) = 0.85
        [HideInInspector] _SmoothnessTextureChannel(
            "Smoothness Texture Channel",
            Float) = 0
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        _MetallicGlossMap("Metallic / Smoothness", 2D) = "white" {}

        _SpecColor("Specular", Color) = (0.2, 0.2, 0.2)
        _SpecGlossMap("Specular / Smoothness", 2D) = "white" {}
        [ToggleOff] _SpecularHighlights("Specular Highlights", Float) = 1.0
        [ToggleOff] _EnvironmentReflections(
            "Environment Reflections",
            Float) = 1.0

        _BumpScale("Normal Strength", Float) = 1.0
        _BumpMap("Normal Map", 2D) = "bump" {}
        _Parallax("Parallax Strength", Range(0.005, 0.08)) = 0.005
        _ParallaxMap("Height Map", 2D) = "black" {}
        _OcclusionStrength("Occlusion Strength", Range(0.0, 1.0)) = 1.0
        _OcclusionMap("Occlusion", 2D) = "white" {}

        [HDR] _EmissionColor("Crystal Glow Color", Color) = (0.1,0.4,0.8,1)
        _EmissionMap("Emission", 2D) = "white" {}

        _ClearCoatMask("Crystal Depth", Range(0.0, 1.0)) = 0.65
        _ClearCoatSmoothness(
            "Sparkle Size",
            Range(0.02, 0.24)) = 0.11
        _DetailAlbedoMapScale(
            "Sparkle Density",
            Range(0.0, 1.0)) = 0.56
        _DetailNormalMapScale(
            "Sparkle Speed",
            Range(0.1, 1.2)) = 0.42

        [HideInInspector] _DetailMask("Detail Mask", 2D) = "white" {}
        [HideInInspector] _DetailAlbedoMap("Detail Albedo", 2D) = "linearGrey" {}
        [HideInInspector] _DetailNormalMap("Detail Normal", 2D) = "bump" {}

        [HideInInspector] _Surface("__surface", Float) = 0.0
        [HideInInspector] _Blend("__blend", Float) = 0.0
        [HideInInspector] _Cull("__cull", Float) = 2.0
        [HideInInspector][ToggleUI] _AlphaClip("__clip", Float) = 0.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [HideInInspector] _BlendModePreserveSpecular(
            "_BlendModePreserveSpecular",
            Float) = 1.0
        [HideInInspector] _AlphaToMask("__alphaToMask", Float) = 0.0
        [HideInInspector][ToggleUI] _ReceiveShadows(
            "Receive Shadows",
            Float) = 1.0
        [HideInInspector] _QueueOffset("Queue Offset", Float) = 0.0

        [HideInInspector] _MainTex("Base Map", 2D) = "white" {}
        [HideInInspector] _Color("Base Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _GlossMapScale("Smoothness", Float) = 0.0
        [HideInInspector] _Glossiness("Smoothness", Float) = 0.0
        [HideInInspector] _GlossyReflections(
            "Environment Reflections",
            Float) = 0.0

        [HideInInspector][NoScaleOffset] unity_Lightmaps(
            "unity_Lightmaps",
            2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_LightmapsInd(
            "unity_LightmapsInd",
            2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_ShadowMasks(
            "unity_ShadowMasks",
            2DArray) = "" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "AlphaTest+50"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // The ore body remains opaque (alpha 1). Premultiplied alpha
            // lets geometry-generated sparkle arms fade without dark rims.
            Blend One OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite[_ZWrite]
            Cull[_Cull]
            AlphaToMask[_AlphaToMask]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma require geometry
            #pragma vertex LitPassVertex
            #pragma geometry CrystalOreGeometry
            #pragma fragment CrystalOreLitPassFragment

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _PARALLAXMAP
            #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
            #pragma shader_feature_local_fragment _OCCLUSIONMAP
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF
            #pragma shader_feature_local_fragment _SPECULAR_SETUP

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #define _REFLECTION_PROBE_BLENDING 1
            #define _REFLECTION_PROBE_BOX_PROJECTION 1

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
            #include "Assets/Game/Materials/Lighting/CrystalOreLitForwardPass.hlsl"
            ENDHLSL
        }

        UsePass "Supernova/Lighting/Soft Falloff Lit/ShadowCaster"
        UsePass "Supernova/Lighting/Soft Falloff Lit/DepthOnly"
        UsePass "Supernova/Lighting/Soft Falloff Lit/DepthNormals"
        UsePass "Supernova/Lighting/Soft Falloff Lit/Meta"
        UsePass "Supernova/Lighting/Soft Falloff Lit/Universal2D"
    }

    // Some player graphics backends strip the custom forward pass even when
    // the editor's active D3D11 compiler accepts it. Keep a complete,
    // geometry-free fallback subshader so voxel ores never resolve to the
    // magenta error shader in packaged players.
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "Queue" = "Geometry"
        }

        LOD 200

        UsePass "Supernova/Lighting/Soft Falloff Lit/ForwardLit"
        UsePass "Supernova/Lighting/Soft Falloff Lit/ShadowCaster"
        UsePass "Supernova/Lighting/Soft Falloff Lit/DepthOnly"
        UsePass "Supernova/Lighting/Soft Falloff Lit/DepthNormals"
        UsePass "Supernova/Lighting/Soft Falloff Lit/Meta"
        UsePass "Supernova/Lighting/Soft Falloff Lit/Universal2D"
    }

    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}

