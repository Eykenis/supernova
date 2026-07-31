Shader "Supernova/Shop/Geometry Wireframe"
{
    Properties
    {
        [HDR] _WireColor("Wire Color", Color) = (0.35, 0.9, 1, 1)
        _Intensity("Emission Intensity", Range(0, 20)) = 4
        _LineWidth("Line Width (Pixels)", Range(0.25, 6)) = 1.25
        _Feather("Line Feather (Pixels)", Range(0.1, 3)) = 0.75
        _PulseSpeed("Pulse Speed", Range(0, 8)) = 1.5
        _PulseAmount("Pulse Amount", Range(0, 0.5)) = 0.08
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
        }

        Pass
        {
            Name "GeometryWireframe"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZTest LEqual
            ZWrite On
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.0
            #pragma require geometry
            #pragma vertex Vert
            #pragma geometry Geom
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct VertexToGeometry
            {
                float4 positionCS : SV_POSITION;
            };

            struct GeometryToFragment
            {
                float4 positionCS : SV_POSITION;
                float3 barycentric : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _WireColor;
                half _Intensity;
                half _LineWidth;
                half _Feather;
                half _PulseSpeed;
                half _PulseAmount;
            CBUFFER_END

            VertexToGeometry Vert(Attributes input)
            {
                VertexToGeometry output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            [maxvertexcount(3)]
            void Geom(
                triangle VertexToGeometry input[3],
                inout TriangleStream<GeometryToFragment> stream)
            {
                GeometryToFragment output;

                output.positionCS = input[0].positionCS;
                output.barycentric = float3(1.0, 0.0, 0.0);
                stream.Append(output);

                output.positionCS = input[1].positionCS;
                output.barycentric = float3(0.0, 1.0, 0.0);
                stream.Append(output);

                output.positionCS = input[2].positionCS;
                output.barycentric = float3(0.0, 0.0, 1.0);
                stream.Append(output);

                stream.RestartStrip();
            }

            half4 Frag(GeometryToFragment input) : SV_Target
            {
                float3 derivatives = max(
                    fwidth(input.barycentric),
                    float3(0.0001, 0.0001, 0.0001));
                float3 edgeDistance = input.barycentric / derivatives;
                float nearestEdge = min(
                    edgeDistance.x,
                    min(edgeDistance.y, edgeDistance.z));
                float lineWidth = max((float)_LineWidth, 0.01);
                float feather = max((float)_Feather, 0.01);
                half edgeAlpha = 1.0h - smoothstep(
                    lineWidth,
                    lineWidth + feather,
                    nearestEdge);

                clip(edgeAlpha - 0.001h);

                half pulse = 1.0h + sin(_Time.y * _PulseSpeed)
                    * _PulseAmount;
                half3 color = _WireColor.rgb * _Intensity * pulse;
                return half4(color, _WireColor.a * edgeAlpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
