Shader "Supernova/PortalExample/Surface"
{
    Properties
    {
        _PortalTexture("Portal View", 2D) = "black" {}
        [HDR] _EdgeColor("Edge Color", Color) = (0.05, 0.7, 5, 1)
        _InteriorTint("Interior Tint", Color) = (0.78, 0.9, 1, 1)
        _EdgeWidth("Edge Width", Range(0.02, 0.35)) = 0.14
        _PulseSpeed("Pulse Speed", Range(0, 8)) = 2.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-10"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "PortalSurface"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPosition : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            TEXTURE2D(_PortalTexture);
            SAMPLER(sampler_PortalTexture);
            float4 _PortalTexture_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float4 _EdgeColor;
                float4 _InteriorTint;
                float _EdgeWidth;
                float _PulseSpeed;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.screenPosition = ComputeScreenPos(output.positionCS);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 ellipse = (input.uv - 0.5) * 2.0;
                float radius = length(ellipse);
                clip(1.0 - radius);

                float2 screenUV = input.screenPosition.xy / input.screenPosition.w;
                #if UNITY_UV_STARTS_AT_TOP
                    if (_PortalTexture_TexelSize.y < 0.0)
                    {
                        screenUV.y = 1.0 - screenUV.y;
                    }
                #endif

                half3 portalView = SAMPLE_TEXTURE2D(
                    _PortalTexture,
                    sampler_PortalTexture,
                    screenUV).rgb;
                float innerEdge = smoothstep(
                    1.0 - _EdgeWidth,
                    1.0,
                    radius);
                float angle = atan2(ellipse.y, ellipse.x);
                float pulse = 0.78 + 0.22 * sin(
                    angle * 9.0 - _Time.y * _PulseSpeed + radius * 14.0);
                half3 interior = portalView * _InteriorTint.rgb;
                half3 color = lerp(
                    interior,
                    _EdgeColor.rgb * pulse,
                    saturate(innerEdge));
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
