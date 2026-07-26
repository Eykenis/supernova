Shader "Supernova/UI/PauseSilhouette"
{
    Properties
    {
        _Color ("Silhouette Color", Color) = (1, 1, 1, 1)
        _OutlineColor ("Outline Color", Color) = (0.04, 0.03, 0.04, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.03)) = 0.008
        [NoScaleOffset] _MainTex ("Face Detail Texture", 2D) = "white" {}
        _FeatureColor ("Face Feature Color", Color) = (0.04, 0.03, 0.04, 1)
        _FeatureThreshold ("Face Feature Threshold", Range(0, 1)) = 0.42
        _FeatureSoftness ("Face Feature Softness", Range(0.001, 0.25)) = 0.06
        _UseTextureMask ("Use Face Detail Texture", Float) = 0
        _Cutoff ("Texture Alpha Cutoff", Range(0, 1)) = 0.05
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry+20"
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _OutlineColor;
                half4 _FeatureColor;
                float4 _MainTex_ST;
                float _OutlineWidth;
                float _FeatureThreshold;
                float _FeatureSoftness;
                float _UseTextureMask;
                float _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings OutlineVertex(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                positionWS += normalWS * _OutlineWidth;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 OutlineFragment(Varyings input) : SV_Target
            {
                if (_UseTextureMask > 0.5)
                {
                    half alpha = SAMPLE_TEXTURE2D(
                        _MainTex,
                        sampler_MainTex,
                        input.uv).a;
                    clip(alpha - _Cutoff);
                }
                return _OutlineColor;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Silhouette"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex SilhouetteVertex
            #pragma fragment SilhouetteFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _OutlineColor;
                half4 _FeatureColor;
                float4 _MainTex_ST;
                float _OutlineWidth;
                float _FeatureThreshold;
                float _FeatureSoftness;
                float _UseTextureMask;
                float _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings SilhouetteVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 SilhouetteFragment(Varyings input) : SV_Target
            {
                if (_UseTextureMask <= 0.5)
                    return _Color;

                half4 textureSample = SAMPLE_TEXTURE2D(
                    _MainTex,
                    sampler_MainTex,
                    input.uv);
                clip(textureSample.a - _Cutoff);

                half luminance = dot(
                    textureSample.rgb,
                    half3(0.299, 0.587, 0.114));
                half feature = 1.0 - smoothstep(
                    _FeatureThreshold - _FeatureSoftness,
                    _FeatureThreshold + _FeatureSoftness,
                    luminance);
                return lerp(_Color, _FeatureColor, feature);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
