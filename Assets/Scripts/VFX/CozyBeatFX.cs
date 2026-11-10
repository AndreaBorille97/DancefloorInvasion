using System.Collections.Generic;
using UnityEngine;

/// A ogni battuta: cuoricini che escono dalle casse insieme alle onde
/// e, ogni N battute, un piccolo fuoco d'artificio lanciato da dietro il player.
/// Le battute le dà PlayerSerotonin chiamando Beat ad ogni onda serotonina.
public class CozyBeatFX : MonoBehaviour
{
    public Material particleMaterial;   // M_CozyParticle (shader VFX/CozyParticle)

    [Header("Cuoricini")]
    public bool hearts = true;
    [Range(0f, 1f)] public float heartChancePerBeat = 0.35f;   // probabilità di un cuore a battuta (da una cassa a caso)
    public Vector2 heartSpeed = new Vector2(4f, 9f);
    public Vector2 heartSize = new Vector2(0.45f, 0.75f);
    public Vector2 heartLife = new Vector2(1f, 1.5f);
    public Color[] heartColors = { new Color(1f, .56f, .66f), new Color(1f, .7f, .76f), new Color(1f, .44f, .57f), new Color(1f, .77f, .66f) };

    [Header("Fuochi d'artificio")]
    public bool fireworks = true;
    [Min(1)] public int everyNBeats = 2;
    public float launchBehind = 1.3f;
    public float launchSpread = 1.2f;
    public Vector2 launchSpeed = new Vector2(12f, 15f);
    public Vector2 fuse = new Vector2(0.55f, 0.75f);
    public int sparksPerBurst = 60;
    public int innerSparks = 30;            // secondo anello, colore diverso
    public float burstSpeed = 7.5f;
    public float sparkSize = 0.35f;
    public float flashSize = 9f;
    public Color[] fireworkColors = { new Color(1f, .82f, .48f), new Color(1f, .56f, .66f), new Color(1f, .69f, .4f), new Color(1f, .95f, .81f), new Color(1f, .64f, .77f) };
    [Range(0f, 1f)] public float heartInBurstChance = 0.2f;

    ParticleSystem heartPS, sparkPS, flashPS;
    int beat;

    struct Rocket { public Vector3 pos, vel; public float t, fuse; public Color col, col2; }
    readonly List<Rocket> rockets = new List<Rocket>();

