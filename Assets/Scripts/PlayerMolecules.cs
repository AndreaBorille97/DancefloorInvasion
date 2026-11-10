using UnityEngine;
using UnityEngine.Rendering;

// Molecola 3D che compare sopra la testa del Player quando raccoglie o usa un potere, colorata
// come il relativo powerup:
//   alcool     -> berserk raccolto (BerserkAmmoPickup)        rosso
//   anfetamina -> vita raccolta (LifeUpPowerUpAmmoPickup)     giallo
//   LSD        -> Hoffman usato (PlayerHoffman)               viola
//   ketamina   -> khole usato (PlayerKhole)                   azzurro
//   cocaina    -> scatto usato (PlayerDash)                   bianco
//   MDMA       -> serotonina usata (PlayerSerotonin)          arancione
// Basta chiamare PlayerMolecules.Show(Molecola.X). L'animazione è quella dei Fader che
// accompagnavano i modelli (fade-in con smoothstep, scala 0.94 -> 1, rotazione su Y), ma fatta
// una volta sola invece che in loop. Una molecola nuova sostituisce quella ancora a schermo.
//
// I modelli (.obj) stanno in Assets/Resources/Molecole, il materiale trasparente in
// Assets/Resources/Molecola.mat: il colore di ogni molecola si mette con un MaterialPropertyBlock.
// I vecchi script *Fader nella cartella models/MOLECOLE non si usano: sposterebbero camera e luci.
//
// Si aggiunge da solo al Player all'avvio (vedi AttachToPlayer): non serve metterlo a mano in scena.
public class PlayerMolecules : MonoBehaviour
{
    public enum Molecola { Alcool, Anfetamina, Lsd, Ketamina, Cocaina, Mdma, Thc }

    [Header("Posizione")]
    [Tooltip("Spazio tra la cima della testa del Player e il fondo della molecola, in metri.")]
    [SerializeField] private float gapAboveHead = 0.3f;
    [Tooltip("Grandezza media delle molecole in metri. Tutte hanno la stessa quantità di atomi a schermo: " +
             "quelle con pochi atomi (alcool) vengono un po' più strette di quelle con tanti (LSD).")]
    [SerializeField] private float size = 2f;

    [Header("Tempi (secondi)")]
    [SerializeField] private float fadeIn = 0.35f;
    [SerializeField] private float hold = 1.1f;
    [SerializeField] private float fadeOut = 0.6f;

    [Header("Aspetto")]
    [Range(0.1f, 1f)] [SerializeField] private float opacity = 0.75f;
    [Range(0f, 2f)] [SerializeField] private float emissionIntensity = 0.8f;
    [Tooltip("Gradi al secondo attorno a Y.")]
    [SerializeField] private float rotationSpeed = 60f;

    private const string ModelFolder = "Molecole/";       // Assets/Resources/Molecole
    private const string MaterialPath = "Molecola";       // Assets/Resources/Molecola.mat

    // I modelli hanno atomi della stessa grandezza ma in numero diverso (alcool ~9, LSD ~49):
    // riportandoli tutti allo stesso ingombro, l'alcool avrebbe atomi 3 volte più grossi e
    // sembrerebbe la più grande. Si scala invece in modo che i triangoli (cioè atomi e legami)
    // coprano la stessa superficie: scala = size * K / sqrt(triangoli). K è tarato perché
    // l'ingombro medio delle sei molecole venga circa size.
    private const float SizeCalibration = 28f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private struct Look
    {
        public string Model;
        public Color Color;

        public Look(string model, Color color)
        {
            Model = model;
            Color = color;
        }
    }

    // Colori presi dai powerup (sparkleColor/bubbleColor dei prefab, materiale per la vita).
    private static readonly Look[] Looks =
    {
        new Look("Ethanol",     new Color(1f, 0.08f, 0.08f)),  // BerserkAmmo
        new Look("Amphetamine", new Color(1f, 0.92f, 0f)),     // SpeedBoostAmmo (prima LifeUpAmmo)
        new Look("LSD",         new Color(0.7f, 0.1f, 1f)),    // HoffmanAmmo
        new Look("Ketamine",    new Color(0.1f, 0.75f, 1f)),   // KholeAmmo
        new Look("Cocaine",     new Color(1f, 1f, 1f)),        // SpeedUpPowerUpAmmo
        new Look("MDMA",        new Color(1f, 0.45f, 0f)),     // SerotoninAmmo
        new Look("THCA",        new Color(0.08f, 0.42f, 0.14f)), // LifeUpAmmo (la cima), verde scuro come la cima
    };

    private static PlayerMolecules instance;

