// Lente gravitazionale ray-traced: piega la luce, avvolge il disco attorno al buco nero.
// Definire BH_SCREEN e BH_TEX prima di includere.
#include "UnityCG.cginc"
#define BH_STEPS 110

float _Horizon, _HScale, _Strength, _Psyche, _LensRadius, _HazeRadius;
float _DiskInner, _DiskOuter, _DiskIntensity, _Phase, _Spin, _Doppler, _NoiseScale;
float _Flash, _ShockR, _ShockAmp;
float4 _RingColor, _HotColor, _MidColor, _CoolColor, _FlashColor, _DiskN, _DiskU;

struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
struct v2f { float4 pos : SV_POSITION; float4 scr : TEXCOORD0; float3 wpos : TEXCOORD1; float2 uv : TEXCOORD2; };

v2f vertLens (appdata v)
{
    v2f o;
    float3 cw = unity_ObjectToWorld._m03_m13_m23;
    float size = length(unity_ObjectToWorld._m00_m10_m20);
    // Quad PARALLELO AL TERRENO (non più un billboard verso la camera): così non si taglia contro
    // il pavimento e lo "svarione" giace sul piano orizzontale passante per il centro del buco nero.
    float3 right = float3(1, 0, 0), up = float3(0, 0, 1);
    float3 wp = cw + (right * v.vertex.x + up * v.vertex.y) * size;
    o.wpos = wp;
    o.pos = mul(UNITY_MATRIX_VP, float4(wp, 1));
    o.scr = BH_SCREEN(o.pos);
    o.uv = v.uv;
    return o;
}

float bh_h(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
float bh_n(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(bh_h(i), bh_h(i + float2(1, 0)), f.x), lerp(bh_h(i + float2(0, 1)), bh_h(i + float2(1, 1)), f.x), f.y);
}
float bh_fbm(float2 p)
{
    float v = 0, a = 0.5;
    for (int i = 0; i < 5; i++) { v += a * bh_n(p); p = p * 2.03 + 17.0; a *= 0.5; }
    return v;
}
float3 bh_hue(float x) { return 0.5 + 0.5 * cos(6.2832 * (float3(0, 0.33, 0.67) + x)); }

float4 diskCol(float3 q, float rr, float3 v)
{
    float3 N = _DiskN.xyz, U = _DiskU.xyz, V = cross(N, U);
    float2 p = float2(dot(q, U), dot(q, V)) / _DiskOuter;
    float r = rr / _DiskOuter;
    float inner = _DiskInner / _DiskOuter;
    float t = saturate((r - inner) / max(1e-3, 1.0 - inner));
    float a = _Phase * 1.5 / (0.3 + t * 1.5);
    float c = cos(-a), s = sin(-a);
    float2 qq = float2(c * p.x - s * p.y, s * p.x + c * p.y);
    float n = bh_fbm(qq * _NoiseScale + r * 3.0) * 0.7 + bh_fbm(float2(r * 28.0, 0) + qq * 1.5) * 0.5;
    float3 col = lerp(_HotColor.rgb, _MidColor.rgb, smoothstep(0, 0.35, t));
    col = lerp(col, _CoolColor.rgb, smoothstep(0.35, 1.0, t));
    float I = (pow(1.0 - t, 1.6) * 2.2 + 0.12) * (0.35 + n * 1.3) + exp(-t * 30.0) * 1.5;
    float an = atan2(qq.y, qq.x);
    float3 rb = 0.5 + 0.5 * cos(6.2832 * (float3(0, 0.33, 0.67) + t * 1.2 + an / 6.2832 * 2.0 + _Phase * 0.08 + n * 0.6));
    float arms = 0.55 + 0.45 * sin(an * 3.0 + log(r + 0.01) * 9.0 - _Phase * 1.7);
    col = lerp(col, rb * 1.4, _Psyche);
    I *= lerp(1.0, 0.4 + arms * 1.4, _Psyche * 0.8);
    float edge = smoothstep(0, 0.04, t) * (1.0 - smoothstep(0.55, 1.0, t));
    float3 tg = normalize(cross(N, q)) * (_Spin >= 0 ? 1.0 : -1.0);
    float dop = 1.0 + _Doppler * dot(tg, -normalize(v));
    return float4(col * I * edge * max(dop, 0.05) * _DiskIntensity, saturate(I * edge * 0.5));
}

