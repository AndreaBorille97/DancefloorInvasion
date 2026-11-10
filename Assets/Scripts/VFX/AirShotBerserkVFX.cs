using System.Collections.Generic;
using UnityEngine;

namespace AirShotBerserk
{
    /// <summary>
    /// Colpo d'aria BERSERK: linea di plasma bianco/rossa, scariche elettriche rosse e bianche,
    /// onde d'urto rosse ondulate e tremolanti, scintille rosse e detriti bianchi.
    /// Totalmente procedurale. Valori di default = setup approvato in preview.
    ///
    ///   GetComponent&lt;AirShotBerserkVFX&gt;().Fire();
    ///   GetComponent&lt;AirShotBerserkVFX&gt;().Fire(hitPoint);
    /// </summary>
    [AddComponentMenu("AirShot/Air Shot BERSERK VFX")]
    public class AirShotBerserkVFX : MonoBehaviour
    {
        [Header("Origine / bersaglio")]
        public Transform muzzle;
        public Transform target;
        public float range = 15.4f;

        [Header("Colpo")]
        [Range(0.2f, 3f)] public float power = 1.1f;
        [Range(0.5f, 12f)] public float speed = 2.2f;
        [Range(0f, 1f)] public float trailSparks = 1f;
        [Tooltip("Secondi in cui la linea resta a schermo sfumando DOPO l'arrivo: è la scia lasciata " +
                 "dallo scatto. 0 = sparisce appena arriva, come nella preview.")]
        [Range(0f, 1.5f)] public float lineHold = 0.4f;
        [Tooltip("Larghezza dell'alone rosso attorno alla linea, rispetto alla linea (2.6 nella preview).")]
        [Range(1f, 6f)] public float lineGlowWidth = 3.6f;
        [Tooltip("Spessore della linea. 1.3 = un po' più grossa di quella dello scatto normale (1.6 nella preview).")]
        [Range(0f, 3f)] public float lineThickness = 1.3f;
        [Range(0f, 2f)] public float sparkSpread = 0.55f;
        [Range(4f, 60f)] public float celFps = 12f;

        [Header("Scariche")]
        [Range(0, 24)] public int discharges = 12;
        [Range(0f, 2f)] public float dischargeChaos = 1f;
        [Tooltip("Persistenza delle scariche dopo l'arrivo (s).")]
        public float dischargeLinger = 0.35f;

        [Header("Onde d'urto")]
        [Range(0, 4)] public int rings = 3;
        [Range(0f, 0.2f)] public float ringWaviness = 0.07f;
        [Range(0f, 1f)] public float ringFlicker = 0.55f;
        [Tooltip("Quanto si allargano le onde: moltiplica il raggio massimo (1 = come nella preview). " +
                 "0.45 le fa un po' più ampie di quelle dello scatto normale (AirShotVFX.ringSize 0.5).")]
        [Range(0.1f, 2f)] public float ringSize = 0.45f;

        [Header("Plasma")]
        [Range(0, 120)] public int density = 26;
        [Tooltip("Dimensione delle particelle rilasciate: 1 = come nella preview. 0.6 = un po' più " +
                 "grosse di quelle dello scatto normale (AirShotVFX.particleSize 0.5).")]
        [Range(0.1f, 2f)] public float particleSize = 0.6f;
        public bool groundSparks = true;

        [Header("Camera")]
        [Tooltip("Tremolio fatto dall'effetto stesso, spostando la camera. Nel gioco è a 0: CameraFollow " +
                 "riposiziona la camera ogni frame e lo cancellerebbe. Lo scatto berserk fa tremare lo " +
                 "schermo con CameraShake (vedi PlayerMovement.berserkDashShake).")]
        [Range(0f, 1f)] public float cameraShake = 0f;
        public bool startFlash = true;
        public Camera shakeCamera;

        [Header("Colori")]
        public Color hotColor = Color.white;
        public Color redColor = new Color(1f, 0.08f, 0.08f, 1f);

        // runtime
        Shader addShader;
        LineRenderer line, lineGlow;
        Material lineMat, lineGlowMat;
        readonly List<LineRenderer> arcs = new List<LineRenderer>();
        readonly List<bool> arcOn = new List<bool>();
        readonly List<Transform> ringTr = new List<Transform>();
        readonly List<Mesh> ringMesh = new List<Mesh>();
        readonly List<Material> ringMat = new List<Material>();
        Vector3[] ringBase; Vector3[] ringWork;
        ParticleSystem hotPs, redPs, glowPs, whitePs;
        Vector3 from, to;
        float t = -1f, travel, lastCel = -1f, lastArcCel = -1f, flashA;
        Vector3 shakeBase; bool shakeCached;
        Texture2D flashTex;
        // Ultimo sfarfallio estratto da UpdateLine (che gira a celFps): la dissolvenza della scia
        // lo riusa ad ogni frame, così sfuma liscia invece che a gradini.
        float lastFlick = 1f;

