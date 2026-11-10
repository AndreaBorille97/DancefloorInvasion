// Shader dei puff del Mini fungo atomico 3D. Identico allo shader del prototipo web.
// Funziona in Built-in e URP (pass senza LightMode = SRPDefaultUnlit).
Shader "MiniNuke/Puff"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.BlendMode)] _Src ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _Dst ("Dst Blend", Float) = 10
        _Add ("Additive", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend [_Src] [_Dst]
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Add;
            struct a2v { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float2 d : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float2 d : TEXCOORD1; };
            v2f vert (a2v v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.color = v.color; o.uv = v.uv; o.d = v.d; return o; }

            float h21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vn(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(h21(i), h21(i + float2(1,0)), f.x), lerp(h21(i + float2(0,1)), h21(i + 1), f.x), f.y); }

            float4 frag (v2f i) : SV_Target
            {
                float m = i.d.x; float2 uv = i.uv; float d = length(uv);
                float3 col = i.color.rgb; float a;
                if (m < 0.5) {            // puff (fumo/fuoco)
                    float n = vn(uv * 2.5 + i.d.y * 17) * 0.6 + vn(uv * 5 + i.d.y * 31) * 0.4;
                    float t = length(uv - float2(-0.18, 0.28) * (1 - min(d, 1))) + (n - 0.5) * 0.22;
                    if (t >= 1) discard;
                    float3 hi = min(1, col * 1.12 + 0.04), lo = col * 0.5;
                    float4 s0 = float4(hi, 1), s1 = float4(col, 0.8), s2 = float4(lo, 0.35), s3 = float4(lo, 0);
                    float4 g = t < 0.5 ? lerp(s0, s1, t / 0.5) : (t < 0.8 ? lerp(s1, s2, (t - 0.5) / 0.3) : lerp(s2, s3, (t - 0.8) / 0.2));
                    col = g.rgb; a = g.a * i.color.a;
                }
                else if (m < 1.5) { if (d >= 1) discard; a = i.color.a * pow(1 - d, 1.5); }   // glow / flash
                else if (m < 2.5) { a = i.color.a * smoothstep(0.08, 0, abs(d - 0.9)); }      // onda d'urto
                else if (m < 3.5) { a = i.color.a * smoothstep(1, 0.55, d); }                 // bruciatura
                else { a = i.color.a * smoothstep(1, 0.2, abs(uv.x)); }                       // scintilla
                #ifndef UNITY_COLORSPACE_GAMMA
                col = GammaToLinearSpace(col);
                #endif
                return _Add > 0.5 ? float4(col * a, a) : float4(col, a);
            }
            ENDCG
        }
    }
}
