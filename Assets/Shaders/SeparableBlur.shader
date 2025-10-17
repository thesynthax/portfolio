Shader "UI/SeparableBlur"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _TexelSize ("TexelSize", Vector) = (1,1,0,0)
        _Radius ("Radius", Range(0,8)) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Transparent" }
        Pass
        {
            ZWrite Off
            Cull Off
            Fog { Mode Off }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize; // Unity provides this automatically
            float _Radius;
            float4 _TexelSize; // custom: x = 1/width, y = 1/height
            struct app { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(app v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // weights: sample in [-n..n] with gaussian weights
            float gaussian(float x, float sigma) {
                return exp(- (x*x) / (2.0 * sigma * sigma));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // horizontal pass -> we expect shader uniform to indicate which direction externally.
                // For simplicity we assume _TexelSize carries direction: (dx, dy)
                float2 texel = float2(_TexelSize.x, _TexelSize.y);
                int radius = (int)max(0, round(_Radius));
                float sigma = max(0.6, _Radius * 0.5);
                float total = 0.0;
                fixed4 c = fixed4(0,0,0,0);
                for (int s = -8; s <= 8; s++) // fixed upper bound of 8 for performance
                {
                    if (s < -radius || s > radius) continue;
                    float w = gaussian(s, sigma);
                    float2 off = i.uv + texel * s;
                    c += tex2D(_MainTex, off) * w;
                    total += w;
                }
                return c / total;
            }
            ENDCG
        }
    }
}

