using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Esplosione "classica" dell'IdroSbirro che salta in aria: una palla di fuoco che si allarga
// e si raffredda in fumo nero che sale, schegge incandescenti che ricadono, onda d'urto e
// bruciatura a terra, lampo di luce. Disegnata con lo stesso shader del fungo atomico
// (MiniNuke/Puff, vedi MiniNuke3D), quindi stessi puff ombreggiati: qui cambia solo la
// simulazione. Tutto procedurale, si autodistrugge a fine effetto. Gira a tempo reale perché
// alla vittoria (WinUI) il gioco va in pausa e l'ultimo camion deve finire di esplodere.
public class TruckExplosion : MonoBehaviour
{
    public float scale = 1f;   // a 1 la palla di fuoco è larga circa 5 m
    public Shader shader;      // MiniNukePuff.shader: va passato per le build (Shader.Find non lo troverebbe)

    private const float Duration = 3.2f;

    private struct Fireball { public Vector3 dir; public float speed, r, hv, seed; }
    private struct Smoke { public Vector3 offset; public float rise, r, delay, seed; }
    private struct Spark { public Vector3 velocity; public float life, width; }
    private struct Quad { public Vector3 p, p1; public float s, a, mode, seed, dist; public Color c; public bool flat, streak; }

    private Fireball[] fireballs;
    private Smoke[] smokes;
    private Spark[] sparks;
    private readonly List<Quad> blended = new List<Quad>(256), additive = new List<Quad>(256);
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Color> colors = new List<Color>();
    private readonly List<Vector2> uvs = new List<Vector2>(), data = new List<Vector2>();
    private readonly List<int> indices = new List<int>();
    private Mesh blendedMesh, additiveMesh;
    private Material blendedMaterial, additiveMaterial;
    private Light flash;
    private float startTime;

    public static TruckExplosion Spawn(Vector3 groundPosition, float scale, Shader shader)
    {
        GameObject go = new GameObject("TruckExplosion");
        go.transform.position = groundPosition;
        TruckExplosion explosion = go.AddComponent<TruckExplosion>();
        explosion.scale = scale;
        explosion.shader = shader;
        return explosion;
    }

    void Start()
    {
        if (shader == null)
        {
            shader = Shader.Find("MiniNuke/Puff");
        }
        blendedMaterial = new Material(shader) { renderQueue = 3000 };
        blendedMaterial.SetFloat("_Src", (float)BlendMode.SrcAlpha);
        blendedMaterial.SetFloat("_Dst", (float)BlendMode.OneMinusSrcAlpha);
        blendedMaterial.SetFloat("_Add", 0f);
        additiveMaterial = new Material(shader) { renderQueue = 3001 };
        additiveMaterial.SetFloat("_Src", (float)BlendMode.One);
        additiveMaterial.SetFloat("_Dst", (float)BlendMode.One);
        additiveMaterial.SetFloat("_Add", 1f);
        blendedMesh = new Mesh();
        blendedMesh.MarkDynamic();
        additiveMesh = new Mesh();
        additiveMesh.MarkDynamic();

        GameObject light = new GameObject("Flash");
        light.transform.SetParent(transform, false);
        light.transform.localPosition = Vector3.up * 1.5f * scale;
        flash = light.AddComponent<Light>();
        flash.type = LightType.Point;
        flash.color = new Color(1f, 0.75f, 0.45f);
        flash.range = 16f * scale;

        // Palla di fuoco: puff sparati in tutte le direzioni della metà alta, frenati dall'aria.
        fireballs = new Fireball[46];
        for (int i = 0; i < fireballs.Length; i++)
        {
            Vector3 dir = Random.onUnitSphere;
            dir.y = Mathf.Abs(dir.y) * 0.8f + 0.15f;
            fireballs[i] = new Fireball
            {
                dir = dir.normalized,
                speed = 3f + Random.value * 4f,
                r = 0.45f + Random.value * 0.4f,
                hv = (Random.value - 0.5f) * 0.2f,
                seed = i * 0.173f
            };
        }

        // Fumo nero che sale dal rottame mentre il fuoco si spegne.
        smokes = new Smoke[26];
        for (int i = 0; i < smokes.Length; i++)
        {
            Vector2 offset = Random.insideUnitCircle * 1.3f;
            smokes[i] = new Smoke
            {
                offset = new Vector3(offset.x, 0.4f + Random.value * 0.8f, offset.y),
                rise = 1.2f + Random.value * 1.2f,
                r = 0.6f + Random.value * 0.5f,
                delay = 0.25f + Random.value * 0.6f,
                seed = 5f + i * 0.211f
            };
        }

        // Schegge incandescenti: parabole con la gravità.
        sparks = new Spark[50];
        for (int i = 0; i < sparks.Length; i++)
        {
            Vector3 dir = Random.onUnitSphere;
            dir.y = Mathf.Abs(dir.y) + 0.3f;
            sparks[i] = new Spark
            {
                velocity = dir.normalized * (6f + Random.value * 8f),
                life = 0.5f + Random.value * 0.8f,
                width = 0.03f + Random.value * 0.04f
            };
        }

        startTime = Time.unscaledTime;
    }

