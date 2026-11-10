using UnityEngine;
using UnityEngine.Rendering;

// Replica esatta dell'animazione web: fade-in → hold → fade-out → pausa, in loop,
// smoothstep sull'opacità, scala 0.94→1, rotazione continua su Y.
// Trascina Cocaine.obj in scena e aggiungi questo componente all'oggetto.
public class CocaineFader : MonoBehaviour
{
    [Header("Tempi (secondi)")]
    [Min(0.05f)] public float fadeIn = 1f;
    [Min(0f)]    public float hold = 0f;
    [Min(0.05f)] public float fadeOut = 1f;
    [Min(0f)]    public float pause = 0.3f;

    [Header("Aspetto")]
    [Range(0.1f, 1f)] public float opacity = 0.55f;
    public Color color = new Color32(0xF4, 0xF7, 0xFA, 0xFF);
    public Color emission = new Color32(0x6A, 0x73, 0x7C, 0xFF);
    [Range(0f, 1f)] public float emissionIntensity = 0.25f;
    public bool rotate = true;
    [Tooltip("Gradi/secondo (web: 0.004 rad/frame a 60fps)")]
    public float rotationSpeed = 13.75f;

    [Header("Setup scena identico al web")]
    public bool setupCamera = true;
    public bool setupLights = true;

    Material mat;
    Renderer[] rends;
    Vector3 baseScale;
    float t0;

    void Start()
    {
        baseScale = transform.localScale;
        t0 = Time.time;
        mat = BuildMaterial();
        rends = GetComponentsInChildren<Renderer>(true);
        foreach (var r in rends) { r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; }
        if (setupCamera) SetupCamera();
        if (setupLights) SetupLights();
    }

    void Update()
    {
        float T = fadeIn + hold + fadeOut + pause;
        float t = (Time.time - t0) % T;
        float k = t < fadeIn ? t / fadeIn
                : t < fadeIn + hold ? 1f
                : t < fadeIn + hold + fadeOut ? 1f - (t - fadeIn - hold) / fadeOut
                : 0f;
        k = k * k * (3f - 2f * k);

        var c = color; c.a = opacity * k;
        mat.SetColor("_BaseColor", c);
        mat.SetColor("_Color", c);
        bool vis = k > 0.001f;
        foreach (var r in rends) r.enabled = vis;

        transform.localScale = baseScale * (0.94f + 0.06f * k);
        // OBJ importato in Unity è specchiato su X → rotazione negativa = stesso verso del web
        if (rotate) transform.Rotate(0f, -rotationSpeed * Time.deltaTime, 0f, Space.World);
    }

    Material BuildMaterial()
    {
        var urp = Shader.Find("Universal Render Pipeline/Lit");
        Material m;
        if (urp != null)
        {
            m = new Material(urp);
            m.SetFloat("_Surface", 1f);           // Transparent
            m.SetFloat("_Blend", 0f);             // Alpha
            m.SetFloat("_Smoothness", 0.85f);
            m.SetFloat("_Metallic", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
        else
        {
            m = new Material(Shader.Find("Standard"));
            m.SetFloat("_Mode", 2f);              // Fade
            m.SetFloat("_Glossiness", 0.85f);
            m.SetFloat("_Metallic", 0f);
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 1);                   // come il web (depthWrite true)
        m.renderQueue = (int)RenderQueue.Transparent;
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", emission * emissionIntensity);
        return m;
    }

    void SetupCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;
        cam.fieldOfView = 35f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color32(0x0B, 0x10, 0x16, 0xFF);
        // diagonale modello 15.562 × 1.6 (come nel web), in scala oggetto
        float dist = 15.562f * 1.6f * baseScale.x;
        cam.transform.position = transform.position + new Vector3(0f, 0f, dist);
        cam.transform.LookAt(transform.position);
    }

    void SetupLights()
    {
        foreach (var l in FindObjectsOfType<Light>())
            if (l.type == LightType.Directional) l.gameObject.SetActive(false);

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = (Color)new Color32(0xFF, 0xFF, 0xFF, 0xFF) * 1.0f;
        RenderSettings.ambientEquatorColor = new Color32(0x8A, 0x8F, 0x94, 0xFF);
        RenderSettings.ambientGroundColor = new Color32(0x1C, 0x22, 0x28, 0xFF);

        MakeLight("Key", new Vector3(-5f, 8f, 6f), Color.white, 1.4f);
        MakeLight("Fill", new Vector3(6f, -3f, -4f), new Color32(0xDF, 0xE8, 0xF0, 0xFF), 0.8f);
    }

    void MakeLight(string n, Vector3 pos, Color c, float i)
    {
        var go = new GameObject("Cocaine " + n + " Light");
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional; l.color = c; l.intensity = i; l.shadows = LightShadows.None;
        go.transform.rotation = Quaternion.LookRotation(-pos.normalized);
    }
}