        public bool IsPlaying => t >= 0f;

        void Awake()
        {
            addShader = Shader.Find("AirShotBerserk/Additive");
            if (addShader == null) addShader = Shader.Find("Legacy Shaders/Particles/Additive");
            if (addShader == null) addShader = MaterialLibrary.SpriteDefault;

            lineMat = new Material(addShader) { color = hotColor };
            lineGlowMat = new Material(addShader) { color = redColor * 0.4f };
            line = MakeLine("Berserk_Line", lineMat);
            lineGlow = MakeLine("Berserk_LineGlow", lineGlowMat);

            for (int i = 0; i < 24; i++)
            {
                var m = new Material(addShader) { color = i % 3 == 0 ? hotColor : redColor };
                var lr = MakeLine("Berserk_Arc" + i, m);
                lr.widthCurve = AnimationCurve.Linear(0, 1, 1, 0.3f);
                arcs.Add(lr); arcOn.Add(false);
            }

            Mesh baseTorus = BuildTorus(96, 6, 0.025f);
            ringBase = baseTorus.vertices;
            ringWork = new Vector3[ringBase.Length];
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("Berserk_Ring" + i);
                go.transform.SetParent(transform, false);
                var mesh = Instantiate(baseTorus); mesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                var mat = new Material(addShader) { color = redColor };
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                go.SetActive(false);
                ringTr.Add(go.transform); ringMesh.Add(mesh); ringMat.Add(mat);
            }

            Texture2D soft = MakeDot(false), hard = MakeDot(true);
            hotPs = MakeParticles("Berserk_Hot", hotColor, addShader, soft, 0.12f);
            glowPs = MakeParticles("Berserk_Glow", redColor, addShader, soft, 0.12f);
            redPs = MakeParticles("Berserk_Red", redColor, addShader, soft, 0.25f);
            Shader alpha = MaterialLibrary.SpriteDefault;
            whitePs = MakeParticles("Berserk_WhiteDebris", Color.white, alpha != null ? alpha : addShader, hard, 0.6f);

            flashTex = new Texture2D(1, 1); flashTex.SetPixel(0, 0, Color.white); flashTex.Apply();
            HideAll();
        }