    private static float EaseOut(float k)
    {
        k = Mathf.Clamp01(k);
        return 1f - Mathf.Pow(1f - k, 3f);
    }

    // Stessa scala di colori del fungo (MiniNuke3D): da bianco caldo ad arancio a fumo scuro.
    private static readonly float[] HeatKeys = { 1f, 0.8f, 0.62f, 0.45f, 0.28f, 0.12f, 0f };
    private static readonly Color[] HeatColors =
    {
        new Color32(255, 248, 222, 255), new Color32(255, 202, 112, 255), new Color32(242, 128, 44, 255),
        new Color32(168, 68, 32, 255), new Color32(92, 60, 46, 255), new Color32(50, 45, 43, 255), new Color32(30, 28, 28, 255)
    };

    private static Color Heat(float h)
    {
        h = Mathf.Clamp01(h);
        for (int i = 0; i < HeatKeys.Length - 1; i++)
        {
            if (h <= HeatKeys[i] && h >= HeatKeys[i + 1])
            {
                return Color.Lerp(HeatColors[i + 1], HeatColors[i], (h - HeatKeys[i + 1]) / (HeatKeys[i] - HeatKeys[i + 1]));
            }
        }
        return HeatColors[HeatColors.Length - 1];
    }

    private void Add(bool isAdditive, Quad q)
    {
        if (q.a > 0.002f && q.s > 0.001f)
        {
            (isAdditive ? additive : blended).Add(q);
        }
    }

    private void Fire(Vector3 p, float r, float h, float a, float seed)
    {
        Add(false, new Quad { p = p, s = r * 1.35f, c = Heat(h), a = a, mode = 0, seed = seed });
        if (h > 0.55f)
        {
            Add(true, new Quad { p = p, s = r * 1.0f, c = Heat(Mathf.Min(1f, h + 0.1f)), a = a * Mathf.Min(1f, (h - 0.55f) * 1.6f), mode = 0, seed = seed + 0.5f });
        }
    }

    private void Simulate(float t)
    {
        float fade = 1f - Mathf.Clamp01((t - 2.2f) / 1f);

        // Bruciatura a terra e onda d'urto.
        Add(false, new Quad { p = new Vector3(0f, 0.03f, 0f), s = 2.6f, c = new Color(0.12f, 0.09f, 0.07f), a = 0.65f * Mathf.Min(1f, t / 0.15f) * fade, mode = 3, flat = true });
        if (t < 0.45f)
        {
            float k = t / 0.45f;
            Add(true, new Quad { p = new Vector3(0f, 0.08f, 0f), s = 1f + 6f * EaseOut(k), c = new Color(1f, 0.95f, 0.85f), a = 0.8f * (1f - k), mode = 2, flat = true });
        }

        // Palla di fuoco: esce di colpo, si allarga e sale, poi diventa fumo.
        const float drag = 3.5f;
        float travel = (1f - Mathf.Exp(-drag * t)) / drag;
        for (int i = 0; i < fireballs.Length; i++)
        {
            Fireball f = fireballs[i];
            Vector3 p = f.dir * f.speed * travel + Vector3.up * (0.8f + 0.9f * t);
            float r = f.r * (0.6f + 1.6f * EaseOut(t / 0.7f));
            float h = 1.05f - t / 0.75f + f.hv;
            Fire(p, r, h, 0.9f * fade, f.seed);
        }

        // Fumo nero dal rottame.
        for (int i = 0; i < smokes.Length; i++)
        {
            Smoke s = smokes[i];
            float st = t - s.delay;
            if (st <= 0f)
            {
                continue;
            }
            Vector3 p = s.offset + Vector3.up * s.rise * st;
            float r = s.r * (0.7f + 0.6f * st);
            float a = 0.75f * Mathf.Min(1f, st / 0.3f) * fade;
            Add(false, new Quad { p = p, s = r * 1.35f, c = Heat(0.08f), a = a, mode = 0, seed = s.seed });
        }

        // Bagliore interno della palla di fuoco.
        if (t < 1.2f)
        {
            Add(true, new Quad { p = new Vector3(0f, 1.2f, 0f), s = 3.5f, c = new Color(1f, 0.7f, 0.3f), a = 0.6f * (1f - t / 1.2f), mode = 1 });
        }

        // Schegge.
        foreach (Spark k in sparks)
        {
            if (t > k.life)
            {
                continue;
            }
            float a = 1f - t / k.life;
            float t0 = Mathf.Max(0f, t - 0.05f);
            Add(true, new Quad { p = SparkPosition(k, t0), p1 = SparkPosition(k, t), streak = true, s = k.width, c = new Color(1f, (140f + 110f * a) / 255f, (60f + 120f * a) / 255f), a = a, mode = 4 });
        }

        // Lampo.
        if (t < 0.15f)
        {
            Add(true, new Quad { p = new Vector3(0f, 1f, 0f), s = 5f, c = new Color(1f, 0.97f, 0.85f), a = 1f - t / 0.15f, mode = 1 });
        }
    }

