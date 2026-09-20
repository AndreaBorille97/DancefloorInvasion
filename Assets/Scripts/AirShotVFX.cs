using System.Collections.Generic;
using UnityEngine;

namespace AirShot
{
    /// <summary>
    /// Colpo d'aria supersonico: fulmine a zigzag che parte dal player, onda d'urto
    /// concentrica alla partenza e scintille di plasma. Tutto procedurale: nessun
    /// prefab o asset esterno richiesto. Valori di default = setup approvato in preview.
    ///
    /// Uso:  GetComponent&lt;AirShotVFX&gt;().Fire();           // spara verso 'target' o in avanti
    ///       GetComponent&lt;AirShotVFX&gt;().Fire(hitPoint);   // spara verso un punto preciso
    /// </summary>
    [AddComponentMenu("AirShot/Air Shot VFX")]
    public class AirShotVFX : MonoBehaviour
    {
        [Header("Origine / bersaglio")]
        [Tooltip("Punto da cui parte il colpo. Se vuoto viene usato questo transform.")]
        public Transform muzzle;
        [Tooltip("Bersaglio opzionale. Se vuoto il colpo va in avanti per 'range'.")]
        public Transform target;
        [Tooltip("Distanza percorsa quando non c'è un bersaglio (in metri).")]
        public float range = 20.9f;          // web: distance 0.95 * 22 u

        [Header("Colpo")]
        [Range(0.2f, 3f)] public float power = 1.1f;
        [Tooltip("Più alto = più veloce. Durata volo = 0.55 / speed secondi.")]
        [Range(0.5f, 12f)] public float speed = 6f;
        [Range(0f, 1f)] public float trailSparks = 0f;
        [Range(0f, 3f)] public float boltThickness = 1.1f;
        [Range(0f, 2f)] public float zigzag = 0.55f;
        [Tooltip("Frequenza di ridisegno del fulmine (stile cel animation).")]
        [Range(4f, 60f)] public float celFps = 12f;
        [Tooltip("Secondi in cui il fulmine resta a schermo dissolvendosi, DOPO aver percorso " +
                 "la distanza. Il volo dura 0.55/speed, che a speed 6 fa meno di un decimo di " +
                 "secondo: senza questa coda il colpo è un lampo singolo e si fatica a vederlo. " +
                 "Non rallenta il colpo, allunga solo la scia visibile.")]
        [Range(0f, 1.5f)] public float boltHold = 0.28f;

        [Header("Onda d'urto (partenza)")]
        [Range(0, 4)] public int rings = 2;
        [Range(0f, 2f)] public float ringSpeed = 1f;

        [Header("Plasma")]
        [Range(0, 120)] public int density = 54;
        public bool groundSparks = true;

        [Header("Impatto camera")]
        [Range(0f, 1f)] public float cameraShake = 0.5f;
        public bool startFlash = true;
        [Tooltip("Camera da scuotere. Se vuoto usa Camera.main.")]
        public Camera shakeCamera;

        [Header("Colori")]
        public Color hotColor = Color.white;
        public Color glowColor = new Color(0.50f, 0.84f, 1f, 1f);

        // ---- runtime ----
        LineRenderer bolt;
        Material boltMat, glowMat, ringMat;
        LineRenderer boltGlow;
        readonly List<Transform> ringTr = new List<Transform>();
        readonly List<Material> ringMats = new List<Material>();
        ParticleSystem hotPs, glowPs;
        Vector3 from, to;
        float t = -1f, travel, lastCel = -1f;
        // Ultimo sfarfallio estratto da RebuildBolt: la dissolvenza lo riusa ad ogni frame,
        // mentre il ridisegno gira solo a celFps.
        float lastFlick = 1f;
        Vector3 shakeBase;
        bool shakeCached;

        public bool IsPlaying => t >= 0f;