    void Awake()
    {
        var baseMat = particleMaterial ? particleMaterial : new Material(Shader.Find("VFX/CozyParticle"));
        var heartMat = new Material(baseMat) { mainTexture = MakeHeartTexture() };
        // 1.4 volte la luminosità del materiale (prima 1.4 fisso): così alzando _Intensity del materiale si accendono anche le scintille.
        var sparkMat = new Material(baseMat) { mainTexture = MakeDotTexture() }; sparkMat.SetFloat("_Intensity", baseMat.GetFloat("_Intensity") * 1.4f);

        heartPS = MakePS("CozyHearts", heartMat, 0f);
        var sz = heartPS.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0), new Keyframe(.12f, 1.25f), new Keyframe(.22f, 1f), new Keyframe(1, .9f)));
        var noise = heartPS.noise; noise.enabled = true; noise.strength = .5f; noise.frequency = .6f; noise.scrollSpeed = .4f;
        var lim = heartPS.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 1.2f; lim.dampen = .05f;
        var rot = heartPS.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-.6f, .6f);
        var force = heartPS.forceOverLifetime; force.enabled = true; force.y = .6f;

        sparkPS = MakePS("CozySparks", sparkMat, .3f);
        var sl = sparkPS.limitVelocityOverLifetime; sl.enabled = true; sl.limit = 0.5f; sl.dampen = 0.08f;
        flashPS = MakePS("CozyFlash", sparkMat, 0f);
        var fsz = flashPS.sizeOverLifetime; fsz.enabled = true; fsz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, .2f, 1, 1));
        var ssz = sparkPS.sizeOverLifetime; ssz.enabled = true; ssz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0));
    }

    ParticleSystem MakePS(string name, Material mat, float gravity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.playOnAwake = false; main.loop = true; main.startSpeed = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = gravity; main.maxParticles = 3000;
        var emission = ps.emission; emission.enabled = false;
        var shape = ps.shape; shape.enabled = false;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, .5f), new GradientAlphaKey(0, 1) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.Billboard;
        ps.Play();
        return ps;
    }

    void LateUpdate()
    {
        UpdateRockets(Time.deltaTime);
    }

    /// Una battuta data da fuori (PlayerSerotonin): i cuoricini partono da heartSource, lungo il suo asse Z.
    public void Beat(Transform heartSource) => OnBeat(heartSource);

    void OnBeat(Transform heartSource)
    {
        if (hearts && Random.value < heartChancePerBeat) EmitHearts(heartSource);
        beat++;
        if (fireworks && beat % everyNBeats == 0)
        {
            Vector3 p = transform.position - transform.forward * launchBehind + transform.right * Random.Range(-launchSpread, launchSpread) + Vector3.up * 1.2f;
            Vector3 v = Vector3.up * Random.Range(launchSpeed.x, launchSpeed.y) - transform.forward * 1.4f + Random.insideUnitSphere * .5f;
            rockets.Add(new Rocket { pos = p, vel = v, fuse = Random.Range(fuse.x, fuse.y), col = fireworkColors[Random.Range(0, fireworkColors.Length)], col2 = fireworkColors[Random.Range(0, fireworkColors.Length)] });
        }
    }

    void EmitHearts(Transform sp)
    {
        if (!sp) return;
        for (int i = 0; i < 1; i++)
        {
            Vector3 v = sp.forward * Random.Range(heartSpeed.x, heartSpeed.y) + sp.right * Random.Range(-1.5f, 1.5f) + Vector3.up * Random.Range(.8f, 2.4f);
            heartPS.Emit(new ParticleSystem.EmitParams
            {
                position = sp.position, velocity = v, startSize = Random.Range(heartSize.x, heartSize.y),
                startLifetime = Random.Range(heartLife.x, heartLife.y), startColor = heartColors[Random.Range(0, heartColors.Length)] * .85f,
                rotation = Random.Range(-15f, 15f)
            }, 1);
        }
    }

    void UpdateRockets(float dt)
    {
        for (int i = rockets.Count - 1; i >= 0; i--)
        {
            var r = rockets[i];
            r.t += dt; r.vel += Vector3.down * 9f * dt; r.pos += r.vel * dt;
            sparkPS.Emit(new ParticleSystem.EmitParams { position = r.pos, velocity = Random.insideUnitSphere * .3f + Vector3.down * .5f, startSize = sparkSize * .8f, startLifetime = .4f, startColor = r.col }, 2);
            if (r.t >= r.fuse)
            {
                for (int k = 0; k < sparksPerBurst + innerSparks; k++)
                {
                    bool inner = k >= sparksPerBurst;
                    sparkPS.Emit(new ParticleSystem.EmitParams
                    {
                        position = r.pos, velocity = Random.onUnitSphere * (inner ? burstSpeed * .55f : burstSpeed) * Random.Range(1f, 1.2f), startSize = sparkSize * Random.Range(.8f, 1.2f),
                        startLifetime = Random.Range(1.2f, 1.9f), startColor = inner ? r.col2 : (Random.value < .2f ? Color.white : r.col)
                    }, 1);
                }
                flashPS.Emit(new ParticleSystem.EmitParams { position = r.pos, startSize = flashSize, startLifetime = .3f, startColor = r.col * 1.4f }, 1);
                if (hearts && Random.value < heartInBurstChance)
                    heartPS.Emit(new ParticleSystem.EmitParams { position = r.pos, velocity = Vector3.up * .6f, startSize = .6f, startLifetime = .9f, startColor = heartColors[0] * .85f }, 1);
                rockets.RemoveAt(i);
            }
            else rockets[i] = r;
        }
    }

    static Texture2D MakeHeartTexture()
    {
        const int N = 64; var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            float cov = 0f;
            for (int s = 0; s < 4; s++)
            {
                float u = ((x + (s & 1) * .5f + .25f) / N - .5f) * 2.6f, v = ((y + (s >> 1) * .5f + .25f) / N - .45f) * 2.6f;
                float a = u * u + v * v - 1f;
                if (a * a * a - u * u * v * v * v <= 0f) cov += .25f;
            }
            px[y * N + x] = new Color32(255, 255, 255, (byte)(cov * 255));
        }
        tex.SetPixels32(px); tex.Apply(); return tex;
    }

    static Texture2D MakeDotTexture()
    {
        const int N = 32; var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(N / 2f, N / 2f)) / (N / 2f);
            px[y * N + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d) * 255));
        }
        tex.SetPixels32(px); tex.Apply(); return tex;
    }
}
