Shader "Supernova/Lighting/Crystal Ore Sparkle Overlay"
{
    Properties
    {
        [HDR] _EmissionColor("Sparkle Color", Color) = (1,1,1,1)
        _ClearCoatMask("Sparkle Energy", Range(0.0, 1.0)) = 0.65
        _ClearCoatSmoothness("Sparkle Size", Range(0.02, 0.24)) = 0.11
        _DetailNormalMapScale("Sparkle Speed", Range(0.1, 1.2)) = 0.42
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+50"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SparkleOverlay"
            Tags { "LightMode" = "UniversalForward" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SparkleVertex
            #pragma fragment SparkleFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _EmissionColor;
                float _ClearCoatMask;
                float _ClearCoatSmoothness;
                float _DetailNormalMapScale;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 seed : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half pulse : TEXCOORD1;
            };

            Varyings SparkleVertex(Attributes input)
            {
                Varyings output;
                float3 centreWS = TransformObjectToWorld(input.positionOS);
                float phase = frac(
                    _Time.y * max(_DetailNormalMapScale, 0.05)
                    + input.seed.x);
                half pulse = (half)(
                    smoothstep(0.0, 0.08, phase)
                    * (1.0 - smoothstep(0.28, 0.48, phase)));
                pulse *= pulse;

                float halfSize = max(_ClearCoatSmoothness, 0.01)
                    * lerp(0.72, 1.18, input.seed.y);
                float2 corner = input.uv * 2.0 - 1.0;
                float3 viewDirectionWS = SafeNormalize(
                    GetCameraPositionWS() - centreWS);
                float3 cameraRightWS = SafeNormalize(UNITY_MATRIX_V[0].xyz);
                float3 cameraUpWS = SafeNormalize(UNITY_MATRIX_V[1].xyz);
                float3 positionWS = centreWS
                    + cameraRightWS * corner.x * halfSize
                    + cameraUpWS * corner.y * halfSize
                    + viewDirectionWS * min(halfSize * 0.12, 0.018);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.pulse = pulse;
                return output;
            }

            half4 SparkleFragment(Varyings input) : SV_Target
            {
                float2 distanceFromCentre = abs(input.uv * 2.0 - 1.0);
                float horizontalTaper = smoothstep(
                    0.0,
                    1.0,
                    distanceFromCentre.x);
                float verticalTaper = smoothstep(
                    0.0,
                    1.0,
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
                half sparkleAlpha = saturate(
                    sparkleMask * input.pulse);
                clip(sparkleAlpha - 0.002h);

                half3 glow = max(
                    _EmissionColor.rgb,
                    half3(0.02h, 0.02h, 0.02h));
                half glowPeak = max(max(glow.r, glow.g), glow.b);
                half3 hue = glow / max(glowPeak, 0.001h);
                half whiteCore = saturate(core * 0.82h);
                half3 sparkleColor = lerp(
                    hue,
                    half3(1.0h, 1.0h, 1.0h),
                    whiteCore);
                half energy = 2.2h
                    + saturate(_ClearCoatMask) * 3.2h
                    + min(glowPeak, 2.0h) * 0.75h;
                half3 premultipliedColor =
                    sparkleColor * energy * sparkleAlpha;
                return half4(premultipliedColor, sparkleAlpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
