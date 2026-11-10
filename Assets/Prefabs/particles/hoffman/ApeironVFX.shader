// Apeiron — area VFX (Julia tentacles + psychedelic gas)
// Works in Built-in and URP (unlit, premultiplied alpha). Driven by ApeironVFX.cs.
Shader "Apeiron/AreaVFX"
{
    Properties
    {
        [Header(Fractal)]
        _JuliaX ("Julia X", Range(-1.2, 0.4)) = -0.745
        _JuliaY ("Julia Y", Range(-0.8, 0.8)) = 0.113
        _Morph ("Morph", Range(0, 0.1)) = 0.018
        _Iter ("Iterations", Range(20, 200)) = 90
        _Feathers ("Feathers", Range(0, 12)) = 4
        [Header(Tentacles)]
        _Segments ("Segments", Range(1, 16)) = 8
        _TentLen ("Tentacle Length", Range(0.8, 4)) = 2.3
        _TentThin ("Tentacle Thin", Range(0.5, 6)) = 2.6
        _Reach ("Reach", Range(0.2, 1.5)) = 1.1
        _Twist ("Twist", Range(0, 5)) = 1.4
        _Rotation ("Rotation", Range(-1, 1)) = 0.12
        [Header(Gas)]
        _GasAmount ("Gas Amount", Range(0, 2)) = 1
        _GasSize ("Gas Size", Range(0, 0.8)) = 0.12
        _GasSpeed ("Gas Speed", Range(0, 3)) = 1
        _GasArms ("Gas Arms", Range(2, 20)) = 7
        _GasTendrils ("Gas Tendrils", Range(0, 1.5)) = 0.5
        [Header(Color)]
        _Hue ("Hue", Range(0, 1)) = 0
        _ColorBands ("Color Bands", Range(0.005, 0.15)) = 0.035
        _ColorSpeed ("Color Speed", Range(0, 2)) = 0.35
        _Saturation ("Saturation", Range(0.5, 2.5)) = 1.6
        _Gloss ("Gloss", Range(0, 1)) = 0.65
        _Intensity ("Intensity", Range(0.4, 2)) = 1.4
        _VoidDark ("Void Dark", Range(0, 1)) = 0.75
        // runtime (set by script)
        [HideInInspector] _R ("R", Float) = 0
        [HideInInspector] _Charge ("Charge", Float) = 0
        [HideInInspector] _Swirl ("Swirl", Float) = 0
        [HideInInspector] _Amt ("Amt", Float) = 0
        [HideInInspector] _FxT ("FxT", Float) = 0
        [HideInInspector] _ShockR ("ShockR", Float) = -1
        [HideInInspector] _Flash ("Flash", Float) = 0
        [HideInInspector] _LocalTime ("Local Time", Float) = -1
        // Tabella di valori a caso per il rumore, generata da ApeironVFX.cs (vedi vn)
        [HideInInspector] [NoScaleOffset] _NoiseTex ("Noise", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            #define TAU 6.2831853
            #define RM 0.38      // reference radius (shader units)
            #define EXT 2.4      // quad half-size in radius units (must match script)

            float _JuliaX,_JuliaY,_Morph,_Iter,_Feathers,_Segments,_TentLen,_TentThin,_Reach,_Twist,_Rotation;
            float _GasAmount,_GasSize,_GasSpeed,_GasArms,_GasTendrils;
            float _Hue,_ColorBands,_ColorSpeed,_Saturation,_Gloss,_Intensity,_VoidDark;
            float _R,_Charge,_Swirl,_Amt,_FxT,_ShockR,_Flash,_LocalTime;
            sampler2D _NoiseTex;
            #define NOISE_SIZE 256.0 // must match ApeironVFX.NoiseSize

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            float2 rot(float2 p, float a) { float c = cos(a), s = sin(a); return float2(c*p.x - s*p.y, s*p.x + c*p.y); }
            float fmodp(float x, float y) { return x - y * floor(x / y); }
            float3 pal(float x) { return 0.5 + 0.5 * cos(TAU * (x + float3(0, 0.33, 0.67) + _Hue)); }
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            // Value noise: one bilinear fetch from a table of random values instead of four sin-based
            // hashes. Sampling at the texel centre plus the smoothed fraction gives the same smooth
            // interpolation as the original lerp(lerp(hash...)) version.
            float vn(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f*f*(3 - 2*f);
                return tex2Dlod(_NoiseTex, float4((i + f + 0.5) / NOISE_SIZE, 0, 0)).r;
            }
            // atan2 approximation (error ~1e-5 rad), used inside the Julia loop where it runs per iteration
            float fastAtan2(float y, float x)
            {
                float ax = abs(x), ay = abs(y);
                float a = min(ax, ay) / max(max(ax, ay), 1e-20);
                float s = a * a;
                float r = ((-0.0464964749 * s + 0.15931422) * s - 0.327622764) * s * a + a;
                if (ay > ax) r = 1.57079637 - r;
                if (x < 0) r = 3.14159274 - r;
                return y < 0 ? -r : r;
            }
            float fbm(float2 p)
            {
                float s = 0, a = 0.5;
                for (int i = 0; i < 5; i++) { s += a * vn(p); p = rot(p, 0.6) * 2.03; a *= 0.5; }
                return s;
            }
            void over(inout float3 c, inout float a, float3 col, float al)
            {
                al = saturate(al); c = c * (1 - al) + col * al; a = a * (1 - al) + al;
            }

            float4 tentacles(float2 q, float t, float g)
            {
                float r = length(q); float an = atan2(q.y, q.x);
                an += r * _Twist * sin(t * 0.35) + t * _Rotation;
                float seg = TAU / _Segments;
                float af = abs(fmodp(an, seg) - seg * 0.5);
                float2 z = float2(-1.25 + r * _TentLen + 0.08 * sin(t * 0.27), af * r * _TentThin);
                float2 c = float2(_JuliaX, _JuliaY) + _Morph * float2(sin(t * 0.41), cos(t * 0.33));
                float n = 0, st = 0, esc = 0;
                [loop] for (int i = 0; i < 200; i++)
                {
                    if (i >= (int)_Iter) break;
                    z = float2(z.x*z.x - z.y*z.y, 2*z.x*z.y) + c;
                    if (dot(z, z) > 256) { esc = 1; break; }
                    st += 0.5 + 0.5 * sin(_Feathers * fastAtan2(z.y, z.x) + t * 1.5);
                    n += 1;
                }
                float sn = n - log2(log2(max(dot(z, z), 1.0001))) + 4;
                st /= max(n, 1);
                float nn = saturate(sn / (_Iter * 0.42));
                float reach = g * (0.45 + _Reach * nn);
                float alpha = smoothstep(4, 10, sn) * smoothstep(reach, reach - 0.05, r);
                float3 col = pal(sn * _ColorBands - t * _ColorSpeed + r * 0.4);
                col *= 0.35 + 0.85 * st;
                col = lerp(col, 1, pow(st, 7) * _Gloss);
                col *= 0.75 + 0.25 * smoothstep(0, 0.15, abs(frac(sn * 0.25) - 0.5));
                if (esc < 0.5) { col = pal(t * 0.3 + r) * 0.25; alpha = smoothstep(r - 0.05, r, g); }
                float l = dot(col, 0.333); col = max(lerp(l, col, _Saturation), 0);
                return float4(col * _Intensity, alpha);
            }

            float4 frag (v2f i) : SV_Target
            {
                float2 uv = (i.uv * 2 - 1) * EXT * RM;
                float t = _LocalTime >= 0 ? _LocalTime : _Time.y;
                float uR = _R * RM;
                float d = length(uv);
                // Outside the inscribed circle the final edge fade is 0: skip everything (corners of the quad)
                if (d >= EXT * RM) return 0;
                float2 w = rot(uv, _Swirl * 1.6 / (d * 6 + 0.3));
                float3 c = 0; float a = 0;

                float inside = smoothstep(uR + 0.004, uR - 0.004, d) * step(0.001, uR);
                over(c, a, float3(0.018, 0.01, 0.03) + pal(d * 3 - t) * 0.035, inside * _VoidDark);

                // Tentacle alpha is 0 beyond max reach (escaped: g*(0.45+_Reach)) and beyond g+0.05
                // (inside the set): there the Julia loop would only produce invisible pixels.
                float tr = length(w) / RM;
                if (uR > 0.001 && d < RM * 1.35 && tr < max(_R * (0.45 + _Reach), _R + 0.05))
                {
                    float4 tc = tentacles(w / RM, t * 0.6 + _FxT, _R);
                    over(c, a, tc.rgb, tc.a);
                    float pup = 0.025 + 0.01 * sin(t * 6);
                    over(c, a, 0, (1 - smoothstep(pup, pup + 0.006, d)) * inside);
                    c += pal(t) * exp(-abs(d - pup - 0.008) * 140) * 1.5 * inside;
                }

                float gv = max(_Amt, _Flash) * _GasAmount;
                float gr = max(uR, RM * 0.35 * _Charge);
                // The gas band is 0 inside the smallest possible inner radius (fbm >= 0) and outside
                // the largest possible outer edge (fbm and ridge <= 1): skip the 11 fbm there.
                float gasMax = gr * (0.45 + _Reach) + RM * (_GasSize * 1.4 + _GasTendrils * 1.5);
                if (gv > 0.001 && d > gr * 0.425 && d < gasMax)
                {
                    float gt = t * _GasSpeed;
                    float2 gp = rot(w, 0.9 / (d * 5 + 0.4) + gt * 0.25) / RM * 2.2;
                    float2 wq = float2(fbm(gp + float2(0, gt * 0.6)), fbm(gp + float2(5.2, -gt * 0.5)));
                    float2 wr = float2(fbm(gp + 3*wq + float2(1.7, 9.2) + gt * 0.3), fbm(gp + 3*wq + float2(8.3, 2.8) - gt * 0.25));
                    float f = fbm(gp + 3.5 * wr);
                    float outer = gr * (0.45 + _Reach);
                    float2 gdir = uv / (d + 1e-4);
                    float ga = atan2(uv.y, uv.x);
                    float nr = d / RM;
                    float tw = ga + nr * 1.6 * sin(gt * 0.4) + 1.2 * (fbm(gdir * 1.5 + float2(nr * 2, gt * 0.3)) - 0.5);
                    float ridge = pow(abs(sin(tw * _GasArms * 0.5)), 5);
                    float jag = fbm(gdir * 2.5 + float2(gt * 0.4, -gt * 0.2));
                    float ext = RM * (_GasSize * (0.4 + jag) + _GasTendrils * ridge * (0.5 + jag));
                    float inner = gr * (0.55 + 0.25 * (fbm(gdir * 3 - gt * 0.3) - 0.5));
                    float band = smoothstep(outer + ext, outer + ext * 0.3, d) * smoothstep(inner, gr * 1.02, d);
                    float dens = smoothstep(0.3, 0.85, f) * band * gv;
                    float3 gc = pal(f * 1.8 + length(wr) * 1.2 - gt * 0.3 + d) * (0.6 + 0.9 * f);
                    gc += pal(length(wq) * 2 - gt * 0.5) * pow(f, 4) * 1.5;
                    over(c, a, gc, dens * 0.85);
                    c += gc * dens * 0.35;
                }

                if (uR > 0.001)
                {
                    float an = atan2(uv.y, uv.x);
                    c += exp(-abs(d - uR) * 90) * pal(an / TAU * 3 + t) * 1.6;
                    c += exp(-abs(d - uR) * 18) * 0.28 * pal(t * 0.5);
                }
                if (_Charge > 0)
                {
                    float cr = 0.045 * _Charge;
                    over(c, a, 0, 1 - smoothstep(cr, cr + 0.004, d));
                    c += pal(t * 2) * exp(-abs(d - cr) * 120) * _Charge * 2;
                    float an = atan2(uv.y, uv.x);
                    float sa = floor((an + 3.14159) / TAU * 70);
                    float sp = hash(float2(sa, 3));
                    float stk = exp(-abs(frac(d * 1.4 + t * 2.4 + sp) - 0.5) * 30) * step(0.6, sp) * smoothstep(0.45, 0.05, d);
                    c += pal(sp + t) * stk * _Charge * 1.2;
                }
                if (_Flash > 0)
                {
                    c += exp(-abs(d - _ShockR) * 35) * _Flash * pal(d - t * 2) * 2;
                    c += pow(_Flash, 4) * 0.4 * smoothstep(EXT * RM, 0, d);
                }

                float edge = smoothstep(EXT * RM, EXT * RM * 0.8, d);
                return float4(c * edge, saturate(a) * edge);
            }
            ENDCG
        }
    }
}
