using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// Apeiron — psychedelic area kill effect.
/// Put on an empty GameObject, assign a material using "Apeiron/AreaVFX", call Cast(position).
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ApeironVFX : MonoBehaviour
{
    [Header("Area")]
    public Material material;
    [Tooltip("Kill radius in world units")] public float radius = 7f;
    [Tooltip("Total effect duration in seconds")] public float duration = 3f;
    [Tooltip("Small lift above the ground to avoid z-fighting")] public float groundOffset = 0.02f;

    [Header("Kill")]
    public LayerMask enemyLayers = ~0;
    [Tooltip("Optional: only colliders with this tag are killed")] public string enemyTag = "";
    public bool destroyEnemies = true;
    public UnityEvent<GameObject> onEnemyKilled;

    [Header("Look")]
    [Tooltip("Off = every cast looks identical. On = each cast differs")] public bool vary = false;
    [Tooltip("Start point of the fractal evolution (s). Higher = more evolved look")] public float phase = 20f;

    [Header("Camera shake")]
    public Transform cameraToShake;
    public float shake = 1f;

    [Header("Debug")]
    public bool castOnStart = false;
    [Tooltip("Press in Play mode to cast at this object's position (None = off)")] public KeyCode testKey = KeyCode.None;

    const float EXT = 2.4f;    // must match shader
    const float TOTAL = 5.3f;  // internal timeline length

    MeshRenderer rend;
    MaterialPropertyBlock mpb;
    float t0 = -1f;
    Vector3 camRest;
    readonly HashSet<Collider> killed = new HashSet<Collider>();
    readonly Collider[] hits = new Collider[256];

    static readonly int idR = Shader.PropertyToID("_R"), idCharge = Shader.PropertyToID("_Charge"),
        idSwirl = Shader.PropertyToID("_Swirl"), idAmt = Shader.PropertyToID("_Amt"), idFxT = Shader.PropertyToID("_FxT"),
        idShockR = Shader.PropertyToID("_ShockR"), idFlash = Shader.PropertyToID("_Flash"), idLocalTime = Shader.PropertyToID("_LocalTime"),
        idNoiseTex = Shader.PropertyToID("_NoiseTex");

    // Random-value table read by the shader's value noise (one texture fetch instead of four sin hashes).
    const int NoiseSize = 256; // must match NOISE_SIZE in the shader
    static Texture2D noiseTex;

    public bool IsPlaying => t0 >= 0f;

    void Awake()
    {
        rend = GetComponent<MeshRenderer>();
        var mf = GetComponent<MeshFilter>();
        if (mf.sharedMesh == null) mf.sharedMesh = BuildQuad();
        if (material != null) rend.sharedMaterial = material;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        rend.enabled = false;
        mpb = new MaterialPropertyBlock();
        if (noiseTex == null) noiseTex = BuildNoise();
    }

    static Texture2D BuildNoise()
    {
        var tex = new Texture2D(NoiseSize, NoiseSize, TextureFormat.R8, false, true)
        {
            name = "ApeironNoise", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat, anisoLevel = 0
        };
        var px = new byte[NoiseSize * NoiseSize];
        new System.Random(1337).NextBytes(px); // fixed seed: same look every run
        tex.SetPixelData(px, 0);
        tex.Apply(false, true);
        return tex;
    }

    void Start() { if (castOnStart) Cast(transform.position); }

    static Mesh BuildQuad()
    {
        var m = new Mesh { name = "ApeironQuad" };
        m.vertices = new[] { new Vector3(-.5f, 0, -.5f), new Vector3(.5f, 0, -.5f), new Vector3(-.5f, 0, .5f), new Vector3(.5f, 0, .5f) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    public void Cast(Vector3 worldPos)
    {
        if (IsPlaying) return;
        transform.position = worldPos + Vector3.up * groundOffset;
        transform.rotation = Quaternion.identity;
        float s = radius * 2f * EXT;
        transform.localScale = new Vector3(s, 1f, s);
        killed.Clear();
        if (cameraToShake) camRest = cameraToShake.localPosition;
        t0 = Time.time;
        rend.enabled = true;
    }

    void Update()
    {
        if (testKey != KeyCode.None && Input.GetKeyDown(testKey)) Cast(transform.position);
        if (!IsPlaying) return;
        float T = (Time.time - t0) * TOTAL / Mathf.Max(0.05f, duration);
        float R = 0, charge = 0, swirl = 0, amt = 0, shockR = -1, flash = 0;
        bool killing = false;

        if (T < 0.6f) { charge = T / 0.6f; swirl = charge; amt = charge * 0.5f; }
        else if (T < 1.4f)
        {
            float e = Mathf.Clamp01((T - 0.6f) / 0.8f);
            const float c1 = 1.70158f, c3 = c1 + 1f;
            R = 1f + c3 * Mathf.Pow(e - 1f, 3) + c1 * Mathf.Pow(e - 1f, 2); // easeOutBack
            swirl = 1f - e; amt = 1f; killing = true;
        }
        else if (T < 3.8f) { R = 1f + 0.04f * Mathf.Sin(T * 7f); swirl = 0.2f * Mathf.Sin(T * 1.7f); amt = 1f; killing = true; }
        else if (T < 4.3f) { float k = (T - 3.8f) / 0.5f; R = 1f - k * k * k; swirl = -1.6f * k; amt = 1f; }
        else if (T < 5.3f) { float k = T - 4.3f; shockR = k * EXT * 0.38f; flash = 1f - k; amt = 1f - k; }
        else { Stop(); return; }

        rend.GetPropertyBlock(mpb);
        mpb.SetFloat(idR, R); mpb.SetFloat(idCharge, charge); mpb.SetFloat(idSwirl, swirl);
        mpb.SetFloat(idAmt, amt); mpb.SetFloat(idFxT, T); mpb.SetFloat(idShockR, shockR); mpb.SetFloat(idFlash, flash);
        mpb.SetFloat(idLocalTime, vary ? -1f : phase + Time.time - t0);
        mpb.SetTexture(idNoiseTex, noiseTex);
        rend.SetPropertyBlock(mpb);

        if (killing) KillInside(R * radius);

        if (cameraToShake)
        {
            float k = (charge * 0.006f + flash * flash * 0.025f + amt * 0.0015f) * shake * radius * 2f;
            cameraToShake.localPosition = camRest + Random.insideUnitSphere * k;
        }
    }

    void KillInside(float r)
    {
        int n = Physics.OverlapSphereNonAlloc(transform.position, r, hits, enemyLayers, QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++)
        {
            var col = hits[i];
            if (col == null || killed.Contains(col)) continue;
            if (!string.IsNullOrEmpty(enemyTag) && !col.CompareTag(enemyTag)) continue;
            killed.Add(col);
            var go = col.attachedRigidbody ? col.attachedRigidbody.gameObject : col.gameObject;
            onEnemyKilled?.Invoke(go);
            if (destroyEnemies) Destroy(go);
        }
    }

    public void Stop()
    {
        t0 = -1f;
        rend.enabled = false;
        if (cameraToShake) cameraToShake.localPosition = camRest;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.9f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
