// Anello sonico additivo. Built-in e URP. UV.x = circonferenza, UV.y = coda(0) -> fronte(1).
Shader "VFX/SonicRingCozy"
{
    Properties
    {
        [HDR] _CoreColor ("Core", Color) = (1.0, 0.9, 0.76, 1)
        [HDR] _GlowColor ("Glow", Color) = (1.0, 0.48, 0.19, 1)
        _Intensity ("Intensità", Range(0, 4)) = 0.42
        _Ripple ("Increspature", Range(0, 1)) = 0.12
        _RippleCount ("N° increspature", Range(2, 48)) = 6
        _Fade ("Fade (da script)", Range(0, 1)) = 1
        _Power ("Power (da script)", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _CoreColor, _GlowColor;
            float _Intensity, _Ripple, _RippleCount, _Fade, _Power;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            float ln(float v, float p, float w) { float d = (v - p) / w; return exp(-d * d); }

            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            float4 frag(v2f i) : SV_Target
            {
                float v = i.uv.y;
                // linee morbide e larghe + leggero velo verso il fronte = look caldo e soffice
                float lines = ln(v, .9, .07) + ln(v, .62, .07) * .4 + ln(v, .34, .07) * .15 + smoothstep(0, 1, v) * .12;
                float ripple = (sin(i.uv.x * 6.28318 * _RippleCount + _Time.y * 4) * .5 + .5) * _Ripple + (1 - _Ripple);
                float3 col = lerp(_GlowColor.rgb, _CoreColor.rgb, .6) * lines * ripple;
                col *= _Fade * _Intensity * saturate(_Power * 0.4 + 0.5);
                return float4(col, 1);
            }
            ENDCG
        }
    }
}
