using System.Collections.Generic;
using UnityEngine;

/// Anello sonico 3D: parte piccolo dalla cassa e si allarga a cono mentre avanza. Spinge ciò che attraversa.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SonicRingShot : MonoBehaviour
{
    [Header("Forma")]
    public float startRadius = 0.25f;
    public float endRadius = 3f;
    public float range = 16f;
    public float duration = 0.69f;
    public float length = 1.6f;
    [Range(0f, 0.15f)] public float wobble = 0.06f;
    public int segments = 96;

    [Header("Scia")]
    [Range(0, 6)] public int trailEchoes = 4;
    [Range(0f, 1f)] public float trailStrength = 0.32f;
    public float trailLag = 0.065f;   // secondi di ritardo tra un'eco e la successiva

    [Header("Spinta")]
    public float knockback = 12f;
    public LayerMask hitMask = ~0;

    [Header("Visibilità")]
    [Tooltip("Moltiplica la luminosità dell'anello e della scia (1 = normale). Usato per le copie olografiche tenui del khole.")]
    [Range(0f, 1f)] public float opacity = 1f;

    float t, power = 1f, delay;
    Vector3 origin, dir;
    Mesh mesh; MeshRenderer mr; MaterialPropertyBlock mpb;
    Vector3[] baseVerts, verts;
    readonly HashSet<Collider> hit = new HashSet<Collider>();
    readonly Collider[] buf = new Collider[32];
    Transform[] echoes; MeshRenderer[] echoMr;
    static readonly int FadeID = Shader.PropertyToID("_Fade"), PowerID = Shader.PropertyToID("_Power");

    public void Init(float power, float delay = 0f)
    {
        this.power = power; this.delay = delay;
        origin = transform.position; dir = transform.forward;
        mr.enabled = delay <= 0f;
    }

    void Awake()
    {
        mr = GetComponent<MeshRenderer>(); mpb = new MaterialPropertyBlock();
        origin = transform.position; dir = transform.forward;
        int n = segments + 1;
        baseVerts = new Vector3[n * 2]; verts = new Vector3[n * 2];
        var uv = new Vector2[n * 2]; var tr = new int[segments * 6];
        for (int i = 0; i <= segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f, u = i / (float)segments;
            Vector3 d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            baseVerts[i * 2] = d * 0.9f + Vector3.back * 0.5f; uv[i * 2] = new Vector2(u, 0f);
            baseVerts[i * 2 + 1] = d + Vector3.forward * 0.5f; uv[i * 2 + 1] = new Vector2(u, 1f);
            if (i < segments) { int b = i * 2, k = i * 6; tr[k] = b; tr[k + 1] = b + 2; tr[k + 2] = b + 1; tr[k + 3] = b + 1; tr[k + 4] = b + 2; tr[k + 5] = b + 3; }
        }
        mesh = new Mesh { name = "SonicRing" };
        mesh.MarkDynamic();
        mesh.vertices = baseVerts; mesh.uv = uv; mesh.triangles = tr;
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 3f);
        GetComponent<MeshFilter>().sharedMesh = mesh;

        echoes = new Transform[trailEchoes]; echoMr = new MeshRenderer[trailEchoes];
        for (int e = 0; e < trailEchoes; e++)
        {
            var go = new GameObject("Echo" + e);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            echoMr[e] = go.AddComponent<MeshRenderer>(); echoMr[e].sharedMaterial = mr.sharedMaterial; echoMr[e].enabled = false;
            echoes[e] = go.transform;
        }
    }

    void OnDestroy() { if (echoes != null) foreach (var e in echoes) if (e) Destroy(e.gameObject); }

    void Pose(Transform tr, float tt, float shrink)
    {
        float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(tt / duration), 2.2f);
        float r = Mathf.Lerp(startRadius, endRadius, k) * shrink;
        tr.SetPositionAndRotation(origin + dir * k * range, Quaternion.LookRotation(dir));
        tr.localScale = new Vector3(r, r, length * (0.8f + 0.3f * power) * 0.6f);
    }

    void Update()
    {
        if (delay > 0f) { delay -= Time.deltaTime; if (delay > 0f) return; mr.enabled = true; }
        t += Time.deltaTime;
        float p = t / duration;
        if (p >= 1f) { Destroy(gameObject); return; }

        float k = 1f - Mathf.Pow(1f - p, 2.2f);
        float dist = k * range;
        float r = Mathf.Lerp(startRadius, endRadius, k);              // raggio ∝ distanza = cono
        transform.SetPositionAndRotation(origin + dir * dist, Quaternion.LookRotation(dir));
        transform.localScale = new Vector3(r, r, length * (0.8f + 0.3f * power));

        // ondulazione morbida lungo la circonferenza
        float time = Time.time;
        for (int i = 0; i < baseVerts.Length; i++)
        {
            Vector3 v = baseVerts[i];
            float a = Mathf.Atan2(v.y, v.x);
            float w = 1f + Mathf.Sin(a * 12f + time * 22f) * wobble * 0.5f + Mathf.Sin(a * 5f - time * 13f) * wobble * 0.4f;
            verts[i] = new Vector3(v.x * w, v.y * w, v.z);
        }
        mesh.vertices = verts;

        DoDamage(r, p);

        float fade = (1f - Mathf.Pow(p, 1.5f)) * opacity;
        mpb.SetFloat(PowerID, power);
        for (int e = 0; e < echoes.Length; e++)
        {
            float et = t - (e + 1) * trailLag;
            echoMr[e].enabled = et > 0f && trailStrength > 0f;
            if (!echoMr[e].enabled) continue;
            Pose(echoes[e], et, 0.97f);
            mpb.SetFloat(FadeID, fade * trailStrength * Mathf.Pow(0.6f, e));
            echoMr[e].SetPropertyBlock(mpb);
        }
        mpb.SetFloat(FadeID, fade);
        mr.SetPropertyBlock(mpb);
    }

    void DoDamage(float r, float p)
    {
        int c = Physics.OverlapSphereNonAlloc(transform.position, r + 1f, buf, hitMask);
        for (int i = 0; i < c; i++)
        {
            var col = buf[i]; if (hit.Contains(col)) continue;
            Vector3 rel = col.bounds.center - transform.position;
            float ext = col.bounds.extents.magnitude * 0.5f;
            float along = Vector3.Dot(rel, dir);
            if (Mathf.Abs(along) > 0.7f + ext) continue;
            Vector3 perp = rel - dir * along; perp.y = 0f;
            if (perp.magnitude > r + ext) continue;
            hit.Add(col);
            float pw = power * (1f - p * 0.5f);
            // Il danno vero lo fa SoundWaveProjectile (il fronte d'onda che segue gli anelli):
            // qui resta solo la spinta fisica a ciò che l'anello attraversa.
            Vector3 push = (dir + perp.normalized * 0.3f).normalized * knockback * pw;
            if (col.attachedRigidbody) col.attachedRigidbody.AddForce(push, ForceMode.Impulse);
        }
    }
}
