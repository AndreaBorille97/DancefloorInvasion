using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// Buco nero power-up: nasce, risucchia i Rigidbody per "duration" secondi, poi implode.
[DisallowMultipleComponent]
public class BlackHole : MonoBehaviour
{
    public enum DiskOrientation { Fissa, Precessione, VersoCamera }
    public enum LifeState { Inattivo, Nascita, Attivo, Morte, Morto }

    [Header("Power-up")]
    [Tooltip("Avvia la nascita appena l'oggetto compare (es. dopo Instantiate).")]
    public bool launchOnStart = true;
    [Tooltip("Durata totale in secondi, nascita e morte incluse. 0 = infinito finché non chiami Kill().")]
    [Min(0f)] public float duration = 8f;
    [Min(0.05f)] public float birthDuration = 1.2f;
    [Min(0.05f)] public float deathDuration = 1.5f;
    [Tooltip("Spinta verso l'esterno sugli oggetti vicini all'implosione finale.")]
    public float deathPushForce = 20f;
    [Tooltip("Distrugge il GameObject alla fine. Se falso lo nasconde (riutilizzabile con Launch).")]
    public bool destroyOnDeath = true;

    [Header("Gravità")]
    [Range(0f, 10000f)] public float mass = 1200f;
    [Min(0.1f)] public float influenceRadius = 60f;
    [Min(0.05f)] public float eventHorizonRadius = 1.5f;
    [Range(0f, 2f)] public float swirl = 0.6f;
    [Range(0f, 2f)] public float drag = 0.15f;
    public float maxAcceleration = 3000f;
    public LayerMask affectedLayers = ~0;
    public bool ignoreKinematic = true;
    [Tooltip("Da codice: se restituisce true per un Rigidbody, il buco nero lo ignora (né attrazione né spinta finale). Usato per escludere il Player.")]
    public System.Predicate<Rigidbody> excludeFilter;

    [Header("Inghiottimento")]
    public bool spaghettify = true;
    [Range(0f, 10f)] public float stretchStrength = 3f;
    [Min(0.01f)] public float consumeDuration = 0.35f;
    public bool destroyConsumed = true;

    [Header("Crescita")]
    public bool growOnConsume = false;
    public float growthPerUnitMass = 0.01f;
    public float maxHorizonRadius = 6f;

    [Header("Visual")]
    public Material horizonMaterial;
    [Tooltip("I colori/Doppler/turbolenza di questo materiale vengono usati anche dalla lente.")]
    public Material diskMaterial;
    [Range(2.5f, 15f)] public float diskSize = 5f;
    [Range(-5f, 5f)] public float diskSpin = 1f;

    [Header("Orientamento disco")]
    public DiskOrientation diskOrientation = DiskOrientation.VersoCamera;
    [Range(0f, 60f)] public float edgeAngle = 12f;
    [Range(0f, 85f)] public float tilt = 35f;
    public float precessionSpeed = 25f;
    [Range(0f, 1f)] public float rockAmount = 0.5f;
    public float orientSmoothing = 3f;
    public Camera targetCamera;
    public bool swirlFollowsDisk = false;

    [Header("Lente gravitazionale")]
    [Tooltip("Ray-tracing della luce: disco avvolto attorno al buco, ombra e anello.")]
    public bool lensing = true;
    public Material lensMaterial;
    [Range(0f, 2f)] public float lensStrength = 1f;
    [Range(1f, 40f)] public float lensSize = 14f;
    [Tooltip("Raggio dell'effetto visibile (spirale/colori), in raggi d'orizzonte. 0 = automatico: un po' oltre il disco (diskSize x 1.2). Non può superare lensSize.")]
    [Min(0f)] public float hazeRadius = 0f;

    [Header("Psichedelia")]
    [Range(0f, 1f)] public float psyche = 0.85f;

    [Header("Eventi")]
    public UnityEvent<GameObject> onConsumed;
    public UnityEvent onBirth, onAlive, onDeath, onDied;

