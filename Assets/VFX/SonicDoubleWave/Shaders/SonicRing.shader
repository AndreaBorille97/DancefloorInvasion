// Anello sonico additivo. Built-in e URP. UV.x = circonferenza, UV.y = coda(0) -> fronte(1).
Shader "VFX/SonicRing"
{
    Properties
    {
        [HDR] _CoreColor ("Core", Color) = (0.74, 0.99, 1.0, 1)
        [HDR] _GlowColor ("Glow", Color) = (0.18, 0.48, 1.0, 1)
        _Intensity ("Intensità", Range(0, 4)) = 0.55
        _Ripple ("Increspature", Range(0, 1)) = 0.25
        _RippleCount ("N° increspature", Range(4, 48)) = 24
        _Rage ("Berserk (strappi, linee extra)", Range(0, 1)) = 0
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
            float _Rage, _Intensity, _Ripple, _RippleCount, _Fade, _Power;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            float ln(float v, float p, float w) { float d = (v - p) / w; return exp(-d * d); }

            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            float4 frag(v2f i) : SV_Target
            {
                float v = i.uv.y;
                float lines = ln(v, .94, .025) + ln(v, .72, .02) * .45 + ln(v, .5, .018) * .22 + ln(v, .3, .015) * .1;
                float rp = saturate(_Ripple + _Rage * .5);
                float ripple = (sin(i.uv.x * 6.28318 * _RippleCount + _Time.y * 30) * .5 + .5) * rp + (1 - rp);
                float n = frac(sin(floor(i.uv.x * 70) * 91.7 + floor(_Time.y * 24) * 13.1) * 4375.5);
                float tear = lerp(1, step(.22, n) * 1.7, _Rage);
                lines += (ln(v, .83, .03) * .6 + ln(v, .62, .025) * .3) * _Rage;
                float3 col = lerp(_GlowColor.rgb, _CoreColor.rgb, .6 - _Rage * .15) * lines * ripple * tear;
                col *= _Fade * _Intensity * (1 + _Rage * .35) * saturate(_Power * 0.4 + 0.5);
                return float4(col, 1);
            }
            ENDCG
        }
    }
}