    private Material material;
    private SkinnedMeshRenderer[] playerRenderers;
    private float headHeight; // cima della testa sopra i piedi, misurata alla comparsa
    private Transform current;
    private Renderer[] currentRenderers;
    private Look currentLook;
    private Vector3 baseScale;
    private float halfHeight; // metà altezza della molecola a scala piena, per appoggiarla sopra la testa
    private float age;
    private MaterialPropertyBlock block; // creato in Awake: Unity non lo permette nell'inizializzatore del campo

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToPlayer()
    {
        PlayerMovement player = FindAnyObjectByType<PlayerMovement>();
        if (player != null && player.GetComponent<PlayerMolecules>() == null)
        {
            player.gameObject.AddComponent<PlayerMolecules>();
        }
    }

    void Awake()
    {
        instance = this;
        block = new MaterialPropertyBlock();
        playerRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        material = Resources.Load<Material>(MaterialPath);
        if (material == null)
        {
            Debug.LogWarning($"[PlayerMolecules] Materiale Resources/{MaterialPath} non trovato: niente molecole.");
        }
    }

    void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }

        if (current != null)
        {
            Destroy(current.gameObject);
        }
    }

    public static void Show(Molecola molecola)
    {
        if (instance != null)
        {
            instance.Spawn(Looks[(int)molecola]);
        }
    }

    private void Spawn(Look look)
    {
        if (material == null)
        {
            return;
        }

        GameObject model = Resources.Load<GameObject>(ModelFolder + look.Model);
        if (model == null)
        {
            Debug.LogWarning($"[PlayerMolecules] Modello Resources/{ModelFolder}{look.Model} non trovato.");
            return;
        }

        if (current != null)
        {
            Destroy(current.gameObject);
        }

        // Non figlio del Player: deve seguirlo ma non girare con la mira (gira per conto suo su Y).
        GameObject go = Instantiate(model);
        go.name = "Molecola " + look.Model;
        current = go.transform;
        currentLook = look;
        currentRenderers = go.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in currentRenderers)
        {
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // GetIndexCount funziona anche con le mesh non leggibili (Read/Write spento sugli .obj).
        long indices = 0;
        foreach (MeshFilter filter in go.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            for (int i = 0; mesh != null && i < mesh.subMeshCount; i++)
            {
                indices += mesh.GetIndexCount(i);
            }
        }

        float triangles = Mathf.Max(1f, indices / 3f);
        baseScale = Vector3.one * (size * SizeCalibration / Mathf.Sqrt(triangles));

        // I modelli sono centrati sul pivot: a scala 1 i bounds sono quelli del modello.
        Bounds bounds = currentRenderers[0].bounds;
        foreach (Renderer r in currentRenderers)
        {
            bounds.Encapsulate(r.bounds);
        }

        halfHeight = bounds.extents.y * baseScale.y;
        headHeight = MeasureHeadHeight();
        age = 0f;
        Animate();
    }

    void LateUpdate()
    {
        if (current == null)
        {
            return;
        }

        age += Time.deltaTime;
        if (age >= fadeIn + hold + fadeOut)
        {
            Destroy(current.gameObject);
            current = null;
            return;
        }

        Animate();
    }

    // Altezza della testa sopra i piedi = punto più alto del corpo (solo le SkinnedMeshRenderer del
    // modello). Gli effetti figli del Player (anelli e fulmini dello scatto, AirShotVFX) alzavano i
    // bounds per un attimo e la molecola poi "cadeva" sulla testa: si misura una volta alla comparsa.
    private float MeasureHeadHeight()
    {
        float top = float.MinValue;
        foreach (SkinnedMeshRenderer r in playerRenderers)
        {
            if (r != null && r.enabled && r.gameObject.activeInHierarchy)
            {
                top = Mathf.Max(top, r.bounds.max.y);
            }
        }

        return top > float.MinValue ? top - transform.position.y : 2f;
    }

    private void Animate()
    {
        float k = age < fadeIn ? age / fadeIn
                : age < fadeIn + hold ? 1f
                : 1f - (age - fadeIn - hold) / fadeOut;
        k = Mathf.Clamp01(k);
        k = k * k * (3f - 2f * k);

        Vector3 position = transform.position;
        position.y += headHeight + gapAboveHead + halfHeight;
        current.position = position;
        current.localScale = baseScale * (0.94f + 0.06f * k);
        current.Rotate(0f, -rotationSpeed * Time.deltaTime, 0f, Space.World);

        Color color = currentLook.Color;
        color.a = opacity * k;
        block.SetColor(BaseColorId, color);
        block.SetColor(EmissionColorId, currentLook.Color * (emissionIntensity * k));
        foreach (Renderer r in currentRenderers)
        {
            r.enabled = k > 0.001f;
            r.SetPropertyBlock(block);
        }
    }
}