    public int ConsumedCount { get; private set; }
    public LifeState State { get; private set; } = LifeState.Inattivo;
    public float CurrentHorizonRadius => eventHorizonRadius * _hScale;
    // Raggio dello svarione a riposo, in raggi d'orizzonte (vedi hazeRadius).
    float HazeBase => hazeRadius > 0f ? hazeRadius : diskSize * 1.2f;
    bool Running => State == LifeState.Nascita || State == LifeState.Attivo || State == LifeState.Morte;

    readonly Collider[] _hits = new Collider[1024];
    readonly HashSet<Rigidbody> _seen = new HashSet<Rigidbody>();
    Transform _horizonVis, _diskVis, _lensVis;
    Renderer _horizonRend, _diskRend, _lensRend;
    MaterialPropertyBlock _mpb;
    bool _built, _pushed;
    float _t, _aliveTime = -1f, _hScale, _diskGrow, _spinBoost = 1f, _gravMul, _flash, _shockR, _shockAmp, _psyBoost, _phase;

    static readonly int SpinId = Shader.PropertyToID("_Spin"), InnerId = Shader.PropertyToID("_Inner"),
        PsyId = Shader.PropertyToID("_Psyche"), PhaseId = Shader.PropertyToID("_Phase"),
        HazeId = Shader.PropertyToID("_HazeRadius"), HorizonId = Shader.PropertyToID("_Horizon"), HScaleId = Shader.PropertyToID("_HScale"),
        StrengthId = Shader.PropertyToID("_Strength"), LensRId = Shader.PropertyToID("_LensRadius"),
        DInId = Shader.PropertyToID("_DiskInner"), DOutId = Shader.PropertyToID("_DiskOuter"),
        DIntId = Shader.PropertyToID("_DiskIntensity"), DNId = Shader.PropertyToID("_DiskN"), DUId = Shader.PropertyToID("_DiskU"),
        FlashId = Shader.PropertyToID("_Flash"), ShockRId = Shader.PropertyToID("_ShockR"), ShockAId = Shader.PropertyToID("_ShockAmp");
    static readonly string[] CopyColors = { "_HotColor", "_MidColor", "_CoolColor" };
    static readonly string[] CopyFloats = { "_Doppler", "_NoiseScale" };

    void Start()
    {
        Build();
        if (launchOnStart) Launch();
        else SetVisible(false);
    }

    // ---------- API power-up ----------
    public void Launch() => Launch(duration);

    public void Launch(float totalDuration)
    {
        if (!_built) Build();
        _aliveTime = totalDuration > 0f ? Mathf.Max(0f, totalDuration - birthDuration - deathDuration) : -1f;
        _t = 0f; _pushed = false; _hScale = 0f; _diskGrow = 0f;
        State = LifeState.Nascita;
        SetVisible(true);
        onBirth?.Invoke();
    }

    public void Kill()
    {
        if (State != LifeState.Nascita && State != LifeState.Attivo) return;
        _t = 0f; _pushed = false;
        State = LifeState.Morte;
        onDeath?.Invoke();
    }

    void Update()
    {
        if (!_built || !Running) return;
        Tick(Time.deltaTime);
        if (Running) UpdateVisuals();
    }

    void Tick(float dt)
    {
        _t += dt; _flash = 0f; _shockAmp = 0f; _psyBoost = 0f;
        if (State == LifeState.Nascita)
        {
            float t = Mathf.Clamp01(_t / birthDuration);
            _flash = 2f * Gauss(t, 0.15f, 0.12f);
            _hScale = OutBack(C01((t - 0.1f) / 0.55f));
            _diskGrow = OutCubic(C01((t - 0.25f) / 0.75f));
            _spinBoost = 1f + 5f * (1f - _diskGrow);
            _gravMul = C01((t - 0.2f) / 0.8f);
            float k = C01((t - 0.1f) / 0.9f);
            _shockR = 0.5f + HazeBase * OutCubic(k); // proporzionata allo svarione, non a lensSize
            _shockAmp = t > 0.1f ? 1f - k : 0f;
            if (t >= 1f) { State = LifeState.Attivo; _t = 0f; onAlive?.Invoke(); }
        }
        else if (State == LifeState.Attivo)
        {
            _hScale = 1f; _diskGrow = 1f; _spinBoost = 1f; _gravMul = 1f;
            if (_aliveTime >= 0f && _t >= _aliveTime) Kill();
        }
        else if (State == LifeState.Morte)
        {
            float t = Mathf.Clamp01(_t / deathDuration), a = C01(t / 0.7f);
            _spinBoost = 1f + 7f * a * a;
            _diskGrow = 1f - a * a * a;
            float wobble = 1f + 0.08f * Mathf.Sin(_t * 40f) * C01(t / 0.6f);
            _hScale = Mathf.Max(0f, wobble * (1f - InBack(C01((t - 0.55f) / 0.25f))));
            _gravMul = t < 0.8f ? 1f + t : 0f;
            _psyBoost = 0.3f * t;
            _flash = 3f * Gauss(t, 0.82f, 0.07f);
            float k = C01((t - 0.78f) / 0.22f);
            _shockR = 1f + HazeBase * 1.3f * OutCubic(k);
            _shockAmp = t > 0.78f ? 1.4f * (1f - k) : 0f;
            if (!_pushed && t >= 0.8f) { _pushed = true; Push(); }
            if (t >= 1f) Finish();
        }
    }

