Shader "Supernova/Vegetation/Cave Grass Blade"
{
    Properties
    {
        [Header(Colour)]
        _RootColor("Root Colour", Color) = (0.055, 0.184, 0.078, 1)
        _TipColor("Tip Colour", Color) = (0.34, 0.61, 0.208, 1)
        _RimColor("Rim / Backlight Colour", Color) = (0.53, 0.79, 0.35, 1)
        _RootOcclusion("Root Occlusion", Range(0, 1)) = 0.55
        _RimPower("Rim Power", Range(0.5, 16)) = 3
        _RimStrength("Rim Strength", Range(0, 2)) = 0.6
        _TintVariation("Per Clump Tint Variation", Range(0, 1)) = 0.3
        _HueJitter("Per Instance Tint Jitter", Range(0, 1)) = 0.12

        [Header(Wind)]
        _WindStrength("Wind Strength", Range(0, 1)) = 0.16
        _WindFrequency("Wind Frequency", Range(0, 4)) = 0.35
        _WindScrollSpeed("Wind Scroll Speed", Range(0, 4)) = 0.45
        _WindBendExponent("Wind Bend Exponent", Range(1, 6)) = 2
        _WindDirection("Wind Direction (XZ)", Vector) = (1, 0.35, 0, 0)

        [Header(Shape)]
        _HeightJitter("Per Instance Height Jitter", Range(0, 1)) = 0.25
        [Tooltip(Zero uses the blade normal. One inherits the ground normal.)]
        _NormalBlend("Ground Normal Inheritance", Range(0, 1)) = 0.85
        _ClumpCellSize("Clump Cell Size (XY)", Vector) = (2.5, 3, 0, 0)

        [Header(Distance Fade)]
        _FadeStartDistance("Fade Start Distance", Float) = 33
        _FadeEndDistance("Fade End Distance", Float) = 45

        [HideInInspector]
        _SupernovaGrassInteractionParams("Reserved Interaction", Vector) = (0,0,0,0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
            "Queue" = "Geometry"
        }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            ZWrite On
            // Blades are single sided; the fragment shader flips the normal for
            // backfaces, which halves the geometry versus duplicating vertices.
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex CaveGrassBladeVertex
            #pragma fragment CaveGrassBladeFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Assets/Game/Shaders/Vegetation/CaveGrassBladePasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags
            {
                "LightMode" = "ShadowCaster"
            }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex CaveGrassBladeShadowVertex
            #pragma fragment CaveGrassBladeDepthFragment

            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "Assets/Game/Shaders/Vegetation/CaveGrassBladePasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags
            {
                "LightMode" = "DepthOnly"
            }

            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex CaveGrassBladeDepthVertex
            #pragma fragment CaveGrassBladeDepthFragment

            #pragma multi_compile_instancing

            #include "Assets/Game/Shaders/Vegetation/CaveGrassBladePasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags
            {
                "LightMode" = "DepthNormals"
            }

            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex CaveGrassBladeDepthNormalsVertex
            #pragma fragment CaveGrassBladeDepthNormalsFragment

            #pragma multi_compile_instancing

            #include "Assets/Game/Shaders/Vegetation/CaveGrassBladePasses.hlsl"
            ENDHLSL
        }
    }

    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
