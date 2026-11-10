// MiniNuke.cs — Mini fungo atomico (distruzione camionetta) per Unity 2020.3+ (Built-in o URP)
// Uso: MiniNuke.Spawn(camionetta.position);  oppure aggiungi il componente a un GameObject.
// Tutto procedurale: nessuna texture/prefab richiesti. Si autodistrugge a fine effetto.
using UnityEngine;
using UnityEngine.Rendering;

public class MiniNuke : MonoBehaviour
{
    [Header("Esplosione")]
    public float scale = 1f;
    public float speed = 1f;
    [Tooltip("Unità mondo per 'pixel' del prototipo HTML. 0.02 ≈ fungo alto ~3.7m a scale 1")]
    public float unitsPerPx = 0.02f;
    [Tooltip("Moltiplica l'opacità del fumo (anche quello arancione del fuoco), non dei bagliori additivi.")]
    public float opacity = 1f;
    public bool destroyWhenDone = true;

    [Header("Camera shake")]
    public bool cameraShake = true;
    public float shakeAmount = 0.2f;

    [Header("Materiali (opzionali, altrimenti generati)")]
    public Material smokeMaterial;   // alpha blended
    public Material fireMaterial;    // additive

    struct Cap { public float phi, th, r, j, hv; }
    struct Dome { public float x, y, z, r, hv; }
    struct Stem { public float f, a, j, r, hv; }
    struct Dust { public float phi, r, sp; }

    Cap[] _cap; Dome[] _dome; Stem[] _stem; Dust[] _dust;
    ParticleSystem _smoke, _fire, _sparks;
    ParticleSystem.Particle[] _sp = new ParticleSystem.Particle[512], _fp = new ParticleSystem.Particle[512];
    int _sn, _fn;
    Light _light;
    float _t0;
    Vector3 _shakeApplied;
    Transform _cam;

    const float Duration = 3.6f;

    public static MiniNuke Spawn(Vector3 position, float scale = 1f)
    {
        var go = new GameObject("MiniNuke");
        go.transform.position = position;
        var n = go.AddComponent<MiniNuke>();
        n.scale = scale;
        return n;
    }

    void Start()
    {
        Random.InitState(System.Environment.TickCount);
        _cap = new Cap[54];
        for (int i = 0; i < _cap.Length; i++) _cap[i] = new Cap { phi = i / 54f * Mathf.PI * 2 + R() * 0.3f, th = R() * 6.28f, r = 0.5f + R() * 0.7f, j = (R() - 0.5f) * 0.25f, hv = (R() - 0.5f) * 0.12f };
        _dome = new Dome[20];
        for (int i = 0; i < _dome.Length; i++) _dome[i] = new Dome { x = (R() - 0.5f) * 1.3f, z = (R() - 0.5f) * 1.3f, y = 0.1f + R() * 0.6f, r = 0.6f + R() * 0.6f, hv = (R() - 0.5f) * 0.12f };
        _stem = new Stem[70];
        for (int i = 0; i < _stem.Length; i++) _stem[i] = new Stem { f = R(), a = R() * 6.283f, j = R(), r = 0.6f + R() * 0.6f, hv = (R() - 0.5f) * 0.1f };
        _dust = new Dust[44];
        for (int i = 0; i < _dust.Length; i++) _dust[i] = new Dust { phi = R() * Mathf.PI * 2, r = 0.5f + R() * 0.8f, sp = 0.6f + R() * 0.6f };

        var puffTex = BuildPuffTexture(false);
        var denseTex = BuildPuffTexture(true); // il fumo: pieno, se no sulla sabbia chiara sparisce
        if (!smokeMaterial) smokeMaterial = MakeMaterial(false, denseTex);
        if (!fireMaterial) fireMaterial = MakeMaterial(true, puffTex);

        _smoke = MakeManualSystem("Smoke", smokeMaterial, 0);
        _fire = MakeManualSystem("Fire", fireMaterial, 1);
        _sparks = MakeSparks();

        var lgo = new GameObject("Flash");
        lgo.transform.SetParent(transform, false);
        lgo.transform.localPosition = Vector3.up * 1f * scale;
        _light = lgo.AddComponent<Light>();
        _light.type = LightType.Point;
        _light.color = new Color(1f, 0.78f, 0.45f);
        _light.range = 18f * scale;
        _light.intensity = 0;

        if (cameraShake && Camera.main) _cam = Camera.main.transform;
        _t0 = Time.unscaledTime; // tempo reale: alla vittoria (WinUI) il gioco va in pausa, e il fungo di un IdroSbirro appena saltato in aria deve finire lo stesso
    }

