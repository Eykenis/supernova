Shader "Supernova/Effects/MagnetEnergyRibbon"
{
    Properties
    {
        [HDR] _EnergyColor ("Energy Color", Color) = (0.035, 1.25, 0.42, 0.72)
        [HDR] _HotColor ("Hot Rune Color", Color) = (0.62, 1.65, 0.72, 0.96)
        _Alpha ("Layer Alpha", Range(0, 1)) = 1
        _Phase ("Flow Phase", Float) = 0
        _BandDensity ("Flow Band Density", Range(1, 24)) = 8
        _PatternStrength ("Rune Pattern Strength", Range(0, 1)) = 0.85
        _EdgePower ("Edge Softness", Range(0.5, 8)) = 2
        [HideInInspector] _ParticleMode ("Particle Mode", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+40"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "EnergyForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _EnergyColor;
                half4 _HotColor;
                float _Alpha;
                float _Phase;
                float _BandDensity;
                float _PatternStrength;
                float _EdgePower;
                float _ParticleMode;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            float TrianglePulse(float value)
            {
                return 1.0 - abs(frac(value) * 2.0 - 1.0);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float phase = _Phase + _Time.y * 0.12;
                float across = saturate(1.0 - abs(input.uv.y * 2.0 - 1.0));
                float softRibbon = pow(across, max(0.5, _EdgePower));
                float hotCore = pow(across, 9.0);

                float travelling = TrianglePulse(
                    input.uv.x * _BandDensity - phase * 1.7);
                travelling = pow(saturate(travelling), 10.0);

                float runeCell = frac(
                    input.uv.x * (_BandDensity * 0.55) - phase);
                float runeArm = abs(runeCell * 2.0 - 1.0);
                float runeDistance = abs(
                    abs(input.uv.y * 2.0 - 1.0) - runeArm * 0.72);
                float rune = 1.0 - smoothstep(0.045, 0.16, runeDistance);
                rune *= step(0.13, runeCell) * step(runeCell, 0.87);
                rune *= _PatternStrength * softRibbon;

                float shimmer = sin(
                    input.uv.x * 71.0
                    - phase * 8.0
                    + sin(input.uv.x * 17.0) * 1.8) * 0.5 + 0.5;
                shimmer = pow(shimmer, 7.0) * softRibbon * 0.38;

                float ribbonAlpha = softRibbon
                    * (0.16 + hotCore * 0.62 + travelling * 0.48
                        + rune * 0.68 + shimmer);
                float ribbonHeat = saturate(
                    hotCore * 0.5 + travelling + rune + shimmer);

                float2 particleDelta = input.uv - 0.5;
                float particleRadius = length(particleDelta) * 2.0;
                float particleCore = pow(
                    saturate(1.0 - particleRadius),
                    2.4);
                float particleRay = pow(
                    saturate(1.0 - min(
                        abs(particleDelta.x),
                        abs(particleDelta.y)) * 11.0),
                    5.0) * saturate(1.0 - particleRadius);
                float particleAlpha = saturate(
                    particleCore + particleRay * 0.42);

                float mode = saturate(_ParticleMode);
                float alphaMask = lerp(ribbonAlpha, particleAlpha, mode);
                float heat = lerp(ribbonHeat, particleCore, mode);
                half3 energy = lerp(
                    _EnergyColor.rgb,
                    _HotColor.rgb,
                    heat);
                energy *= 0.68 + heat * 1.45;
                energy *= input.color.rgb;

                half alpha = saturate(
                    alphaMask * _Alpha * input.color.a);
                return half4(energy, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
