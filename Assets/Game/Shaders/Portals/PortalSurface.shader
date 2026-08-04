Shader "Supernova/Portals/PortalSurface"
{
    Properties
    {
        _PortalTexture("Portal Texture", 2D) = "black" {}
        [HDR]_EdgeColor("Edge Color", Color) = (0.1, 2.0, 8.0, 1)
        _EdgeWidth("Edge Width", Range(0.01, 0.35)) = 0.12
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Geometry"
        }

        Pass
        {
            Name "PortalSurface"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_PortalTexture);
            SAMPLER(sampler_PortalTexture);

            CBUFFER_START(UnityPerMaterial)
                float4 _EdgeColor;
                float _EdgeWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 screenUv = input.screenPos.xy / input.screenPos.w;
                half3 portal = SAMPLE_TEXTURE2D(
                    _PortalTexture,
                    sampler_PortalTexture,
                    screenUv).rgb;

                float2 centered = input.uv * 2.0 - 1.0;
                float ellipse = length(centered);
                float edge = smoothstep(1.0 - _EdgeWidth, 1.0, ellipse);
                float pulse = 0.75 + 0.25 * sin(_Time.y * 4.0 + centered.y * 12.0);
                half3 color = portal + _EdgeColor.rgb * edge * pulse;
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