    static float R() => Random.value;
    static float Eo(float k) { k = Mathf.Clamp01(k); return 1 - Mathf.Pow(1 - k, 3); }

    void LateUpdate()
    {
        float t = (Time.unscaledTime - _t0) * speed;
        float u = unitsPerPx * scale;
        Vector3 O = transform.position;
        _sn = _fn = 0;

        if (_cam)
        {
            _cam.localPosition -= _shakeApplied;
            float sh = t < 0.7f ? (1 - t / 0.7f) * shakeAmount * scale : 0;
            _shakeApplied = Random.insideUnitSphere * sh;
            _cam.localPosition += _shakeApplied;
        }

        if (t >= Duration)
        {
            _smoke.SetParticles(_sp, 0); _fire.SetParticles(_fp, 0);
            if (_cam) { _cam.localPosition -= _shakeApplied; _shakeApplied = Vector3.zero; _cam = null; }
            if (destroyWhenDone && t > Duration + 1.5f) Destroy(gameObject);
            return;
        }

        float fade = 1 - Mathf.Clamp01((t - 2.3f) / 1.2f);
        float drift = Mathf.Max(0, t - 1.2f) * 10 * u;

        // dust ring
        float dk = Eo(t / 1.4f);
        float dustA = 0.55f * Mathf.Min(1, t / 0.1f) * fade * Mathf.Max(0, 1 - Mathf.Max(0, t - 1.2f) / 1.6f);
        foreach (var d in _dust)
        {
            float Rr = (20 + 95 * dk * d.sp) * u;
            var p = O + new Vector3(Mathf.Cos(d.phi) * Rr, (4 + 6 * dk) * u, Mathf.Sin(d.phi) * Rr);
            AddSmoke(p, (8 + 12 * dk) * d.r * u, new Color32(176, 140, 102, 255), dustA);
        }

        float rise = Eo(t / 0.95f);
        float capH = 185 * u * rise + drift;
        float grow = Eo(t / 1.1f) * (1 + 0.18f * Mathf.Max(0, t - 1.1f));
        float capR = 78 * u * grow;

        // stem
        if (t > 0.12f)
        {
            float sg = Eo((t - 0.12f) / 0.6f);
            float top = capH - 10 * u;
            foreach (var st in _stem)
            {
                float f = (st.f + t * 0.22f) % 1f;
                float prof = 0.24f + 0.36f * Mathf.Pow(1 - f, 6) + 0.22f * Mathf.Pow(f, 6);
                float half = capR * prof * (0.75f + 0.25f * sg);
                float ang = st.a + t * 1.2f;
                float rad = half * (0.35f + 0.65f * st.j);
                var p = O + new Vector3(Mathf.Cos(ang) * rad, 6 * u + top * f * sg, Mathf.Sin(ang) * rad);
                float r = (11 + 6 * f + 8 * Mathf.Pow(1 - f, 4)) * st.r * u * (0.6f + 0.4f * sg) * 0.85f;
                float h = Mathf.Max(0.3f + 0.15f * st.j, 1f - t / 1.25f) - (1 - f) * 0.06f + st.hv;
                AddFire(p, r, h, 0.8f * fade);
            }
        }

        // cap (rolling torus) + dome
        foreach (var q in _cap)
        {
            float th = q.th + t * 1.8f;
            float rad = capR * (0.8f + q.j + Mathf.Cos(th) * 0.25f);
            var p = O + new Vector3(Mathf.Cos(q.phi) * rad, capH + Mathf.Sin(th) * capR * 0.22f, Mathf.Sin(q.phi) * rad);
            bool under = Mathf.Sin(th) < 0;
            float b = 1.1f - t / 1.2f - (1 - Mathf.Cos(th)) * 0.08f + q.hv - (under ? 0 : 0.08f);
            float h = under ? Mathf.Max(0.42f + q.hv, b) : Mathf.Max(0.2f, b);
            AddFire(p, 22 * q.r * u * grow * (0.75f + 0.25f * Mathf.Max(0.3f, rise)), h, 0.85f * fade);
        }
        foreach (var d in _dome)
        {
            var p = O + new Vector3(d.x * capR, capH + d.y * capR, d.z * capR);
            AddFire(p, 19 * d.r * u * grow, 1.15f - t / 1.3f + d.hv, 0.85f * fade);
        }

        // inner glow
        if (t < 2.6f)
        {
            float a = 0.45f * Mathf.Max(0.25f, 1 - t / 1.3f) * fade;
            AddAdd(O + Vector3.up * (capH - capR * 0.2f), (capR * 1.6f + 10 * u) * 2f, new Color(1f, 0.78f, 0.35f), a);
        }

        // shockwave
        if (t < 0.6f)
        {
            float k = t / 0.6f, Rr = 40 * u + 200 * u * Eo(k);
            for (int i = 0; i < 64; i++)
            {
                float a = i / 64f * Mathf.PI * 2;
                AddAdd(O + new Vector3(Mathf.Cos(a) * Rr, 2 * u, Mathf.Sin(a) * Rr), (5 * (1 - k) + 2) * u * 4, new Color(1f, 0.98f, 0.9f), 0.5f * (1 - k));
            }
        }

        // flash
        float fl = t < 0.18f ? 1 - t / 0.18f : 0;
        if (fl > 0) AddAdd(O + Vector3.up * 10 * u, 220 * u, Color.white, fl);
        _light.intensity = fl * 8f + (t < 2f ? (1 - t / 2f) * 2f : 0);

        _smoke.SetParticles(_sp, _sn);
        _fire.SetParticles(_fp, _fn);
    }

