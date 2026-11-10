// WaterJet.cs — Getto idrante nemico (inizio / getto / fine) per Unity 2020.3+
// Aggiungere a un GameObject vuoto. Richiede LineRenderer (aggiunto in automatico).
// Uso: jet.Fire(nozzleTransform, targetPosition); jet.Stop();
using System;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class WaterJet : MonoBehaviour
{
    public enum Phase { Idle, Start, Sustain, End }

    [Header("Punti")]
    public Transform nozzle;                 // ugello della camionetta
    public Transform targetTransform;        // opzionale: segue un bersaglio
    public Vector3 targetPoint;              // usato se targetTransform è null

    [Header("Getto")]
    [Tooltip("Altezza arco in proporzione alla distanza ugello→bersaglio")]
    [Range(0f, 0.6f)] public float arcHeight = 0.12f;
    public float thickness = 0.25f;          // spessore base in unità mondo
    public Color waterColor = new Color32(0x6c, 0xc4, 0xea, 0xff);
    [Range(8, 128)] public int segments = 60;
    public float wobble = 0.08f;             // oscillazione del punto di controllo
    public float streakScroll = 3f;          // velocità strisce lungo il getto

    [Header("Tempi (s)")]
    public float startTime = 0.45f;
    public float sustainTime = 1.6f;         // <=0 = infinito finché non chiami Stop()
    public float endTime = 0.55f;
    [Range(0f, 1f)] public float endPressureDrop = 0.45f;

    [Header("Particelle (opzionali)")]
    public ParticleSystem headSpray;         // fronte d'acqua durante l'inizio
    public ParticleSystem impactSplash;      // impatto continuo sul bersaglio
    public ParticleSystem finalSplash;       // burst a fine colpo (Emit)
    public int finalSplashCount = 26;        // gocce del burst finale
    public ParticleSystem nozzleMist;        // nebbia all'ugello
    public ParticleSystem beamSpray;         // gocce che si staccano lungo il getto (Emit, rate 0 nel sistema)
    public float beamSprayRate = 120f;       // gocce al secondo per metro di getto
    public float beamSpraySpeed = 1.6f;      // velocità laterale delle gocce

    public event Action OnHitStart;          // l'acqua raggiunge il bersaglio (inizia danno)
    public event Action OnHitEnd;            // l'ultima acqua arriva (fine danno)

    public Phase CurrentPhase { get; private set; } = Phase.Idle;
    public bool IsHitting => CurrentPhase == Phase.Sustain || (CurrentPhase == Phase.End && _tail < 0.98f);

    LineRenderer _lr;
    Material _mat;
    float _t0, _stopAt, _tail, _head, _beamAccum;
    bool _hitFired;
    Vector3[] _pts;

    void Awake()
    {
        _lr = GetComponent<LineRenderer>();
        _lr.useWorldSpace = true;
        _lr.textureMode = LineTextureMode.Tile;
        _lr.numCapVertices = 4;
        _lr.alignment = LineAlignment.View;
        if (_lr.sharedMaterial == null || _lr.sharedMaterial.name.StartsWith("Default"))
        {
            _mat = new Material(MaterialLibrary.SpriteDefault);
            _mat.mainTexture = BuildStreakTexture();
            _lr.material = _mat;
        }
        else _mat = _lr.material;
        _lr.positionCount = 0;
        _pts = new Vector3[segments + 1];
    }

    public void Fire(Transform from, Vector3 to)
    {
        nozzle = from; targetTransform = null; targetPoint = to; Fire();
    }
    public void Fire(Transform from, Transform to)
    {
        nozzle = from; targetTransform = to; Fire();
    }
    public void Fire()
    {
        _t0 = Time.time;
        _stopAt = sustainTime > 0 ? _t0 + startTime + sustainTime : float.MaxValue;
        _hitFired = false; _tail = 0; _head = 0;
        CurrentPhase = Phase.Start;
        Play(headSpray); Play(nozzleMist);
    }
    public void Stop()
    {
        if (CurrentPhase == Phase.Idle || CurrentPhase == Phase.End) return;
        _stopAt = Mathf.Max(Time.time, _t0 + 0.12f);
    }

    void LateUpdate()
    {
        if (CurrentPhase == Phase.Idle || nozzle == null) { _lr.positionCount = 0; return; }

        float now = Time.time, e = now - _t0, press = 1f;
        _head = 1f - Mathf.Pow(1f - Mathf.Clamp01(e / startTime), 3f);
        _tail = 0f;
        if (_head < 1f) CurrentPhase = Phase.Start;
        else if (CurrentPhase == Phase.Start)
        {
            CurrentPhase = Phase.Sustain;
            Stop(headSpray); Play(impactSplash);
            if (!_hitFired) { _hitFired = true; OnHitStart?.Invoke(); }
        }
        if (now >= _stopAt)
        {
            if (CurrentPhase != Phase.End) { CurrentPhase = Phase.End; Stop(nozzleMist); }
            float f = Mathf.Clamp01((now - _stopAt) / endTime);
            _tail = f * f; press = 1f - endPressureDrop * f;
            if (f >= 1f) { Finish(); return; }
        }

        Vector3 N = nozzle.position;
        Vector3 T = targetTransform ? targetTransform.position : targetPoint;
        float len = Vector3.Distance(N, T);
        float h = arcHeight * len * press;
        Vector3 C = (N + T) * 0.5f + Vector3.up * (2f * h)
                  + new Vector3(Mathf.Sin(now * 11f), Mathf.Sin(now * 19f), 0f) * wobble;

        if (_pts.Length != segments + 1) _pts = new Vector3[segments + 1];
        var width = new AnimationCurve();
        for (int i = 0; i <= segments; i++)
        {
            float k = (float)i / segments;
            float t = Mathf.Lerp(_tail, _head, k);
            _pts[i] = Bezier(N, C, T, t);
            float taperTail = _tail > 0 ? Mathf.Clamp01(k * 6f) : 1f;
            float taperHead = _head < 1f ? Mathf.Clamp01((1f - k) * 10f + 0.55f) : 1f;
            float w = (0.75f + 0.5f * t) * taperTail * taperHead * (0.85f + 0.15f * press);
            if (i % 4 == 0 || i == segments) width.AddKey(k, w);
        }
        _lr.positionCount = _pts.Length;
        _lr.SetPositions(_pts);
        _lr.widthCurve = width;
        _lr.widthMultiplier = thickness;
        _lr.startColor = _lr.endColor = waterColor;
        _mat.mainTextureOffset = new Vector2(-now * streakScroll, 0);

        if (headSpray) headSpray.transform.position = _pts[segments];
        if (impactSplash) impactSplash.transform.position = T;
        if (nozzleMist) nozzleMist.transform.SetPositionAndRotation(N, Quaternion.LookRotation(Tangent(N, C, T, 0)));
        if (CurrentPhase == Phase.End && _tail > 0.95f) Stop(impactSplash);
        EmitBeamSpray(T - N, len * (_head - _tail) * press);
    }

    // Gocce sparse in punti a caso del tratto visibile, spinte di lato rispetto al getto e un po'
    // in avanti: la gravità del sistema le fa ricadere.
    void EmitBeamSpray(Vector3 along, float visibleLength)
    {
        if (!beamSpray || beamSprayRate <= 0f || segments < 1) return;
        _beamAccum += beamSprayRate * visibleLength * Time.deltaTime;
        int count = (int)_beamAccum;
        _beamAccum -= count;

        Vector3 dir = along.sqrMagnitude > 1e-6f ? along.normalized : Vector3.forward;
        var p = new ParticleSystem.EmitParams();
        for (int n = 0; n < count; n++)
        {
            float s = UnityEngine.Random.value * segments;
            int i = Mathf.Min((int)s, segments - 1);
            p.position = Vector3.Lerp(_pts[i], _pts[i + 1], s - i);
            Vector3 side = Vector3.ProjectOnPlane(UnityEngine.Random.onUnitSphere, dir).normalized;
            p.velocity = side * beamSpraySpeed * UnityEngine.Random.Range(0.4f, 1f)
                       + dir * beamSpraySpeed * UnityEngine.Random.Range(0.5f, 1.5f);
            beamSpray.Emit(p, 1);
        }
    }

    void Finish()
    {
        Vector3 T = targetTransform ? targetTransform.position : targetPoint;
        if (finalSplash) { finalSplash.transform.position = T; finalSplash.Emit(finalSplashCount); }
        Stop(headSpray); Stop(impactSplash); Stop(nozzleMist);
        _lr.positionCount = 0;
        CurrentPhase = Phase.Idle;
        OnHitEnd?.Invoke();
    }

    static Vector3 Bezier(Vector3 a, Vector3 c, Vector3 b, float t)
    { float u = 1 - t; return u * u * a + 2 * u * t * c + t * t * b; }
    static Vector3 Tangent(Vector3 a, Vector3 c, Vector3 b, float t)
    { return (2 * (1 - t) * (c - a) + 2 * t * (b - c)).normalized; }
    static void Play(ParticleSystem p) { if (p && !p.isPlaying) p.Play(); }
    static void Stop(ParticleSystem p) { if (p) p.Stop(true, ParticleSystemStopBehavior.StopEmitting); }

    // Texture procedurale: bordo morbido + nucleo chiaro + strisce bianche che scorrono
    static Texture2D BuildStreakTexture()
    {
        const int W = 64, H = 32;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            float v = Mathf.Abs((y + 0.5f) / H * 2f - 1f);          // 0 centro, 1 bordo
            float body = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.55f, 1f, v));
            float glow = (1f - v) * 0.25f;
            float core = Mathf.Clamp01(1f - Mathf.Abs(v - 0.15f) * 6f) * 0.8f;
            float streak = (x % 32 < 7 && v < 0.35f) ? 0.6f : 0f;
            float white = Mathf.Clamp01(core + streak);
            Color c = Color.Lerp(Color.white * 0.85f, Color.white, white);
            c.a = Mathf.Clamp01(body + glow);
            c = Color.Lerp(c, Color.white, white); c.a = Mathf.Clamp01(body + glow);
            tex.SetPixel(x, y, c);
        }
        tex.Apply();
        return tex;
    }
}