    void Finish()
    {
        State = LifeState.Morto;
        _hScale = 0f; _diskGrow = 0f; _gravMul = 0f;
        onDied?.Invoke();
        if (destroyOnDeath) Destroy(gameObject);
        else SetVisible(false);
    }

    void Push()
    {
        if (deathPushForce <= 0f) return;
        Vector3 c = transform.position;
        int n = Physics.OverlapSphereNonAlloc(c, influenceRadius, _hits, affectedLayers, QueryTriggerInteraction.Ignore);
        _seen.Clear();
        for (int i = 0; i < n; i++)
        {
            Rigidbody rb = _hits[i].attachedRigidbody;
            if (rb == null || !_seen.Add(rb) || rb.isKinematic) continue;
            if (excludeFilter != null && excludeFilter(rb)) continue;
            if (rb.TryGetComponent(out BlackHoleConsumable bc) && bc.IsBeingConsumed) continue;
            Vector3 d = rb.worldCenterOfMass - c;
            float r = Mathf.Max(0.01f, d.magnitude);
            rb.AddForce(d / r * deathPushForce * (1f - r / influenceRadius), ForceMode.VelocityChange);
        }
    }

    // ---------- Fisica ----------
    void FixedUpdate()
    {
        if (!Running || _gravMul <= 0f) return;
        Vector3 c = transform.position;
        Vector3 up = (swirlFollowsDisk && _diskVis) ? _diskVis.forward : transform.up;
        float hr = CurrentHorizonRadius;
        bool canEat = _hScale > 0.15f;
        float m = mass * _gravMul;
        int n = Physics.OverlapSphereNonAlloc(c, influenceRadius, _hits, affectedLayers, QueryTriggerInteraction.Ignore);
        _seen.Clear();
        for (int i = 0; i < n; i++)
        {
            Rigidbody rb = _hits[i].attachedRigidbody;
            if (rb == null || !_seen.Add(rb)) continue;
            if (ignoreKinematic && rb.isKinematic) continue;
            if (excludeFilter != null && excludeFilter(rb)) continue;
            if (rb.transform.IsChildOf(transform)) continue;
            if (rb.TryGetComponent(out BlackHoleConsumable bc) && bc.IsBeingConsumed) continue;

            Vector3 d = c - rb.worldCenterOfMass;
            float r = d.magnitude;
            if (canEat && r <= hr) { Consume(rb); continue; }

            Vector3 dir = d / Mathf.Max(r, 1e-4f);
            float a = Mathf.Min(m / Mathf.Max(r * r, 1e-4f), maxAcceleration);
            Vector3 acc = dir * a;
            Vector3 tan = Vector3.Cross(up, -dir);
            if (swirl > 0f && tan.sqrMagnitude > 1e-4f) acc += tan.normalized * a * swirl * 0.35f;
            acc -= BlackHoleUtil.GetVelocity(rb) * drag * (1f - Mathf.Clamp01(r / influenceRadius));
            rb.AddForce(acc, ForceMode.Acceleration);
        }
    }

