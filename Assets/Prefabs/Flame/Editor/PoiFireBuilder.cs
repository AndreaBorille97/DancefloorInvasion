#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

/// <summary>
/// Costruisce l'intero effetto: le due teste + i tre ParticleSystem per testa
/// (fiamma, scia, scintille) già configurati con i valori della documentazione.
///
/// Menu: GameObject ▸ Effects ▸ Poi di fuoco (danza)
///
/// Crea anche il materiale additivo in Assets/PoiFire/ se non esiste.
/// </summary>
public static class PoiFireBuilder
{
    const string ASSET_DIR = "Assets/PoiFire";
    static readonly Color HOT   = new Color32(0xff, 0xf1, 0xc9, 0xff);
    static readonly Color WARM  = new Color32(0xff, 0xd1, 0x66, 0xff);
    static readonly Color MAIN  = new Color32(0xff, 0x7a, 0x18, 0xff);
    static readonly Color COOL  = new Color32(0xd3, 0x3c, 0x08, 0xff);
    static readonly Color SMOKE = new Color32(0x7c, 0x1a, 0x04, 0xff);
    static readonly Color TRAIL = new Color32(0x8f, 0x1d, 0x05, 0xff);

    [MenuItem("GameObject/Effects/Poi di fuoco (danza)", false, 10)]
    public static void Create(MenuCommand cmd)
    {
        var root = new GameObject("PoiFireDance");
        GameObjectUtility.SetParentAndAlign(root, cmd.context as GameObject);

        var dance = root.AddComponent<PoiFireDance>();
        dance.CreateHeads();

        Material mat = GetOrCreateMaterial();

        BuildHead(dance.headLeft, mat);
        BuildHead(dance.headRight, mat);

        Undo.RegisterCreatedObjectUndo(root, "Crea Poi di fuoco");
        Selection.activeObject = root;
        Debug.Log("Poi di fuoco creato. Regola la velocità dal campo 'Spin Speed'.");
    }

    static void BuildHead(Transform head, Material mat)
    {
        BuildFlame(head, mat);
        BuildTrail(head, mat);
        BuildSparks(head, mat);
    }

    // ---- FX_Flame: il corpo della fiamma ----------------------------------
    static void BuildFlame(Transform parent, Material mat)
    {
        var go = new GameObject("FX_Flame");
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.duration = 2f;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.44f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.40f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = -0.14f;
        main.simulationSpace = ParticleSystemSimulationSpace.World; // indispensabile
        main.maxParticles = 140;

        var em = ps.emission;
        em.rateOverTime = 85f;

        var sh = ps.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.06f;

        var vol = ps.velocityOverLifetime;
        vol.enabled = true;
        vol.space = ParticleSystemSimulationSpace.Local;
        vol.y = new ParticleSystem.MinMaxCurve(1f, Curve(1f, 0.25f));

        var lim = ps.limitVelocityOverLifetime;
        lim.enabled = true;
        lim.limit = 2.2f;
        lim.dampen = 0.35f;

        var inh = ps.inheritVelocity;
        inh.enabled = true;
        inh.mode = ParticleSystemInheritVelocityMode.Current;
        inh.curve = new ParticleSystem.MinMaxCurve(0.25f);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(CelGradient());

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, SizeCurve());

        var rol = ps.rotationOverLifetime;
        rol.enabled = true;
        rol.z = new ParticleSystem.MinMaxCurve(-45f * Mathf.Deg2Rad, 45f * Mathf.Deg2Rad);