        LineRenderer MakeLine(string n, Material m)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.material = m; lr.useWorldSpace = true; lr.numCapVertices = 2;
            lr.alignment = LineAlignment.View;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false; lr.enabled = false;
            return lr;
        }

        ParticleSystem MakeParticles(string n, Color c, Shader sh, Texture2D tex, float gravity)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f; main.startColor = c;
            main.gravityModifier = gravity; main.maxParticles = 5000;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.enabled = false;

            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(c, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.25f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0.2f)));
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.drag = 3f; lim.multiplyDragByParticleSize = false; lim.multiplyDragByParticleVelocity = true;

            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.material = new Material(sh) { color = Color.white, mainTexture = tex };
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------- API
        public void Fire()
        {
            Vector3 o = muzzle ? muzzle.position : transform.position;
            Vector3 d = muzzle ? muzzle.forward : transform.forward;
            Fire(target ? target.position : o + d * range);
        }

        public void Fire(Vector3 hitPoint)
        {
            from = muzzle ? muzzle.position : transform.position;
            to = hitPoint;
            travel = 0.55f / Mathf.Max(0.05f, speed);
            t = 0f; lastCel = lastArcCel = -1f;
            if (startFlash) flashA = 0.5f;
            EmitLaunchBurst();
        }

        public void Stop() { t = -1f; HideAll(); RestoreShake(); }

        // ---------------------------------------------------------- update
        void Update()
        {
            if (flashA > 0f) flashA = Mathf.Max(0f, flashA - Time.deltaTime * 5f);
            if (t < 0f) return;
            t += Time.deltaTime;

            Vector3 dir = to - from; float total = dir.magnitude;
            dir = total > 0.001f ? dir / total : Vector3.forward;
            Vector3 head = from + dir * (total * Mathf.Clamp01(t / travel));
            Vector3 side = Vector3.Cross(dir, Vector3.up); if (side.sqrMagnitude < 1e-4f) side = Vector3.right; side.Normalize();
            Vector3 up = Vector3.Cross(side, dir).normalized;

            UpdateRings(dir, side, up);
            UpdateShake();

            // 1 durante il volo, poi scende a 0 in lineHold secondi: la scia resta dietro al Player.
            float fade = t <= travel ? 1f : (lineHold > 0f ? Mathf.Clamp01(1f - (t - travel) / lineHold) : 0f);
            if (fade > 0f)
            {
                float cel = Mathf.Floor(t * celFps);
                if (cel != lastCel) { lastCel = cel; UpdateLine(from + dir * 0.3f, head); }
                line.enabled = lineGlow.enabled = true;
                lineMat.color = hotColor * (lastFlick * fade);
                lineGlowMat.color = redColor * (0.55f * lastFlick * fade);
                // Solo in volo: a volo finito la testa è ferma all'arrivo e le scintille ci si ammucchierebbero.
                if (t <= travel) EmitTrail(head, dir, side);
            }
            else line.enabled = lineGlow.enabled = false;

            UpdateDischarges(head, dir, side, up);

            if (t > travel + Mathf.Max(1.1f, lineHold + 0.1f)) Stop();
        }

        void UpdateLine(Vector3 a, Vector3 b)
        {
            line.positionCount = lineGlow.positionCount = 2;
            line.SetPosition(0, a); line.SetPosition(1, b);
            lineGlow.SetPosition(0, a); lineGlow.SetPosition(1, b);
            float w = (0.11f * lineThickness * power + 0.05f) * 2f;
            line.widthMultiplier = w; lineGlow.widthMultiplier = w * lineGlowWidth;
            // Il colore lo applica Update ad ogni frame (vedi lastFlick e la dissolvenza della scia).
            lastFlick = 0.75f + 0.25f * Random.value;
        }

        void UpdateDischarges(Vector3 head, Vector3 dir, Vector3 side, Vector3 up)
        {
            if (t > travel + dischargeLinger) { foreach (var a in arcs) a.enabled = false; return; }
            float reach = Mathf.Max(0.6f, Vector3.Dot(head - from, dir));
            float cel = Mathf.Floor(t * celFps * 1.6f);
            if (cel != lastArcCel)
            {
                lastArcCel = cel;
                for (int i = 0; i < arcs.Count; i++)
                {
                    arcOn[i] = i < discharges && Random.value < 0.8f;
                    if (!arcOn[i]) continue;
                    float along = 0.3f + Random.value * 0.92f * reach;
                    float L = (1.2f + Random.value * 3.8f) * power;
                    Vector3 a = from + dir * along + side * (Random.value - 0.5f) * 0.3f + up * (Random.value - 0.5f) * 0.3f;
                    float endAlong = Mathf.Min(along + L, reach + 1.6f * power);
                    Vector3 b = from + dir * endAlong + side * (Random.value - 0.5f) * 2.2f * power * dischargeChaos + up * (Random.value - 0.5f) * 2.2f * power * dischargeChaos;
                    float amp = (0.5f + Random.value * 1.2f) * power * dischargeChaos;
                    const int n = 9;
                    var lr = arcs[i];
                    lr.positionCount = n + 1;
                    lr.SetPosition(0, a);
                    for (int k = 1; k <= n; k++)
                    {
                        float f = (float)k / n, e = k == n ? 0.2f : 1f;
                        Vector3 p = Vector3.Lerp(a, b, f) + dir * (Random.value - 0.5f) * amp * 0.4f
                                    + side * (Random.value - 0.5f) * amp * e + up * (Random.value - 0.5f) * amp * e;
                        lr.SetPosition(k, p);
                    }
                    lr.widthMultiplier = (i % 3 == 0 ? 0.06f : 0.09f) * power;
                }
            }
            float fl = t > travel ? Mathf.Clamp01(1f - (t - travel) / dischargeLinger) : 1f;
            for (int i = 0; i < arcs.Count; i++)
            {
                arcs[i].enabled = arcOn[i];
                if (!arcOn[i]) continue;
                Color c = i % 3 == 0 ? hotColor : redColor;
                arcs[i].sharedMaterial.color = c * (fl * (0.65f + 0.35f * Random.value));
            }

            int rc = Mathf.RoundToInt((520f + density * 20f) * power * Time.deltaTime * fl);
            for (int i = 0; i < rc; i++)
            {
                Vector3 pos = from + dir * (0.3f + Random.value * reach) + Random.insideUnitSphere * 0.15f;
                float a = Random.value * Mathf.PI * 2f, sp = (3f + Random.value * 13f) * power;
                Vector3 vel = dir * (Random.value - 0.3f) * 7f * power + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * sp;
                Emit(redPs, pos, vel, 0.15f + Random.value * 0.35f, (0.06f + Random.value * 0.11f) * 1.5f);
            }
        }

        void UpdateRings(Vector3 dir, Vector3 side, Vector3 up)
        {
            Quaternion baseRot = Quaternion.LookRotation(dir, up);
            for (int i = 0; i < ringTr.Count; i++)
            {
                float a = t - i * 0.045f;
                float al = i < rings && a > 0f ? Mathf.Clamp01(1f - a / 0.42f) : 0f;
                var tr = ringTr[i];
                if (al <= 0f) { if (tr.gameObject.activeSelf) tr.gameObject.SetActive(false); continue; }
                if (!tr.gameObject.activeSelf) tr.gameObject.SetActive(true);

                float pop = 1f - Mathf.Pow(1f - Mathf.Clamp01(a / 0.42f), 4f);
                float rr = (0.4f + pop * 9.5f) * power * 1.35f * ringSize * Mathf.Pow(0.66f, i);
                float push = 0.7f + i * (1.1f + a * 4.2f) * power + pop * 2.6f;
                float j = 0.18f * power;

                tr.position = from + dir * (push + (Random.value - 0.5f) * j)
                              + side * (Random.value - 0.5f) * j + up * (Random.value - 0.5f) * j;
                tr.rotation = baseRot * Quaternion.Euler((Random.value - 0.5f) * 7f, (Random.value - 0.5f) * 7f, 0f);
                tr.localScale = new Vector3(rr * (1f + (Random.value - 0.5f) * 0.16f), rr * (1f + (Random.value - 0.5f) * 0.16f), 1f + 4f * al * power);

                float flick = 1f - ringFlicker + ringFlicker * Random.value;
                ringMat[i].color = redColor * (Mathf.Min(1f, al * 1.4f) * (1f - i * 0.1f) * flick);

                WaveRing(ringMesh[i], t * 9f + i * 1.7f, ringWaviness, 6 + i);
            }
        }

        void WaveRing(Mesh m, float phase, float amp, int k)
        {
            for (int v = 0; v < ringBase.Length; v++)
            {
                Vector3 b = ringBase[v];
                float th = Mathf.Atan2(b.y, b.x);
                float f = 1f + amp * Mathf.Sin(k * th + phase) + amp * 0.5f * Mathf.Sin((k * 2 + 1) * th - phase * 1.3f);
                ringWork[v] = new Vector3(b.x * f, b.y * f, b.z + amp * 0.6f * Mathf.Sin(k * th * 0.5f + phase * 0.7f));
            }
            m.vertices = ringWork;
            m.RecalculateBounds();
        }

        void UpdateShake()
        {
            Camera cam = shakeCamera ? shakeCamera : Camera.main;
            if (!cam || cameraShake <= 0f) return;
            if (t < 0.3f)
            {
                if (!shakeCached) { shakeBase = cam.transform.localPosition; shakeCached = true; }
                float s = (1f - t / 0.3f) * cameraShake * 0.85f * power * 1.9f;
                cam.transform.localPosition = shakeBase + Random.insideUnitSphere * s;
            }
            else RestoreShake();
        }

        void RestoreShake()
        {
            if (!shakeCached) return;
            Camera cam = shakeCamera ? shakeCamera : Camera.main;
            if (cam) cam.transform.localPosition = shakeBase;
            shakeCached = false;
        }

        void OnGUI()
        {
            if (flashA <= 0f) return;
            var c = GUI.color;
            GUI.color = new Color(1f, 0.16f, 0.16f, flashA);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), flashTex);
            GUI.color = c;
        }

        // --------------------------------------------------------- emission
        void EmitLaunchBurst()
        {
            Vector3 dir = (to - from).normalized;
            Quaternion rot = Quaternion.LookRotation(dir);

            int n = Mathf.RoundToInt(60 + density * 5);
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f, sp = (12f + Random.value * 24f) * power;
                Vector3 radial = rot * new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                Vector3 vel = radial * sp + dir * (1.5f + Random.value * 6f) * power;
                Vector3 pos = from + dir * 0.3f + radial * 0.35f;
                Emit(hotPs, pos, vel, 0.28f + Random.value * 0.4f, 0.10f + Random.value * 0.16f);
                if (i % 2 == 0) Emit(glowPs, pos, vel * 0.8f, 0.4f + Random.value * 0.5f, 0.24f + Random.value * 0.4f);
            }

            int nr = Mathf.RoundToInt(260 + density * 5);
            for (int i = 0; i < nr; i++)
            {
                float a = Random.value * Mathf.PI * 2f, sp = (14f + Random.value * 30f) * power;
                Vector3 radial = rot * new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                Emit(redPs, from + dir * 0.3f, radial * sp + dir * (2f + Random.value * 12f) * power,
                     0.25f + Random.value * 0.45f, (0.08f + Random.value * 0.16f) * 1.5f);
            }

            int nw = Mathf.RoundToInt((90 + density * 3f) * 1.6f);
            for (int i = 0; i < nw; i++)
            {
                float a = Random.value * Mathf.PI * 2f, sp = (10f + Random.value * 20f) * power;
                Vector3 radial = rot * new Vector3(Mathf.Cos(a), Mathf.Sin(a) * 0.7f, 0f);
                Emit(whitePs, from + dir * 0.4f + Random.insideUnitSphere * 0.2f,
                     radial * sp + dir * (4f + Random.value * 14f) * power,
                     0.7f + Random.value * 0.8f, 0.32f + Random.value * 0.38f);
            }
        }

        void EmitTrail(Vector3 head, Vector3 dir, Vector3 side)
        {
            int count = Mathf.RoundToInt((150f + density * 14f) * power * trailSparks * Time.deltaTime);
            for (int i = 0; i < count; i++)
            {
                Vector3 pos = head - dir * (Random.value * 2.2f * power) + Random.insideUnitSphere * 0.4f * power;
                Vector3 vel = -dir * (2f + Random.value * 10f) * power + Random.insideUnitSphere * sparkSpread * 6f * power;
                Emit(hotPs, pos, vel, 0.1f + Random.value * 0.22f, 0.05f + Random.value * 0.09f);
                if (i % 4 == 0) Emit(glowPs, pos, vel * 0.7f, 0.2f + Random.value * 0.3f, 0.14f + Random.value * 0.28f);
            }
            if (!groundSparks) return;
            int gc = Mathf.RoundToInt((25f + density * 2.5f) * Time.deltaTime * 30f);
            float groundY = (muzzle ? muzzle.position.y : transform.position.y) - 1.7f;
            for (int i = 0; i < gc; i++)
            {
                float s = Random.value < 0.5f ? -1f : 1f;
                Vector3 pos = head - dir * (Random.value * 4f * power);
                pos.y = groundY + 0.08f + Random.value * 0.3f;
                pos += side * s * (0.5f + Random.value * 1.6f);
                Vector3 vel = -dir * (1f + Random.value * 5f) * power + Vector3.up * (1.5f + Random.value * 5f) * power + side * s * (1f + Random.value * 4f) * power;
                Emit(i % 2 == 0 ? redPs : hotPs, pos, vel, 0.3f + Random.value * 0.5f, 0.06f + Random.value * 0.1f);
            }
        }

        void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, float life, float size)
        {
            var ep = new ParticleSystem.EmitParams { position = pos, velocity = vel, startLifetime = life, startSize = size * particleSize, applyShapeToPosition = false };
            ps.Emit(ep, 1);
        }

        void HideAll()
        {
            line.enabled = lineGlow.enabled = false;
            foreach (var a in arcs) a.enabled = false;
            foreach (var r in ringTr) r.gameObject.SetActive(false);
        }

        // --------------------------------------------------------- utility
        static Texture2D MakeDot(bool hard)
        {
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float r = new Vector2(x - N / 2f + 0.5f, y - N / 2f + 0.5f).magnitude / (N / 2f);
                    float a = hard ? Mathf.Clamp01((1f - r) * 7f) : Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f);
                    px[y * N + x] = new Color(1, 1, 1, a);
                }
            tex.SetPixels(px); tex.Apply();
            return tex;
        }

        static Mesh BuildTorus(int radial, int tubeSeg, float tubeR)
        {
            var verts = new Vector3[(radial + 1) * (tubeSeg + 1)];
            var tris = new int[radial * tubeSeg * 6];
            int v = 0;
            for (int i = 0; i <= radial; i++)
            {
                float u = (float)i / radial * Mathf.PI * 2f;
                Vector3 c = new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0f);
                for (int j = 0; j <= tubeSeg; j++)
                {
                    float w = (float)j / tubeSeg * Mathf.PI * 2f;
                    verts[v++] = c + (c * Mathf.Cos(w) + Vector3.forward * Mathf.Sin(w)) * tubeR;
                }
            }
            int ti = 0;
            for (int i = 0; i < radial; i++)
                for (int j = 0; j < tubeSeg; j++)
                {
                    int a = i * (tubeSeg + 1) + j, b = a + tubeSeg + 1;
                    tris[ti++] = a; tris[ti++] = b; tris[ti++] = a + 1;
                    tris[ti++] = a + 1; tris[ti++] = b; tris[ti++] = b + 1;
                }
            var m = new Mesh { name = "BerserkRing", vertices = verts, triangles = tris };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