    // ---- puff helpers (mirror del prototipo HTML) ----
    void AddFire(Vector3 p, float r, float h, float a)
    {
        AddSmoke(p, r, Heat(h), a);
        if (h > 0.55f) AddAdd(p, r * 0.75f * 2.7f, Heat(Mathf.Min(1, h + 0.1f)), a * Mathf.Min(1, (h - 0.55f) * 1.6f));
    }
    void AddSmoke(Vector3 p, float r, Color32 c, float a)
    {
        if (a <= 0 || _sn >= _sp.Length) return;
        c.a = (byte)(Mathf.Clamp01(a * opacity) * 255);
        _sp[_sn++] = MakeP(p, r * 2.7f, c, _sn);
    }
    void AddAdd(Vector3 p, float size, Color c, float a)
    {
        if (a <= 0 || _fn >= _fp.Length) return;
        // Qui niente opacity: è il bagliore additivo, e moltiplicato (x6 per l'IdroSbirro) il
        // bagliore interno, largo come tutto il fungo, lo sbiancava fino a nasconderne la forma.
        a = Mathf.Clamp01(a);
        _fp[_fn++] = MakeP(p, size, new Color(c.r * a, c.g * a, c.b * a, a), _fn);
    }
    static ParticleSystem.Particle MakeP(Vector3 p, float size, Color32 c, int seed)
    {
        var q = new ParticleSystem.Particle
        {
            position = p, startSize = size, startColor = c,
            startLifetime = 10, remainingLifetime = 10,
            rotation = (seed * 137.5f) % 360f, randomSeed = (uint)seed
        };
        return q;
    }

    static readonly float[] HK = { 1f, 0.8f, 0.62f, 0.45f, 0.28f, 0.12f, 0f };
    static readonly Color32[] HC = {
        new Color32(255,248,222,255), new Color32(255,202,112,255), new Color32(242,128,44,255),
        new Color32(168,68,32,255), new Color32(92,60,46,255), new Color32(66,57,53,255), new Color32(54,51,51,255) };
    static Color32 Heat(float h)
    {
        h = Mathf.Clamp01(h);
        for (int i = 0; i < HK.Length - 1; i++)
            if (h <= HK[i] && h >= HK[i + 1])
                return Color32.Lerp(HC[i + 1], HC[i], (h - HK[i + 1]) / (HK[i] - HK[i + 1]));
        return HC[HC.Length - 1];
    }

