// MiniNuke3D.cs — Mini fungo atomico 3D (porting 1:1 del prototipo web "Fungo Atomico 3D").
// Unity 2020.3+ · Built-in o URP. Richiede MiniNukePuff.shader.
// Uso: MiniNuke3D.Spawn(camionetta.position);   (la base del fungo è sul punto passato)
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class MiniNuke3D : MonoBehaviour
{
    [Header("Esplosione")]
    public float scale = 1f;
    public float speed = 1f;
    [Tooltip("Metri per 'pixel' del prototipo. 0.02 → fungo alto ~3.7 m a scale 1")]
    public float unitsPerPx = 0.02f;
    public bool loop = false;
    public bool destroyWhenDone = true;

    [Header("Extra")]
    public bool flashLight = true;
    public bool cameraShake = true;
    public float shakeAmount = 0.15f;

    [Header("Shader (assegnalo per le build, vedi README)")]
    public Shader shader;

    const float DUR = 3.6f;

    struct Cap { public float phi, th, r, j, hv; }
    struct Dome { public float x, y, z, r, hv; }
    struct Stem { public float f, a, j, r, hv; }
    struct Dust { public float phi, r, sp; }
    struct Spark { public float yaw, h, v, life, w; }
    struct Q { public Vector3 p, p1; public float s, a, mode, seed, dist; public Color c; public bool flat, streak; }

    Cap[] _cap; Dome[] _dome; Stem[] _stem; Dust[] _dust; Spark[] _sparks;
    readonly List<Q> _qa = new List<Q>(512), _qb = new List<Q>(512);
    Mesh _meshA, _meshB; Material _matA, _matB;
    readonly List<Vector3> _v = new List<Vector3>(); readonly List<Color> _c = new List<Color>();
    readonly List<Vector2> _uv = new List<Vector2>(), _d = new List<Vector2>(); readonly List<int> _i = new List<int>();
    Light _light; Transform _cam; Vector3 _shake; float _t0;

    public static MiniNuke3D Spawn(Vector3 groundPos, float scale = 1f, Shader shader = null)
    {
        var go = new GameObject("MiniNuke3D");
        go.transform.position = groundPos;
        var n = go.AddComponent<MiniNuke3D>();
        n.scale = scale; n.shader = shader;
        return n;
    }

    void Start()
    {
        if (!shader) shader = Shader.Find("MiniNuke/Puff");
        _matA = new Material(shader) { renderQueue = 3000 };
        _matA.SetFloat("_Src", (float)BlendMode.SrcAlpha); _matA.SetFloat("_Dst", (float)BlendMode.OneMinusSrcAlpha); _matA.SetFloat("_Add", 0);
        _matB = new Material(shader) { renderQueue = 3001 };
        _matB.SetFloat("_Src", (float)BlendMode.One); _matB.SetFloat("_Dst", (float)BlendMode.One); _matB.SetFloat("_Add", 1);
        _meshA = new Mesh(); _meshA.MarkDynamic();
        _meshB = new Mesh(); _meshB.MarkDynamic();
        if (flashLight)
        {
            var l = new GameObject("Flash"); l.transform.SetParent(transform, false);
            l.transform.localPosition = Vector3.up * 1f * scale;
            _light = l.AddComponent<Light>(); _light.type = LightType.Point; _light.color = new Color(1f, 0.8f, 0.5f);
        }
        if (cameraShake && Camera.main) _cam = Camera.main.transform;
        Boom();
    }

    static float R() => Random.value;
    static float Eo(float k) { k = Mathf.Clamp01(k); return 1 - Mathf.Pow(1 - k, 3); }

    public void Boom()
    {
        _cap = new Cap[54];
        for (int i = 0; i < 54; i++) _cap[i] = new Cap { phi = i / 54f * Mathf.PI * 2 + R() * 0.3f, th = R() * 6.28f, r = 0.5f + R() * 0.7f, j = (R() - 0.5f) * 0.25f, hv = (R() - 0.5f) * 0.12f };
        _dome = new Dome[20];
        for (int i = 0; i < 20; i++) _dome[i] = new Dome { x = (R() - 0.5f) * 1.3f, z = (R() - 0.5f) * 1.3f, y = 0.1f + R() * 0.6f, r = 0.6f + R() * 0.6f, hv = (R() - 0.5f) * 0.12f };
        _stem = new Stem[70];
        for (int i = 0; i < 70; i++) _stem[i] = new Stem { f = R(), a = R() * 6.283f, j = R(), r = 0.6f + R() * 0.6f, hv = (R() - 0.5f) * 0.1f };
        _dust = new Dust[44];
        for (int i = 0; i < 44; i++) _dust[i] = new Dust { phi = R() * Mathf.PI * 2, r = 0.5f + R() * 0.8f, sp = 0.6f + R() * 0.6f };
        _sparks = new Spark[60];
        for (int i = 0; i < 60; i++) { float a = Mathf.PI * (0.05f + R() * 0.9f), s = 100 + R() * 340; _sparks[i] = new Spark { yaw = R() * 6.283f, h = Mathf.Abs(Mathf.Cos(a)) * s, v = Mathf.Sin(a) * s * 1.2f, life = 0.4f + R() * 0.9f, w = 0.6f + R() * 1.4f }; }
        _t0 = Time.unscaledTime; // tempo reale: alla vittoria (WinUI) il gioco va in pausa, e il fungo di un IdroSbirro appena saltato in aria deve finire lo stesso
    }

    // ---------- simulazione (identica al prototipo) ----------
    static readonly float[] HK = { 1f, 0.8f, 0.62f, 0.45f, 0.28f, 0.12f, 0f };
    static readonly Color[] HC = {
        new Color32(255,248,222,255), new Color32(255,202,112,255), new Color32(242,128,44,255),
        new Color32(168,68,32,255), new Color32(92,60,46,255), new Color32(66,57,53,255), new Color32(54,51,51,255) };
    static Color Heat(float h)
    {
        h = Mathf.Clamp01(h);
        for (int i = 0; i < HK.Length - 1; i++)
            if (h <= HK[i] && h >= HK[i + 1]) return Color.Lerp(HC[i + 1], HC[i], (h - HK[i + 1]) / (HK[i] - HK[i + 1]));
        return HC[HC.Length - 1];
    }
    void Add(bool add, Q q) { if (q.a > 0.002f && q.s > 0.01f) (add ? _qb : _qa).Add(q); }
    void Puff(Vector3 p, float r, Color c, float a, float seed) { Add(false, new Q { p = p, s = r * 1.35f, c = c, a = a, mode = 0, seed = seed }); }
    void Fire(Vector3 p, float r, float h, float a, float seed)
    {
        Puff(p, r, Heat(h), a, seed);
        if (h > 0.55f) Add(true, new Q { p = p, s = r * 0.75f * 1.35f, c = Heat(Mathf.Min(1, h + 0.1f)), a = a * Mathf.Min(1, (h - 0.55f) * 1.6f), mode = 0, seed = seed + 0.5f });
    }

    float Sim(float t)
    {
        float fade = 1 - Mathf.Clamp01((t - 2.3f) / 1.2f);
        float drift = Mathf.Max(0, t - 1.2f) * 10;
        Add(false, new Q { p = new Vector3(0, 0.5f, 0), s = 46, c = new Color(0.165f, 0.114f, 0.086f), a = 0.6f * Mathf.Min(1, t / 0.2f) * fade, mode = 3, flat = true });
        if (t < 0.6f) { float k = t / 0.6f; Add(true, new Q { p = new Vector3(0, 1, 0), s = 40 + 200 * Eo(k), c = new Color(1, 0.98f, 0.9f), a = 0.85f * (1 - k), mode = 2, flat = true }); }

        float dk = Eo(t / 1.4f), dA = 0.55f * Mathf.Min(1, t / 0.1f) * fade * Mathf.Max(0, 1 - Mathf.Max(0, t - 1.2f) / 1.6f);
        var dustC = new Color(176 / 255f, 140 / 255f, 102 / 255f);
        for (int i = 0; i < _dust.Length; i++) { var d = _dust[i]; float Rr = 20 + 95 * dk * d.sp;
            Puff(new Vector3(Mathf.Cos(d.phi) * Rr, 4 + 6 * dk, Mathf.Sin(d.phi) * Rr), (8 + 12 * dk) * d.r, dustC, dA, i * 0.137f); }

        float rise = Eo(t / 0.95f), capH = 185 * rise + drift, grow = Eo(t / 1.1f) * (1 + 0.18f * Mathf.Max(0, t - 1.1f)), capR = 78 * grow;
        if (t > 0.12f)
        {
            float sg = Eo((t - 0.12f) / 0.6f), top = capH - 10;
            for (int i = 0; i < _stem.Length; i++)
            {
                var st = _stem[i];
                float f = (st.f + t * 0.22f) % 1f;
                float prof = 0.24f + 0.36f * Mathf.Pow(1 - f, 6) + 0.22f * Mathf.Pow(f, 6);
                float half = capR * prof * (0.75f + 0.25f * sg), ang = st.a + t * 1.2f, rad = half * (0.35f + 0.65f * st.j);
                float r = (11 + 6 * f + 8 * Mathf.Pow(1 - f, 4)) * st.r * (0.6f + 0.4f * sg) * 0.85f;
                float h = Mathf.Max(0.3f + 0.15f * st.j, 1 - t / 1.25f) - (1 - f) * 0.06f + st.hv;
                Fire(new Vector3(Mathf.Cos(ang) * rad, 6 + top * f * sg, Mathf.Sin(ang) * rad), r, h, 0.8f * fade, 3 + i * 0.211f);
            }
        }
        for (int i = 0; i < _cap.Length; i++)
        {
            var q = _cap[i];
            float th = q.th + t * 1.8f, rad = capR * (0.8f + q.j + Mathf.Cos(th) * 0.25f);
            bool under = Mathf.Sin(th) < 0;
            float b = 1.1f - t / 1.2f - (1 - Mathf.Cos(th)) * 0.08f + q.hv - (under ? 0 : 0.08f);
            float h = under ? Mathf.Max(0.42f + q.hv, b) : Mathf.Max(0.2f, b);
            Fire(new Vector3(Mathf.Cos(q.phi) * rad, capH + Mathf.Sin(th) * capR * 0.22f, Mathf.Sin(q.phi) * rad), 22 * q.r * grow * (0.75f + 0.25f * Mathf.Max(0.3f, rise)), h, 0.85f * fade, 7 + i * 0.173f);
        }
        for (int i = 0; i < _dome.Length; i++) { var d = _dome[i];
            Fire(new Vector3(d.x * capR, capH + d.y * capR, d.z * capR), 19 * d.r * grow, 1.15f - t / 1.3f + d.hv, 0.85f * fade, 11 + i * 0.29f); }
        if (t < 2.6f) Add(true, new Q { p = new Vector3(0, capH - capR * 0.2f, 0), s = capR * 1.6f + 10, c = new Color(1, 0.72f, 0.3f), a = 0.45f * Mathf.Max(0.25f, 1 - t / 1.3f) * fade, mode = 1 });
        foreach (var k in _sparks)
        {
            if (t > k.life) continue; float a = 1 - t / k.life, t2 = Mathf.Max(0, t - 0.04f);
            Add(true, new Q { p = SparkPos(k, t2), p1 = SparkPos(k, t), streak = true, s = k.w, c = new Color(1, (140 + 110 * a) / 255f, (60 + 120 * a) / 255f), a = a, mode = 4 });
        }
        if (t < 0.18f) Add(true, new Q { p = new Vector3(0, 10, 0), s = 110, c = new Color(1, 0.97f, 0.85f), a = 1 - t / 0.18f, mode = 1 });
        return capH;
    }
    static Vector3 SparkPos(Spark k, float tt) => new Vector3(Mathf.Cos(k.yaw) * k.h * tt, 10 + k.v * tt - 320 * tt * tt, Mathf.Sin(k.yaw) * k.h * tt);

    // ---------- costruzione mesh (billboard su CPU) ----------
    void Build(Mesh mesh, List<Q> list, bool sort, Vector3 camPos, Vector3 right, Vector3 up, Vector3 O, float k)
    {
        if (sort)
        {
            for (int i = 0; i < list.Count; i++) { var q = list[i]; q.dist = ((O + q.p * k) - camPos).sqrMagnitude; list[i] = q; }
            list.Sort((a, b) => b.dist.CompareTo(a.dist));
        }
        _v.Clear(); _c.Clear(); _uv.Clear(); _d.Clear(); _i.Clear();
        int n = Mathf.Min(list.Count, 16000);
        for (int i = 0; i < n; i++)
        {
            var q = list[i]; float s = q.s * k; Vector3 c = O + q.p * k;
            if (q.flat) { var ax = Vector3.right * s; var az = Vector3.forward * s; _v.Add(c - ax - az); _v.Add(c + ax - az); _v.Add(c - ax + az); _v.Add(c + ax + az); }
            else if (q.streak) { Vector3 e = O + q.p1 * k, side = Vector3.Cross(e - c, camPos - c).normalized * s; _v.Add(c - side); _v.Add(c + side); _v.Add(e - side); _v.Add(e + side); }
            else { Vector3 rx = right * s, uy = up * s; _v.Add(c - rx - uy); _v.Add(c + rx - uy); _v.Add(c - rx + uy); _v.Add(c + rx + uy); }
            var col = q.c; col.a = Mathf.Min(1, q.a);
            for (int v = 0; v < 4; v++) { _c.Add(col); _d.Add(new Vector2(q.mode, q.seed)); }
            _uv.Add(new Vector2(-1, -1)); _uv.Add(new Vector2(1, -1)); _uv.Add(new Vector2(-1, 1)); _uv.Add(new Vector2(1, 1));
            int b = i * 4; _i.Add(b); _i.Add(b + 2); _i.Add(b + 1); _i.Add(b + 2); _i.Add(b + 3); _i.Add(b + 1);
        }
        mesh.Clear();
        mesh.SetVertices(_v); mesh.SetColors(_c); mesh.SetUVs(0, _uv); mesh.SetUVs(1, _d); mesh.SetTriangles(_i, 0);
        mesh.bounds = new Bounds(O, Vector3.one * 500f);
        list.Clear();
    }

    void LateUpdate()
    {
        float t = (Time.unscaledTime - _t0) * speed;
        if (_cam)
        {
            _cam.localPosition -= _shake;
            float sh = t < 0.7f ? (1 - t / 0.7f) * shakeAmount * scale : 0;
            _shake = Random.insideUnitSphere * sh; _cam.localPosition += _shake;
        }
        if (_light) { _light.intensity = (t < 0.18f ? (1 - t / 0.18f) * 8 : 0) + (t < 2 ? (1 - t / 2) * 2 : 0); _light.range = 20 * scale; }

        if (t >= DUR)
        {
            if (loop && t > DUR + 1) { Boom(); return; }
            if (!loop) { ResetShake(); if (destroyWhenDone) Destroy(gameObject); else enabled = false; }
            return;
        }

        var cam = Camera.main; if (!cam) return;
        Sim(t);
        float k = unitsPerPx * scale; Vector3 O = transform.position; var ct = cam.transform;
        Build(_meshA, _qa, true, ct.position, ct.right, ct.up, O, k);
        Build(_meshB, _qb, false, ct.position, ct.right, ct.up, O, k);
        Graphics.DrawMesh(_meshA, Matrix4x4.identity, _matA, gameObject.layer);
        Graphics.DrawMesh(_meshB, Matrix4x4.identity, _matB, gameObject.layer);
    }

    void ResetShake() { if (_cam) { _cam.localPosition -= _shake; _shake = Vector3.zero; _cam = null; } }
    void OnDestroy()
    {
        ResetShake();
        if (_meshA) Destroy(_meshA); if (_meshB) Destroy(_meshB);
        if (_matA) Destroy(_matA); if (_matB) Destroy(_matB);
    }
}