    public void Consume(Rigidbody rb)
    {
        if (rb.TryGetComponent(out BlackHoleConsumable bc) && bc.IsBeingConsumed) return;
        if (bc == null) bc = rb.gameObject.AddComponent<BlackHoleConsumable>();
        ConsumedCount++;
        if (growOnConsume) eventHorizonRadius = Mathf.Min(maxHorizonRadius, eventHorizonRadius + rb.mass * growthPerUnitMass);
        bc.Begin(this);
        onConsumed?.Invoke(rb.gameObject);
    }

    // ---------- Visual ----------
    void Build()
    {
        if (_built) return;
        _built = true;
        if (horizonMaterial == null) horizonMaterial = MakeMat("BlackHole/Horizon");
        if (diskMaterial == null) diskMaterial = MakeMat("BlackHole/AccretionDisk");
        if (lensMaterial == null) lensMaterial = MakeMat("BlackHole/Lens");

        _horizonRend = MakePart(PrimitiveType.Sphere, "EventHorizon", horizonMaterial, out _horizonVis);
        _diskRend = MakePart(PrimitiveType.Quad, "AccretionDisk", diskMaterial, out _diskVis);
        _diskVis.localRotation = Quaternion.Euler(90f, 0f, 0f);
        if (lensMaterial) _lensRend = MakePart(PrimitiveType.Quad, "GravitationalLens", lensMaterial, out _lensVis);
        _mpb = new MaterialPropertyBlock();
    }