    private static Vector3 SparkPosition(Spark k, float t)
    {
        return new Vector3(0f, 0.8f, 0f) + k.velocity * t + 0.5f * Physics.gravity * t * t;
    }

    // Quad rivolti alla camera costruiti su CPU, come in MiniNuke3D.
    private void Build(Mesh mesh, List<Quad> list, bool sort, Vector3 camPos, Vector3 right, Vector3 up, Vector3 origin)
    {
        if (sort)
        {
            for (int i = 0; i < list.Count; i++)
            {
                Quad q = list[i];
                q.dist = ((origin + q.p * scale) - camPos).sqrMagnitude;
                list[i] = q;
            }
            list.Sort((a, b) => b.dist.CompareTo(a.dist));
        }

        vertices.Clear(); colors.Clear(); uvs.Clear(); data.Clear(); indices.Clear();
        for (int i = 0; i < list.Count; i++)
        {
            Quad q = list[i];
            float s = q.s * scale;
            Vector3 c = origin + q.p * scale;
            if (q.flat)
            {
                Vector3 ax = Vector3.right * s, az = Vector3.forward * s;
                vertices.Add(c - ax - az); vertices.Add(c + ax - az); vertices.Add(c - ax + az); vertices.Add(c + ax + az);
            }
            else if (q.streak)
            {
                Vector3 e = origin + q.p1 * scale;
                Vector3 side = Vector3.Cross(e - c, camPos - c).normalized * s;
                vertices.Add(c - side); vertices.Add(c + side); vertices.Add(e - side); vertices.Add(e + side);
            }
            else
            {
                Vector3 rx = right * s, uy = up * s;
                vertices.Add(c - rx - uy); vertices.Add(c + rx - uy); vertices.Add(c - rx + uy); vertices.Add(c + rx + uy);
            }

            Color col = q.c;
            col.a = Mathf.Min(1f, q.a);
            for (int v = 0; v < 4; v++)
            {
                colors.Add(col);
                data.Add(new Vector2(q.mode, q.seed));
            }
            uvs.Add(new Vector2(-1f, -1f)); uvs.Add(new Vector2(1f, -1f)); uvs.Add(new Vector2(-1f, 1f)); uvs.Add(new Vector2(1f, 1f));
            int b = i * 4;
            indices.Add(b); indices.Add(b + 2); indices.Add(b + 1); indices.Add(b + 2); indices.Add(b + 3); indices.Add(b + 1);
        }

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, data);
        mesh.SetTriangles(indices, 0);
        mesh.bounds = new Bounds(origin, Vector3.one * 100f);
        list.Clear();
    }

    void LateUpdate()
    {
        float t = Time.unscaledTime - startTime;
        flash.intensity = (t < 0.15f ? (1f - t / 0.15f) * 8f : 0f) + (t < 1.5f ? (1f - t / 1.5f) * 2.5f : 0f);

        if (t >= Duration)
        {
            Destroy(gameObject);
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        Simulate(t);
        Transform ct = cam.transform;
        Vector3 origin = transform.position;
        Build(blendedMesh, blended, true, ct.position, ct.right, ct.up, origin);
        Build(additiveMesh, additive, false, ct.position, ct.right, ct.up, origin);
        Graphics.DrawMesh(blendedMesh, Matrix4x4.identity, blendedMaterial, gameObject.layer);
        Graphics.DrawMesh(additiveMesh, Matrix4x4.identity, additiveMaterial, gameObject.layer);
    }

    void OnDestroy()
    {
        if (blendedMesh != null) Destroy(blendedMesh);
        if (additiveMesh != null) Destroy(additiveMesh);
        if (blendedMaterial != null) Destroy(blendedMaterial);
        if (additiveMaterial != null) Destroy(additiveMaterial);
    }
}
