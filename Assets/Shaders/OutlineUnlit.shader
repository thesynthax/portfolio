Shader "Custom/OutlineUnlit"
{
    Properties
    {
        _Color ("Outline Color", Color) = (1,1,1,1)
        _Thickness ("Thickness", Range(0,0.3)) = 0.06
        _MainTex ("MainTex (unused)", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue"="Geometry-1" }
        Cull Front           // draw backfaces so expanded silhouette appears behind
        ZWrite On
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Thickness;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float3 n = normalize(v.normal);
                // expand in object space
                float4 pos = v.vertex;
                pos.xyz += n * _Thickness;
                o.pos = UnityObjectToClipPos(pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
    FallBack Off
}