    // ---- setup ----
    ParticleSystem MakeManualSystem(string name, Material mat, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true; main.playOnAwake = false; main.maxParticles = 512;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.simulationSpeed = 0.0001f;
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.enabled = false;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat; r.sortMode = ParticleSystemSortMode.Distance; r.sortingOrder = order;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.minParticleSize = 0; r.maxParticleSize = 5f;
        ps.Play();
        return ps;
    }

    ParticleSystem MakeSparks()
    {
        var go = new GameObject("Sparks");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * 10 * unitsPerPx * scale;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false; main.playOnAwake = false; main.maxParticles = 100;
        main.useUnscaledTime = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 1.3f;
        main.startSize = 0.06f * scale;
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.enabled = false;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.98f, 0.8f), 0), new GradientColorKey(new Color(1f, 0.6f, 0.2f), 0.5f), new GradientColorKey(new Color(0.9f, 0.3f, 0.1f), 1) },
            new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = fireMaterial; r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.04f; r.lengthScale = 1.5f; r.sortingOrder = 2;
        ps.Play();
        float u = unitsPerPx * scale;
        for (int i = 0; i < 60; i++)
        {
            float a = Mathf.PI * (0.05f + R() * 0.9f), yaw = R() * Mathf.PI * 2, s = (100 + R() * 340) * u;
            var dir = new Vector3(Mathf.Cos(a) * Mathf.Cos(yaw), Mathf.Sin(a) * 1.2f, Mathf.Cos(a) * Mathf.Sin(yaw));
            ps.Emit(new ParticleSystem.EmitParams
            {
                velocity = dir * s, startLifetime = (0.4f + R() * 0.9f) / speed,
                startSize = (0.6f + R() * 1.4f) * 0.04f * scale, applyShapeToPosition = false
            }, 1);
        }
        return ps;
    }

    static Material MakeMaterial(bool additive, Texture2D tex)
    {
        bool urp = GraphicsSettings.currentRenderPipeline != null;
        Material m;
        if (!additive)
        {
            m = new Material(Shader.Find("Sprites/Default"));
        }
        else if (urp)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 2);
            m.SetFloat("_SrcBlend", (float)BlendMode.One); m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetFloat("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetTexture("_BaseMap", tex);
            m.renderQueue = 3001;
        }
        else
        {
            var s = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Additive");
            m = new Material(s);
        }
        m.mainTexture = tex;
        return m;
    }

    // Puff morbido con luce dall'alto (come il gradiente radiale del prototipo)
    // dense: pieno al centro e sfumato con un bordo largo (senza alpha al quadrato), così le
    // nuvolette coprono la sabbia ma si fondono fra loro invece di sembrare sfere staccate:
    // il fumo deve coprire quello che c'è sotto, il bagliore additivo no.
    static Texture2D BuildPuffTexture(bool dense)
    {
        const int N = 128;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float fx = (x + 0.5f) / N * 2 - 1, fy = (y + 0.5f) / N * 2 - 1;
            float d = Mathf.Sqrt(fx * fx + fy * fy);
            float n = Mathf.PerlinNoise(x * 0.075f, y * 0.075f) * 0.25f; // stessa grana a 128 px
            float a = dense
                ? Mathf.Clamp01(1 - Mathf.SmoothStep(0.75f, 1f, d + n * 0.2f))
                : Mathf.Clamp01(1 - Mathf.SmoothStep(0.45f, 1f, d + n * 0.5f));
            float dl = Mathf.Sqrt((fx + 0.18f) * (fx + 0.18f) + (fy - 0.28f) * (fy - 0.28f));
            float lum = Mathf.Lerp(1f, 0.5f, Mathf.Clamp01(dl / 1.1f)) - n * 0.3f;
            // Il puff del fuoco va col materiale additivo (One One), che somma il colore senza
            // guardare l'alpha: il colore va già moltiplicato per l'alpha, se no gli angoli
            // trasparenti ma grigi del quadrato si vedono e ogni bagliore diventa un quadrato.
            float alpha = dense ? a : a * a;
            if (!dense) lum *= alpha;
            tex.SetPixel(x, y, new Color(lum, lum, lum, alpha));
        }
        tex.Apply();
        return tex;
    }

    void OnDestroy()
    {
        if (_cam) _cam.localPosition -= _shakeApplied;
    }
}
