Shader "Supernova/PortalExample/Clipped Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
        _Smoothness("Smoothness", Range(0,1)) = 0.5
        _Metallic("Metallic", Range(0,1)) = 0
        _MetallicGlossMap("Metallic", 2D) = "white" {}
        _SpecColor("Specular", Color) = (0.2,0.2,0.2,1)
        _SpecGlossMap("Specular", 2D) = "white" {}
        _BumpScale("Normal Scale", Float) = 1
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _OcclusionStrength("Occlusion Strength", Range(0,1)) = 1
        _OcclusionMap("Occlusion", 2D) = "white" {}
        [HDR] _EmissionColor("Emission", Color) = (0,0,0,0)
        _EmissionMap("Emission", 2D) = "white" {}
        [HideInInspector] _WorkflowMode("Workflow", Float) = 1
        [HideInInspector] _Surface("Surface", Float) = 0
        [HideInInspector] _Cull("Cull", Float) = 2
        [HideInInspector] _AlphaClip("Alpha Clip", Float) = 0
        [HideInInspector] _ReceiveShadows("Receive Shadows", Float) = 1
        [HideInInspector] _SmoothnessTextureChannel("Smoothness Channel", Float) = 0
        [HideInInspector] _ClearCoatMask("Clear Coat Mask", Float) = 0
        [HideInInspector] _ClearCoatSmoothness("Clear Coat Smoothness", Float) = 0
        [HideInInspector] _MainTex("Legacy Albedo", 2D) = "white" {}
        [HideInInspector] _Color("Legacy Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "RenderPipeline"="UniversalPipeline"
            "UniversalMaterialType"="Lit"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull[_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex PortalClipVertex
            #pragma fragment PortalClipFragment
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
            #pragma shader_feature_local_fragment _SPECULAR_SETUP
            #pragma shader_feature_local_fragment _OCCLUSIONMAP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float4 _PortalClipPlane;
            float4 _PortalApertureCenterRadius;
            float3 _PortalApertureRight;
            float3 _PortalApertureUp;
            float _PortalLimitAperture;

            struct PortalClipAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct PortalClipVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                half3 vertexLighting : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            PortalClipVaryings PortalClipVertex(
                PortalClipAttributes input)
            {
                PortalClipVaryings output = (PortalClipVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positions =
                    GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normals =
                    GetVertexNormalInputs(input.normalOS, input.tangentOS);
                real tangentSign = input.tangentOS.w * GetOddNegativeScale();
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = normals.normalWS;
                output.tangentWS = half4(normals.tangentWS, tangentSign);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                output.vertexLighting = VertexLighting(
                    positions.positionWS,
                    normals.normalWS);
                return output;
            }

            void ApplyPortalClipping(float3 positionWS)
            {
                clip(dot(positionWS, _PortalClipPlane.xyz)
                    + _PortalClipPlane.w);
                if (_PortalLimitAperture > 0.5)
                {
                    float3 offset = positionWS
                        - _PortalApertureCenterRadius.xyz;
                    float apertureX = dot(offset, _PortalApertureRight);
                    float apertureY = dot(offset, _PortalApertureUp);
                    float radius = _PortalApertureCenterRadius.w;
                    clip(radius * radius
                        - apertureX * apertureX
                        - apertureY * apertureY);
                }
            }

            half4 PortalClipFragment(PortalClipVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                ApplyPortalClipping(input.positionWS);

                SurfaceData surfaceData;
                InitializeStandardLitSurfaceData(input.uv, surfaceData);
                half3 normalWS = normalize(input.normalWS);
                #if defined(_NORMALMAP)
                    half tangentSign = input.tangentWS.w;
                    half3 bitangent = tangentSign * cross(
                        input.normalWS,
                        input.tangentWS.xyz);
                    normalWS = TransformTangentToWorld(
                        surfaceData.normalTS,
                        half3x3(
                            input.tangentWS.xyz,
                            bitangent,
                            input.normalWS));
                    normalWS = NormalizeNormalPerPixel(normalWS);
                #endif

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS =
                    GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord =
                    TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.vertexLighting = input.vertexLighting;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV =
                    GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1,1,1,1);
                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask 0
            ZWrite On
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex PortalDepthVertex
            #pragma fragment PortalDepthFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _PortalClipPlane;
            float4 _PortalApertureCenterRadius;
            float3 _PortalApertureRight;
            float3 _PortalApertureUp;
            float _PortalLimitAperture;

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            DepthVaryings PortalDepthVertex(DepthAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                VertexPositionInputs positions =
                    GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                return output;
            }
            half4 PortalDepthFragment(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                clip(dot(input.positionWS, _PortalClipPlane.xyz)
                    + _PortalClipPlane.w);
                if (_PortalLimitAperture > 0.5)
                {
                    float3 offset = input.positionWS
                        - _PortalApertureCenterRadius.xyz;
                    float x = dot(offset, _PortalApertureRight);
                    float y = dot(offset, _PortalApertureUp);
                    float radius = _PortalApertureCenterRadius.w;
                    clip(radius * radius - x * x - y * y);
                }
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
