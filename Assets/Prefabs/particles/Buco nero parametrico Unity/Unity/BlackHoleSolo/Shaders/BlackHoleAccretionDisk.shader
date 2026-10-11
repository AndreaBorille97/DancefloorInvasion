// Disco di accrescimento procedurale. Funziona su Built-in e URP (unlit, additivo).
Shader "BlackHole/AccretionDisk"
{
    Properties
    {
        [HDR] _HotColor ("Colore interno", Color) = (1, 0.95, 0.84, 1)
        [HDR] _MidColor ("Colore medio", Color) = (1, 0.63, 0.25, 1)
        [HDR] _CoolColor ("Colore esterno", Color) = (0.63, 0.16, 0.03, 1)
        _Inner ("Raggio interno (0-1)", Range(0.01, 0.95)) = 0.4
        _Spin ("Velocità rotazione", Float) = 1
        _Doppler ("Effetto Doppler", Range(0, 1)) = 0.6
        _Intensity ("Intensità", Range(0, 10)) = 1.5
        _NoiseScale ("Scala turbolenza", Range(0.5, 10)) = 4
        _Psyche ("Psichedelia", Range(0, 1)) = 0.85
        _Phase ("Fase (guidata da script)", Float) = 0
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

            float4 _HotColor, _MidColor, _CoolColor;
            float _Inner, _Spin, _Doppler, _Intensity, _NoiseScale, _Psyche, _Phase;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 viewObj : TEXCOORD1; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.viewObj = ObjSpaceViewDir(v.vertex);
                return o;
            }

            float h(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float n2(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(h(i), h(i + float2(1, 0)), f.x), lerp(h(i + float2(0, 1)), h(i + float2(1, 1)), f.x), f.y);
            }
            float fbm(float2 p)
            {
                float v = 0, a = 0.5;
                for (int i = 0; i < 5; i++) { v += a * n2(p); p = p * 2.03 + 17.0; a *= 0.5; }
                return v;
            }

            float4 frag (v2f i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * 2.0;
                float r = length(p);
                if (r > 1.0 || r < _Inner) discard;
                float t = (r - _Inner) / (1.0 - _Inner);

                float a = _Phase * 1.5 / (0.3 + t * 1.5);
                float c = cos(-a), s = sin(-a);
                float2 q = float2(c * p.x - s * p.y, s * p.x + c * p.y);
                float n = fbm(q * _NoiseScale + r * 3.0) * 0.7 + fbm(float2(r * 28.0, 0) + q * 1.5) * 0.5;

                float3 col = lerp(_HotColor.rgb, _MidColor.rgb, smoothstep(0, 0.35, t));
                col = lerp(col, _CoolColor.rgb, smoothstep(0.35, 1.0, t));
                float I = (pow(1.0 - t, 1.6) * 2.2 + 0.12) * (0.35 + n * 1.3) + exp(-t * 30.0) * 1.5;
                float an = atan2(q.y, q.x);
                float3 rb = 0.5 + 0.5 * cos(6.2832 * (float3(0, 0.33, 0.67) + t * 1.2 + an / 6.2832 * 2.0 + _Time.y * 0.12 + n * 0.6));
                float arms = 0.55 + 0.45 * sin(an * 3.0 + log(r + 0.01) * 9.0 - _Time.y * 2.5);
                col = lerp(col, rb * 1.4, _Psyche);
                I *= lerp(1.0, 0.4 + arms * 1.4, _Psyche * 0.8);
                float edge = smoothstep(0, 0.04, t) * (1.0 - smoothstep(0.55, 1.0, t));

                float3 vd = normalize(i.viewObj);
                float2 tg = float2(-p.y, p.x) / r;
                float dop = 1.0 + _Doppler * dot(tg, vd.xy) * sign(_Spin);

                return float4(col * I * edge * max(dop, 0.05) * _Intensity, 1);
            }
            ENDCG
        }
    }
}