        void Awake()
        {
            Shader add = FindAdditiveShader();
            boltMat = new Material(add) { color = hotColor };
            glowMat = new Material(add) { color = glowColor * 0.45f };
            ringMat = new Material(add) { color = hotColor };

            bolt = CreateLine("AirShot_Bolt", boltMat, 0.06f);
            boltGlow = CreateLine("AirShot_BoltGlow", glowMat, 0.22f);

            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("AirShot_Ring" + i);
                go.transform.SetParent(transform, false);
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = BuildTorus(80, 6, 0.03f);
                var mr = go.AddComponent<MeshRenderer>();
                var m = new Material(ringMat);
                mr.sharedMaterial = m;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                go.SetActive(false);
                ringTr.Add(go.transform);
                ringMats.Add(m);
            }

            hotPs = CreateParticles("AirShot_Hot", hotColor, 0.09f, 0.34f, add);
            glowPs = CreateParticles("AirShot_Glow", glowColor, 0.22f, 0.55f, add);

            SetVisible(false);
        }

        LineRenderer CreateLine(string n, Material m, float width)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.material = m;
            lr.useWorldSpace = true;
            lr.numCapVertices = 2;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;
            lr.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0.55f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.7f));
            lr.widthMultiplier = width;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            return lr;
        }

        ParticleSystem CreateParticles(string n, Color c, float size, float life, Shader add)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.5f, life);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
            main.startSpeed = 0f;               // la velocità viene passata all'emissione
            main.startColor = c;
            main.gravityModifier = 0.12f;
            main.maxParticles = 4000;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;

            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.25f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(1f, 0.15f)));

            var vol = ps.velocityOverLifetime; vol.enabled = true;
            vol.space = ParticleSystemSimulationSpace.World;
            vol.speedModifier = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(1f, 0.15f)));   // drag

            var tr = ps.GetComponent<ParticleSystemRenderer>();
            tr.material = new Material(add) { color = Color.white };
            tr.renderMode = ParticleSystemRenderMode.Billboard;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;
            return ps;
        }

        static Shader FindAdditiveShader()
        {
            Shader s = Shader.Find("AirShot/Additive");
            if (s == null) s = Shader.Find("Legacy Shaders/Particles/Additive");
            if (s == null) s = Shader.Find("Particles/Standard Unlit");
            if (s == null) s = Shader.Find("Sprites/Default");
            return s;
        }

        // ---------------------------------------------------------------- API
        public void Fire()
        {
            Vector3 origin = muzzle ? muzzle.position : transform.position;
            Vector3 dir = muzzle ? muzzle.forward : transform.forward;
            Fire(target ? target.position : origin + dir * range);
        }

        public void Fire(Vector3 hitPoint)
        {
            from = muzzle ? muzzle.position : transform.position;
            to = hitPoint;
            travel = 0.55f / Mathf.Max(0.05f, speed);
            t = 0f;
            lastCel = -1f;
            SetVisible(true);
            EmitShockwaveSparks();
        }

        public void Stop()
        {
            t = -1f;
            SetVisible(false);
            hotPs.Clear(); glowPs.Clear();
        }

        // ------------------------------------------------------------- update
        void Update()
        {
            if (t < 0f) return;
            t += Time.deltaTime;

            Vector3 dir = (to - from);
            float total = dir.magnitude;
            dir = total > 0.001f ? dir / total : Vector3.forward;

            float k = Mathf.Clamp01(t / travel);
            Vector3 head = from + dir * (total * k);

            UpdateRings(dir);
            UpdateShake();

            // 1 durante il volo, poi scende a 0 in boltHold secondi: è la coda che rende il
            // colpo leggibile senza allungarne la durata percepita.
            float fade = t <= travel
                ? 1f
                : (boltHold > 0f ? Mathf.Clamp01(1f - (t - travel) / boltHold) : 0f);

            if (fade > 0f)
            {
                float cel = Mathf.Floor(t * celFps);
                if (!Mathf.Approximately(cel, lastCel))
                {
                    lastCel = cel;
                    RebuildBolt(from + dir * 0.3f, head, dir);
                }

                // Colori applicati qui e non dentro RebuildBolt: quello gira a celFps (12 volte
                // al secondo), quindi la dissolvenza ne uscirebbe a tre o quattro gradini.
                boltMat.color = hotColor * (lastFlick * fade);
                glowMat.color = glowColor * (0.3f * lastFlick * fade);

                bolt.enabled = boltGlow.enabled = true;

                // Solo mentre vola: a volo finito la testa è ferma sul punto d'arrivo e le
                // scintille si ammucchierebbero tutte lì.
                if (t <= travel)
                {
                    EmitTrailSparks(head, dir);
                }
            }
            else
            {
                bolt.enabled = boltGlow.enabled = false;
            }

            if (t > travel + 1.1f) Stop();
        }

        void RebuildBolt(Vector3 a, Vector3 b, Vector3 dir)
        {
            float len = Vector3.Distance(a, b);
            if (len < 0.15f) { bolt.positionCount = boltGlow.positionCount = 0; return; }

            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 0.0001f) side = Vector3.right;
            side.Normalize();
            Vector3 up = Vector3.Cross(side, dir).normalized;

            int segs = Mathf.Clamp(Mathf.RoundToInt(len * 1.6f), 6, 48);
            bolt.positionCount = boltGlow.positionCount = segs + 1;

            for (int i = 0; i <= segs; i++)
            {
                float f = (float)i / segs;
                float taper = Mathf.Sin(Mathf.PI * f) * 0.6f + 0.4f;
                float o1 = (i == 0 || i == segs) ? 0f : (Random.value - 0.5f) * zigzag * 1.5f * taper;
                float o2 = (i == 0 || i == segs) ? 0f : (Random.value - 0.5f) * zigzag * 1.5f * taper;
                Vector3 p = Vector3.Lerp(a, b, f) + side * o1 + up * o2;
                bolt.SetPosition(i, p);
                boltGlow.SetPosition(i, p);
            }
            bolt.widthMultiplier = (0.035f * boltThickness * power + 0.02f) * 2f;
            boltGlow.widthMultiplier = bolt.widthMultiplier * 3.2f;

            // Il colore vero lo applica Update ad ogni frame (vedi lastFlick/fade): qui si
            // estrae solo il nuovo sfarfallio del fotogramma cel.
            lastFlick = 0.75f + 0.25f * Random.value;
        }

        void UpdateRings(Vector3 dir)
        {
            Quaternion rot = Quaternion.LookRotation(dir);
            for (int i = 0; i < ringTr.Count; i++)
            {
                bool on = i < rings;
                float a = t - i * 0.045f;
                float al = on && a > 0f ? Mathf.Clamp01(1f - a / 0.55f) : 0f;
                if (al <= 0f) { if (ringTr[i].gameObject.activeSelf) ringTr[i].gameObject.SetActive(false); continue; }

                float rr = a * 22f * power * ringSpeed * Mathf.Pow(0.62f, i);
                float push = 0.7f + i * (1.1f + a * 3.4f) * power + a * 2.2f;
                var tr = ringTr[i];
                if (!tr.gameObject.activeSelf) tr.gameObject.SetActive(true);
                tr.position = from + dir * push;
                tr.rotation = rot;
                tr.localScale = new Vector3(rr, rr, 1f + 8f * al * power);
                ringMats[i].color = hotColor * (al * 0.8f * (1f - i * 0.12f));
            }
        }

        void UpdateShake()
        {
            Camera cam = shakeCamera ? shakeCamera : Camera.main;
            if (!cam || cameraShake <= 0f) return;
            if (!shakeCached) { shakeBase = cam.transform.localPosition; shakeCached = true; }
            if (t < 0.3f)
            {
                float s = (1f - t / 0.3f) * cameraShake * 0.4f * power;
                cam.transform.localPosition = shakeBase + Random.insideUnitSphere * s;
            }
            else if (shakeCached)
            {
                cam.transform.localPosition = shakeBase;
                shakeCached = false;
            }
        }

        // --------------------------------------------------------- emissione
        void EmitShockwaveSparks()
        {
            int n = Mathf.RoundToInt(60 + density * 5);
            Vector3 dir = (to - from).normalized;
            Quaternion rot = Quaternion.LookRotation(dir);
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                float sp = (7f + Random.value * 13f) * power;
                Vector3 radial = rot * new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                Vector3 vel = radial * sp + dir * (1.5f + Random.value * 6f) * power;
                Vector3 pos = from + dir * 0.3f + radial * 0.35f;
                Emit(hotPs, pos, vel, 0.28f + Random.value * 0.4f, 0.10f + Random.value * 0.16f);
                if (i % 2 == 0) Emit(glowPs, pos, vel * 0.8f, 0.4f + Random.value * 0.5f, 0.12f + Random.value * 0.2f);
            }
        }

        void EmitTrailSparks(Vector3 head, Vector3 dir)
        {
            int count = Mathf.RoundToInt((150f + density * 14f) * power * trailSparks * Time.deltaTime);
            for (int i = 0; i < count; i++)
            {
                Vector3 pos = head - dir * (Random.value * 2.2f * power) + Random.insideUnitSphere * 0.4f * power;
                Vector3 vel = -dir * (2f + Random.value * 10f) * power + Random.insideUnitSphere * zigzag * 6f * power;
                Emit(hotPs, pos, vel, 0.1f + Random.value * 0.22f, 0.05f + Random.value * 0.09f);
                if (i % 4 == 0) Emit(glowPs, pos, vel * 0.7f, 0.2f + Random.value * 0.3f, 0.06f + Random.value * 0.12f);
            }

            if (!groundSparks) return;
            int gc = Mathf.RoundToInt((25f + density * 2.5f) * Time.deltaTime * 30f);
            float groundY = (muzzle ? muzzle.position.y : transform.position.y) - 1.7f;
            Vector3 side = Vector3.Cross(dir, Vector3.up).normalized;
            for (int i = 0; i < gc; i++)
            {
                float s = Random.value < 0.5f ? -1f : 1f;
                Vector3 pos = head - dir * (Random.value * 4f * power);
                pos.y = groundY + 0.08f + Random.value * 0.3f;
                pos += side * s * (0.5f + Random.value * 1.6f);
                Vector3 vel = -dir * (1f + Random.value * 5f) * power
                              + Vector3.up * (1.5f + Random.value * 5f) * power
                              + side * s * (1f + Random.value * 4f) * power;
                Emit(hotPs, pos, vel, 0.3f + Random.value * 0.5f, 0.06f + Random.value * 0.1f);
            }
        }

        void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, float life, float size)
        {
            var ep = new ParticleSystem.EmitParams
            {
                position = pos,
                velocity = vel,
                startLifetime = life,
                startSize = size,
                applyShapeToPosition = false
            };
            ps.Emit(ep, 1);
        }

        void SetVisible(bool v)
        {
            bolt.enabled = boltGlow.enabled = v;
            if (!v) foreach (var r in ringTr) r.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------ utility
        static Mesh BuildTorus(int radial, int tubeSeg, float tubeR)
        {
            var verts = new Vector3[(radial + 1) * (tubeSeg + 1)];
            var uvs = new Vector2[verts.Length];
            var tris = new int[radial * tubeSeg * 6];
            int v = 0;
            for (int i = 0; i <= radial; i++)
            {
                float u = (float)i / radial * Mathf.PI * 2f;
                Vector3 center = new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0f);
                for (int j = 0; j <= tubeSeg; j++)
                {
                    float w = (float)j / tubeSeg * Mathf.PI * 2f;
                    Vector3 n = center * Mathf.Cos(w) + Vector3.forward * Mathf.Sin(w);
                    verts[v] = center + n * tubeR;
                    uvs[v] = new Vector2((float)i / radial, (float)j / tubeSeg);
                    v++;
                }
            }
            int ti = 0;
            for (int i = 0; i < radial; i++)
                for (int j = 0; j < tubeSeg; j++)
                {
                    int a = i * (tubeSeg + 1) + j;
                    int b = a + tubeSeg + 1;
                    tris[ti++] = a; tris[ti++] = b; tris[ti++] = a + 1;
                    tris[ti++] = a + 1; tris[ti++] = b; tris[ti++] = b + 1;
                }
            var m = new Mesh { name = "AirShotRing" };
            m.vertices = verts; m.uv = uvs; m.triangles = tris;
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