float4 fragLens (v2f i) : SV_Target
{
    float2 suv = i.scr.xy / i.scr.w;
    float3 orig = tex2D(BH_TEX, suv).rgb;
    float3 C = unity_ObjectToWorld._m03_m13_m23;
    float H = max(_Horizon, 1e-4);
    float R = _LensRadius;
    float tm = _Time.y;
    float w = 1.0 - smoothstep(0.72, 1.0, length(i.uv - 0.5) * 2.0);
    // Posizione del pixel sul piano orizzontale del buco nero, in unità di orizzonte: il quad giace
    // su quel piano, quindi i.wpos ci sta già sopra (vale sia per camera prospettica sia ortografica).
    float2 goff = (i.wpos.xz - C.xz) / H;
    float gd = length(goff);
    // Raggio visibile dell'effetto, indipendente da _LensRadius (che deve restare >= al disco).
    w *= 1.0 - smoothstep(0.7 * _HazeRadius, _HazeRadius, gd);
    if (w <= 0.0) discard; // fuori dall'effetto non disegna niente: la Opaque Texture non ha particelle/onde, ridisegnarla le cancellerebbe
    float gAn = atan2(goff.y, goff.x);

    float3 ro, rd;
    if (unity_OrthoParams.w > 0.5) { rd = -UNITY_MATRIX_V[2].xyz; ro = i.wpos - rd * H * R * 4.0; }
    else { ro = _WorldSpaceCameraPos; rd = normalize(i.wpos - ro); }

    float3 p = (ro - C) / H;
    float b = dot(p, rd), c = dot(p, p) - R * R, disc = b * b - c;
    if (disc <= 0) discard;
    p += rd * max(0, -b - sqrt(disc));

    float bimp = length(cross(p, rd));
    float3 v = rd;
    float3 hv = cross(p, v);
    float h2 = dot(hv, hv);
    float rs = _HScale;
    float k = rs * _Strength;
    float3 acc = 0; float trans = 1; float rmin = 1e5; bool cap = false;
    float3 N = _DiskN.xyz;

    [loop] for (int s = 0; s < BH_STEPS; s++)
    {
        float r = length(p);
        rmin = min(rmin, r);
        if (r < rs) { cap = true; break; }
        if (s > 0 && r > R * 1.01 && dot(p, v) > 0) break;
        float dt = clamp(0.1 * r, 0.02, 0.6);
        float3 pp = p;
        v += -1.5 * k * h2 * p / (r * r * r * r * r) * dt;
        p += v * dt;
        float d0 = dot(pp, N), d1 = dot(p, N);
        if (d0 * d1 < 0 && trans > 0.01 && _DiskOuter > _DiskInner)
        {
            float3 q = lerp(pp, p, d0 / (d0 - d1));
            float rr = length(q);
            if (rr > _DiskInner && rr < _DiskOuter) { float4 dc = diskCol(q, rr, v); acc += trans * dc.rgb; trans *= 1.0 - dc.a; }
        }
    }

    float asp = _ScreenParams.x / _ScreenParams.y;
    float4 cc = BH_SCREEN(mul(UNITY_MATRIX_VP, float4(C, 1)));
    float2 cuv = cc.xy / cc.w;
    float hs = saturate(rs);
    float3 bg = 0;
    float an = 0;
    float sh = _ShockAmp * exp(-pow((gd - _ShockR) / 1.2, 2.0));
    if (!cap)
    {
        float3 dir = normalize(v);
        float camD = length(ro - C);
        float4 cp = mul(UNITY_MATRIX_VP, float4(C + p * H + dir * camD, 1));
        if (cp.w > 1e-4)
        {
            float4 sp = BH_SCREEN(cp);
            float2 dd = sp.xy / sp.w - cuv;
            dd.x *= asp;
            dd *= 1.0 - sh * 0.25;
            // Torsione a spirale fatta sul piano del terreno (non sullo schermo): ruoto il punto del
            // pavimento attorno alla verticale del buco nero e prendo lo spostamento che ne risulta sullo schermo.
            float tw = _Psyche * hs * (2.5 / (gd + 0.5)) * (1.2 + 0.6 * sin(tm * 0.4));
            float cs = cos(tw), sn = sin(tw);
            float2 goffT = float2(cs * goff.x - sn * goff.y, sn * goff.x + cs * goff.y);
            float4 sgt = BH_SCREEN(mul(UNITY_MATRIX_VP, float4(C + float3(goffT.x, 0, goffT.y) * H, 1)));
            float2 twist = sgt.xy / sgt.w - suv;
            float ca = _Psyche * hs * 0.015 * (1.0 + 3.0 / (gd + 0.01));
            float2 d1 = dd * (1 - ca), d2 = dd * (1 + ca);
            dd.x /= asp; d1.x /= asp; d2.x /= asp;
            bg = float3(tex2D(BH_TEX, saturate(cuv + d1 + twist)).r, tex2D(BH_TEX, saturate(cuv + dd + twist)).g, tex2D(BH_TEX, saturate(cuv + d2 + twist)).b);
        }
        else bg = orig;
        bg = lerp(bg, bg * bh_hue(gAn / 6.2832 + tm * 0.05) * 2.0, _Psyche * 0.6 * hs);
        float tun = 0.5 + 0.5 * sin(log(gd + 0.01) * 14.0 - tm * 3.0 + gAn * 4.0);
        bg += _Psyche * hs * 0.08 * bh_hue(log(gd + 0.01) * 0.8 - tm * 0.2) * tun * smoothstep(2.6 * rs, 5.0 * rs + 0.01, gd);
        float3 ring = lerp(_RingColor.rgb, bh_hue(gAn / 6.2832 * 2.0 + tm * 0.3) * 1.6, _Psyche);
        bg += ring * exp(-pow((rmin - 1.5 * rs) / (0.03 + 0.05 * rs), 2.0)) * 0.9 * hs;
    }

    float3 col = acc + trans * bg;
    col += _FlashColor.rgb * _Flash * exp(-gd * gd / (1.0 + _Flash * 4.0));
    col += lerp(_FlashColor.rgb * 0.3, bh_hue(gd * 0.15 - tm), _Psyche) * sh * 0.5 * exp(-pow((gd - _ShockR) / 0.4, 2.0));
    return float4(lerp(orig, col, w), 1);
}