        var nz = ps.noise;
        nz.enabled = true;
        nz.strength = 0.35f;
        nz.frequency = 1.4f;
        nz.octaveCount = 2;
        nz.damping = true;
        nz.quality = ParticleSystemNoiseQuality.Medium;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.alignment = ParticleSystemRenderSpace.View;
        r.sortingFudge = -5;
        r.sharedMaterial = mat;
    }

    // ---- FX_Trail: la scia del piano --------------------------------------
    static void BuildTrail(Transform parent, Material mat)
    {
        var go = new GameObject("FX_Trail");
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.duration = 2f;
        main.loop = true;
        main.startLifetime = 0.42f;
        main.startSpeed = 0f;          // la disegna il movimento, non la velocità
        main.startSize = 0.16f;
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World; // obbligatorio
        main.maxParticles = 200;

        var em = ps.emission;
        em.rateOverTime = 0f;
        em.rateOverDistance = 90f;     // uniforme a ogni velocità

        var sh = ps.shape;
        sh.enabled = false;

        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(MAIN, 0f), new GradientColorKey(TRAIL, 0.6f) },
            new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, EaseOutCurve());

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.alignment = ParticleSystemRenderSpace.View;
        r.sortingFudge = 5;            // dietro alla fiamma
        r.sharedMaterial = mat;
    }

    // ---- FX_Sparks: scintille staccate ------------------------------------
    static void BuildSparks(Transform parent, Material mat)
    {
        var go = new GameObject("FX_Sparks");
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.duration = 2f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
        main.gravityModifier = 0.45f;  // ricadono: è ciò che vende il peso
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 60;

        var em = ps.emission;
        em.rateOverTime = 22f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 4, 8, 1, 0.5f) });

        var inh = ps.inheritVelocity;
        inh.enabled = true;
        inh.mode = ParticleSystemInheritVelocityMode.Current;
        inh.curve = new ParticleSystem.MinMaxCurve(0.35f);

        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(WARM, 0f), new GradientColorKey(MAIN, 0.4f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.lengthScale = 2.5f;
        r.velocityScale = 0.06f;
        r.alignment = ParticleSystemRenderSpace.Velocity;
        r.sharedMaterial = mat;
    }

    // ---- gradiente a fasce: chiavi doppie = stacco netto (cel) -------------
    static Gradient CelGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(HOT,   0.00f),
                new GradientColorKey(HOT,   0.18f),
                new GradientColorKey(WARM,  0.19f),
                new GradientColorKey(WARM,  0.42f),
                new GradientColorKey(MAIN,  0.43f),
                new GradientColorKey(COOL,  0.69f),
                new GradientColorKey(SMOKE, 0.89f)
            },
            new[]
            {
                new GradientAlphaKey(1f,    0.00f),
                new GradientAlphaKey(1f,    0.68f),
                new GradientAlphaKey(0.65f, 0.69f),
                new GradientAlphaKey(0.65f, 0.88f),
                new GradientAlphaKey(0f,    1.00f)
            });
        return g;
    }

    static AnimationCurve Curve(float a, float b)
    {
        return AnimationCurve.EaseInOut(0f, a, 1f, b);
    }

    static AnimationCurve SizeCurve()
    {
        var c = new AnimationCurve();
        c.AddKey(new Keyframe(0f, 0.55f));
        c.AddKey(new Keyframe(0.35f, 1f));
        c.AddKey(new Keyframe(1f, 0.15f));
        for (int i = 0; i < c.length; i++) c.SmoothTangents(i, 0f);
        return c;
    }

    static AnimationCurve EaseOutCurve()
    {
        var c = new AnimationCurve();
        c.AddKey(new Keyframe(0f, 1f, 0f, 0f));
        c.AddKey(new Keyframe(1f, 0f, -1.6f, -1.6f));
        return c;
    }

    // ---- materiale additivo ------------------------------------------------
    static Material GetOrCreateMaterial()
    {
        if (!AssetDatabase.IsValidFolder(ASSET_DIR))
            AssetDatabase.CreateFolder("Assets", "PoiFire");

        string path = ASSET_DIR + "/FX_Flame_Cel.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        // URP se disponibile, altrimenti built-in
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        bool urp = sh != null;
        if (!urp) sh = Shader.Find("Legacy Shaders/Particles/Additive");

        var mat = new Material(sh);
        mat.name = "FX_Flame_Cel";
        if (urp)
        {
            mat.SetFloat("_Surface", 1f);       // Transparent
            mat.SetFloat("_Blend", 1f);         // Additive
            // Multiply è 0, non 2: nel particle shader di URP l'enum ColorMode è
            // Multiply/Additive/Subtractive/Overlay/Color/Difference. Con 2 il materiale
            // finisce in Add/Sub/Diff e, col _BaseColorAddSubDiff di default a -1, SOTTRAE il
            // colore della particella dal bianco: le fiamme arancioni risultavano invertite,
            // cioè blu e azzurre. Il float da solo non basta, la modalità è guidata dalle
            // keyword, quindi quella sbagliata va anche disattivata esplicitamente.
            mat.SetFloat("_ColorMode", 0f);     // Multiply
            mat.DisableKeyword("_COLORADDSUBDIFF_ON");
            mat.DisableKeyword("_COLOROVERLAY_ON");
            mat.DisableKeyword("_COLORCOLOR_ON");
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        }
        mat.mainTexture = LoadFlameTexture();

        AssetDatabase.CreateAsset(mat, path);
        AssetDatabase.SaveAssets();
        return mat;
    }

    // Texture della fiamma. Usa PoiFire_Flame.png se l'hai copiato insieme agli
    // script (è la stessa dell'anteprima); altrimenti la genera identica.
    static Texture2D LoadFlameTexture()
    {
        string shipped = ASSET_DIR + "/PoiFire_Flame.png";
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(shipped);
        if (tex != null) { ConfigureImport(shipped); return tex; }

        // cerca il PNG ovunque nel progetto, nel caso sia stato copiato altrove
        foreach (string guid in AssetDatabase.FindAssets("PoiFire_Flame t:Texture2D"))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            if (t != null) { ConfigureImport(p); return t; }
        }
        return SoftDisc();
    }

    static void ConfigureImport(string path)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return;
        bool dirty = false;
        if (imp.textureType != TextureImporterType.Default) { imp.textureType = TextureImporterType.Default; dirty = true; }
        if (!imp.alphaIsTransparency) { imp.alphaIsTransparency = true; dirty = true; }
        if (imp.wrapMode != TextureWrapMode.Clamp) { imp.wrapMode = TextureWrapMode.Clamp; dirty = true; }
        if (dirty) imp.SaveAndReimport();
    }

    // Fallback: disco a bordo duro generato a runtime, identico al PNG.
    static Texture2D SoftDisc()
    {
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        tex.name = "PoiFire_Placeholder";
        var px = new Color[N * N];
        float c = (N - 1) * 0.5f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = d < 0.9f ? 1f : (d < 1f ? 0.45f : 0f);
                px[y * N + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();

        string path = ASSET_DIR + "/PoiFire_Placeholder.png";
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
#endif
