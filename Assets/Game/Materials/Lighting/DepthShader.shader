Shader "Custom/DepthShader"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float viewDepth : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDepth = -UnityObjectToViewPos(o.worldPos).z;
                o.viewDepth = pow(o.viewDepth, 2) * 0.01;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return fixed4(i.viewDepth, i.viewDepth, i.viewDepth, 1.0);
            }
            ENDCG
        }
    }
}
