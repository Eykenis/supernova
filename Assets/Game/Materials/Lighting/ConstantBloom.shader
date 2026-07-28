Shader "Supernova/Lighting/Constant Bloom"
{
    Properties
    {
        [HDR] _GlowColor("Glow Color", Color) = (1, 1, 1, 1)
        _GlowIntensity("Glow Intensity", Range(0, 20)) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ConstantBloom"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _GlowColor;
                half _GlowIntensity;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(_GlowColor.rgb * _GlowIntensity, _GlowColor.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