    Renderer MakePart(PrimitiveType type, string name, Material mat, out Transform tr)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        g.name = name;
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(transform, false);
        Renderer r = g.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        if (mat) r.sharedMaterial = mat;
        tr = g.transform;
        return r;
    }

    void SetVisible(bool v)
    {
        if (!_built) return;
        _horizonRend.enabled = v && !lensing;
        _diskRend.enabled = v && !lensing;
        if (_lensRend) _lensRend.enabled = v && lensing;
    }

    void UpdateVisuals()
    {
        float ps = Mathf.Max(1e-4f, transform.lossyScale.x);
        float H = eventHorizonRadius;
        float rs = _hScale;
        float inner = Mathf.Max(rs * 1.05f, 2f * rs);
        float outer = inner + Mathf.Max(0f, diskSize - 2f) * _diskGrow;
        float psy = Mathf.Clamp01(psyche + _psyBoost);
        bool useLens = lensing && _lensRend != null;
        _phase += Time.deltaTime * diskSpin * _spinBoost;
        OrientDisk();

        _horizonRend.enabled = !useLens && rs > 0.001f;
        _horizonVis.localScale = Vector3.one * Mathf.Max(1e-4f, H * rs * 2f / ps);
        _horizonRend.GetPropertyBlock(_mpb);
        _mpb.SetFloat(PsyId, psy);
        _horizonRend.SetPropertyBlock(_mpb);

        _diskRend.enabled = !useLens && _diskGrow > 0.002f;
        _diskVis.localScale = Vector3.one * Mathf.Max(1e-4f, H * outer * 2f / ps);
        _diskRend.GetPropertyBlock(_mpb);
        _mpb.SetFloat(SpinId, diskSpin);
        _mpb.SetFloat(PhaseId, _phase);
        _mpb.SetFloat(InnerId, Mathf.Clamp(inner / Mathf.Max(outer, 1e-4f), 0.05f, 0.95f));
        _mpb.SetFloat(PsyId, psy);
        _diskRend.SetPropertyBlock(_mpb);

        if (_lensRend == null) return;
        _lensRend.enabled = useLens;
        if (!useLens) return;
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam) _lensVis.rotation = cam.transform.rotation;
        // L'onda d'urto allarga la lente solo mentre c'è: prima restava allargata per tutta la vita attiva.
        bool shocking = _shockAmp > 0f;
        float R = shocking ? Mathf.Max(lensSize, _shockR + 2f) : lensSize;
        float haze = HazeBase;
        if (shocking) haze = Mathf.Max(haze, _shockR + 1.5f);
        haze = Mathf.Min(haze, R);
        _lensVis.localScale = Vector3.one * (H * haze * 2.4f / ps); // il quad copre solo lo svarione: fuori non serve

        _lensRend.GetPropertyBlock(_mpb);
        if (diskMaterial)
        {
            foreach (string c in CopyColors) if (diskMaterial.HasProperty(c)) _mpb.SetColor(c, diskMaterial.GetColor(c));
            foreach (string f in CopyFloats) if (diskMaterial.HasProperty(f)) _mpb.SetFloat(f, diskMaterial.GetFloat(f));
        }
        float baseI = diskMaterial && diskMaterial.HasProperty("_Intensity") ? diskMaterial.GetFloat("_Intensity") : 1.5f;
        _mpb.SetFloat(HorizonId, H);
        _mpb.SetFloat(HScaleId, rs);
        _mpb.SetFloat(HazeId, haze);
        _mpb.SetFloat(StrengthId, lensStrength);
        _mpb.SetFloat(LensRId, R);
        _mpb.SetFloat(DInId, inner);
        _mpb.SetFloat(DOutId, outer);
        _mpb.SetFloat(DIntId, baseI * Mathf.Clamp01(_diskGrow * 3f));
        _mpb.SetFloat(PhaseId, _phase);
        _mpb.SetFloat(SpinId, diskSpin);
        _mpb.SetFloat(PsyId, psy);
        _mpb.SetFloat(FlashId, _flash);
        _mpb.SetFloat(ShockRId, _shockR);
        _mpb.SetFloat(ShockAId, _shockAmp);
        _mpb.SetVector(DNId, _diskVis.forward);
        _mpb.SetVector(DUId, _diskVis.right);
        _lensRend.SetPropertyBlock(_mpb);
    }

    void OrientDisk()
    {
        Vector3 up = transform.up;
        Vector3 n = up;
        if (diskOrientation == DiskOrientation.Precessione)
        {
            Vector3 side = Vector3.Cross(up, transform.forward).normalized;
            n = Quaternion.AngleAxis(Time.time * precessionSpeed, up) * (Quaternion.AngleAxis(tilt, side) * up);
        }
        else if (diskOrientation == DiskOrientation.VersoCamera)
        {
            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam != null)
            {
                Vector3 d = cam.orthographic ? -cam.transform.forward : (cam.transform.position - transform.position).normalized;
                Vector3 p = up - Vector3.Dot(up, d) * d;
                if (p.sqrMagnitude < 1e-6f) p = cam.transform.up;
                p.Normalize();
                float a = (90f - edgeAngle) * Mathf.Deg2Rad;
                n = d * Mathf.Cos(a) + p * Mathf.Sin(a);
                if (rockAmount > 0f) n = Quaternion.AngleAxis(Mathf.Sin(Time.time * precessionSpeed * 0.02f) * 30f * rockAmount, d) * n;
            }
        }
        Vector3 hint = Mathf.Abs(Vector3.Dot(n.normalized, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
        _diskVis.rotation = Quaternion.Slerp(_diskVis.rotation, Quaternion.LookRotation(n, hint), Mathf.Clamp01(Time.deltaTime * orientSmoothing));
    }

    static Material MakeMat(string shaderName)
    {
        Shader s = Shader.Find(shaderName);
        if (s == null) { Debug.LogWarning("BlackHole: shader non trovato: " + shaderName); return null; }
        return new Material(s);
    }

    static float C01(float x) => Mathf.Clamp01(x);
    static float Gauss(float x, float c, float w) { float z = (x - c) / w; return Mathf.Exp(-z * z); }
    static float OutCubic(float x) { float y = 1f - x; return 1f - y * y * y; }
    static float OutBack(float x) { const float c = 1.70158f; float y = x - 1f; return 1f + (c + 1f) * y * y * y + c * y * y; }
    static float InBack(float x) { const float c = 1.70158f; return (c + 1f) * x * x * x - c * x * x; }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, influenceRadius);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, eventHorizonRadius);
    }
}

public static class BlackHoleUtil
{
    public static Vector3 GetVelocity(Rigidbody rb)
    {
#if UNITY_6000_0_OR_NEWER
        return rb.linearVelocity;
#else
        return rb.velocity;
#endif
    }

    public static void SetVelocity(Rigidbody rb, Vector3 v)
    {
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = v;
#else
        rb.velocity = v;
#endif
    }
}
