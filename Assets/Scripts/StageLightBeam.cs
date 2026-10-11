using UnityEngine;

// Faro da palco a cono: da mettere sul GameObject creato dal comando Tools/Luci/Crea un faro a
// cono (vedi StageLightBeamBuilder). Il fascio visibile nell'aria lo disegna lo shader
// (Assets/Shaders/FascioLuce.shader) su un cono generato qui; qui c'è quello che uno shader non
// può fare:
//
//  - la mesh del cono, ricostruita ogni volta che cambi apertura o lunghezza, così la forma si
//    tara dall'Inspector e si vede subito anche fuori da Play Mode (ExecuteAlways);
//
//  - la spot light vera, se sullo stesso GameObject ce n'è una: è lei a disegnare la pozza di
//    luce a terra, che il solo fascio additivo non fa. Apertura, portata e colore li prende dal
//    fascio, così i due restano sempre allineati;
//
//  - l'oscillazione da "testa mobile" (solo in Play Mode): il faro spazza avanti e indietro
//    attorno all'orientamento con cui l'hai piazzato. Ogni faro parte da un punto a caso
//    dell'oscillazione, altrimenti fari vicini si muoverebbero in coro e si vedrebbe che è finto;
//
//  - l'aggancio a un faretto animato (solo in Play Mode): all'avvio si segna dove sta il fascio
//    rispetto al faretto, che in quel momento è ancora nella posa neutra della scena, e da lì in
//    poi lo tiene in quella stessa posizione relativa, come se fosse suo figlio. Il punto di
//    emissione resta così incollato alla lente anche quando il faretto ruota attorno alla staffa;
//
//  - i colori che girano (solo in Play Mode): la tinta scorre su tutto l'arcobaleno partendo
//    dal colore scelto, fascio e luce a terra insieme. Fari con colori di partenza diversi
//    restano sempre sfasati tra loro (il builder li crea già sparsi sull'arcobaleno).
//
// Il colore passa per un MaterialPropertyBlock, non per renderer.material: fari diversi possono
// avere colori diversi condividendo lo stesso materiale, senza copie (vedi la nota sulle
// istanze di materiale sotto il sound system in ScreenFrameAnimator).
//
// Il cono punta lungo +Z locale, come la spot light: ruota il GameObject per mirare.
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class StageLightBeam : MonoBehaviour
{
    [Header("Fascio")]
    [ColorUsage(false, true)]
    [SerializeField] private Color color = new Color(2.5f, 2.2f, 1.6f);
    [Tooltip("Apertura totale del cono, in gradi.")]
    [Range(2f, 90f)] [SerializeField] private float angle = 22f;
    [Tooltip("Lunghezza del fascio, in metri. Il fascio si spegne sfumando verso la fine (vedi il materiale), quindi può tranquillamente attraversare il pavimento.")]
    [SerializeField] private float length = 10f;
    [Tooltip("Raggio del fascio all'attacco, cioè quanto è grande la lente del faro.")]
    [SerializeField] private float sourceRadius = 0.15f;

    [Header("Colori che cambiano (Play Mode)")]
    [Tooltip("Il faro gira su tutti i colori dell'arcobaleno, partendo dal Colore qui sopra. Dai ai fari vicini colori di partenza diversi: così non sono mai tutti dello stesso colore insieme.")]
    [SerializeField] private bool cycleColors = true;
    [Tooltip("Giri completi dell'arcobaleno al secondo: 0.1 = un giro ogni 10 secondi.")]
    [SerializeField] private float cycleSpeed = 0.12f;

    [Header("Luce a terra")]
    [Tooltip("Intensità della spot light (se presente sullo stesso GameObject). 0 = solo il fascio nell'aria, niente pozza di luce a terra.")]
    [SerializeField] private float lightIntensity = 40f;

    [Header("Aggancio al faretto (Play Mode)")]
    [Tooltip("Il faretto da seguire. Vuoto = si aggancia da solo al Faretto più vicino in scena. Piazza il fascio sulla lente con il faretto in posa neutra: all'avvio si segna quella posizione e poi la mantiene mentre il faretto si muove.")]
    [SerializeField] private Transform followTarget;
    [Tooltip("Se attivo e il faretto da seguire è vuoto, cerca da solo il Faretto più vicino.")]
    [SerializeField] private bool autoFindFaretto = true;
    [Tooltip("Oltre questa distanza (metri) il Faretto più vicino non viene considerato suo: il fascio resta fisso.")]
    [SerializeField] private float autoFindMaxDistance = 2f;

    [Header("Oscillazione (Play Mode)")]
    [Tooltip("Di quanti gradi spazza a destra e a sinistra. 0 = faro fermo.")]
    [SerializeField] private float panAmplitude = 0f;
    [Tooltip("Di quanti gradi spazza in su e in giù. 0 = faro fermo.")]
    [SerializeField] private float tiltAmplitude = 0f;
    [Tooltip("Oscillazioni complete al secondo.")]
    [SerializeField] private float sweepSpeed = 0.25f;

    private const int Segments = 32;
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private Mesh mesh;
    private MaterialPropertyBlock properties;
    private Light spotLight;
    private Quaternion baseRotation;
    private Vector3 basePosition;
    private bool following;
    private Vector3 offsetPosition;
    private Quaternion offsetRotation;
    private float sweepPhase;
    private bool shapeDirty = true;

    void OnEnable()
    {
        shapeDirty = true;
        baseRotation = transform.localRotation;
        basePosition = transform.localPosition;
        sweepPhase = Random.value * 100f;
        Apply();
    }

    void OnDisable()
    {
        if (Application.isPlaying)
        {
            transform.localRotation = baseRotation;
            transform.localPosition = basePosition;
        }
    }

    // In Start le animazioni non hanno ancora mosso niente (il primo campionamento arriva dopo
    // gli Update del primo frame): i faretti sono ancora nella posa della scena, cioè quella
    // neutra in cui il fascio è stato allineato a mano.
    void Start()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (followTarget == null && autoFindFaretto)
        {
            followTarget = FindNearestFaretto();
        }

        if (followTarget != null)
        {
            offsetPosition = followTarget.InverseTransformPoint(transform.position);
            offsetRotation = Quaternion.Inverse(followTarget.rotation) * transform.rotation;
            following = true;

            // Sotto un genitore a scala non uniforme (tipo Floor) la rotazione del faretto
            // verrebbe schiacciata e il fascio farebbe solo su e giù.
            Vector3 s = transform.lossyScale;
            if (Mathf.Abs(s.x - s.y) > 0.01f * Mathf.Abs(s.x) || Mathf.Abs(s.x - s.z) > 0.01f * Mathf.Abs(s.x))
            {
                Debug.LogWarning($"[StageLightBeam] {name}: scala non uniforme {s}, il fascio non può ruotare come il faretto. Lancia Tools/Luci/Raddrizza i fari a cono.", this);
            }
        }
    }

    private Transform FindNearestFaretto()
    {
        Transform nearest = null;
        float bestDistance = autoFindMaxDistance;
        foreach (Transform candidate in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (!candidate.name.StartsWith("Faretto", System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            float distance = DistanceToFaretto(candidate);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = candidate;
            }
        }

        if (nearest == null)
        {
            Debug.LogWarning($"[StageLightBeam] {name}: nessun Faretto entro {autoFindMaxDistance} m, il fascio resta fisso.", this);
        }

        return nearest;
    }

    // Distanza dalla sagoma del faretto, non dal suo perno: il perno sta sulla staffa, lontano
    // dalla lente, e un faretto vicino potrebbe avere il perno più vicino al fascio del suo.
    private float DistanceToFaretto(Transform faretto)
    {
        Renderer[] renderers = faretto.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return Vector3.Distance(faretto.position, transform.position);
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return Vector3.Distance(bounds.ClosestPoint(transform.position), transform.position);
    }

    void OnDestroy()
    {
        if (mesh != null)
        {
            if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
        }
    }

    // In OnValidate non si possono toccare mesh e renderer: si segna e lo si fa al prossimo Update.
    void OnValidate()
    {
        length = Mathf.Max(0.1f, length);
        sourceRadius = Mathf.Max(0f, sourceRadius);
        shapeDirty = true;
    }

    void Update()
    {
        if (shapeDirty)
        {
            Apply();
        }

        if (Application.isPlaying && !following && (panAmplitude != 0f || tiltAmplitude != 0f))
        {
            transform.localRotation = baseRotation * GetSweep();
        }

        if (Application.isPlaying && cycleColors)
        {
            ApplyColor(GetCycledColor());
        }
    }

    // In LateUpdate le animazioni del frame sono già applicate: il fascio segue la posa del
    // faretto di questo frame, non quella del frame prima.
    void LateUpdate()
    {
        if (!following || followTarget == null)
        {
            return;
        }

        transform.SetPositionAndRotation(
            followTarget.TransformPoint(offsetPosition),
            followTarget.rotation * offsetRotation * GetSweep());
    }

    private Quaternion GetSweep()
    {
        if (panAmplitude == 0f && tiltAmplitude == 0f)
        {
            return Quaternion.identity;
        }

        float t = (Time.time + sweepPhase) * sweepSpeed * Mathf.PI * 2f;
        // Tilt al doppio della frequenza del pan: il faro disegna un otto invece di una
        // semplice riga avanti e indietro, che è il movimento tipico delle teste mobili.
        return Quaternion.Euler(Mathf.Sin(t * 2f) * tiltAmplitude, Mathf.Sin(t) * panAmplitude, 0f);
    }

    // Stesso colore di partenza, con la tinta spostata avanti nel tempo: saturazione e
    // luminosità (anche HDR) restano quelle scelte nell'Inspector, cambia solo "quale" colore.
    private Color GetCycledColor()
    {
        float max = Mathf.Max(color.r, color.g, color.b, 0.0001f);
        Color.RGBToHSV(color / max, out float hue, out float saturation, out _);
        hue = Mathf.Repeat(hue + Time.time * cycleSpeed, 1f);
        Color cycled = Color.HSVToRGB(hue, saturation, 1f) * max;
        cycled.a = 1f;
        return cycled;
    }

    private void Apply()
    {
        shapeDirty = false;
        BuildMesh();

        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        if (spotLight == null)
        {
            spotLight = GetComponent<Light>();
        }

        if (spotLight != null)
        {
            spotLight.type = LightType.Spot;
            spotLight.spotAngle = angle;
            spotLight.innerSpotAngle = angle * 0.6f;
            spotLight.range = length * 1.5f;
            spotLight.intensity = lightIntensity;
            spotLight.enabled = lightIntensity > 0f;
        }

        ApplyColor(color);
    }

    private void ApplyColor(Color beamColor)
    {
        properties ??= new MaterialPropertyBlock();
        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.GetPropertyBlock(properties);
        properties.SetColor(ColorId, beamColor);
        meshRenderer.SetPropertyBlock(properties);

        if (spotLight != null)
        {
            // La luce vuole un colore normale (0..1): l'HDR del fascio serve solo al Bloom.
            float max = Mathf.Max(beamColor.r, beamColor.g, beamColor.b, 0.0001f);
            spotLight.color = new Color(beamColor.r / max, beamColor.g / max, beamColor.b / max);
        }
    }

    // Tronco di cono aperto (senza tappi) lungo +Z. uv.x = giro attorno all'asse, uv.y = lungo
    // il fascio (0 al faro, 1 in fondo): è da lì che lo shader sfuma e disegna le striature.
    private void BuildMesh()
    {
        if (mesh == null)
        {
            mesh = new Mesh { name = "Fascio di luce", hideFlags = HideFlags.DontSave };
        }

        float endRadius = sourceRadius + length * Mathf.Tan(angle * 0.5f * Mathf.Deg2Rad);
        // La parete è inclinata: la normale non è puramente radiale, pende un po' all'indietro.
        float slope = (endRadius - sourceRadius) / length;

        Vector3[] vertices = new Vector3[(Segments + 1) * 2];
        Vector3[] normals = new Vector3[vertices.Length];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[Segments * 6];

        for (int i = 0; i <= Segments; i++)
        {
            float u = (float)i / Segments;
            float a = u * Mathf.PI * 2f;
            Vector3 radial = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            Vector3 normal = (radial - Vector3.forward * slope).normalized;

            vertices[i * 2] = radial * sourceRadius;
            vertices[i * 2 + 1] = radial * endRadius + Vector3.forward * length;
            normals[i * 2] = normal;
            normals[i * 2 + 1] = normal;
            uvs[i * 2] = new Vector2(u, 0f);
            uvs[i * 2 + 1] = new Vector2(u, 1f);
        }

        for (int i = 0; i < Segments; i++)
        {
            int v = i * 2;
            int t = i * 6;
            triangles[t] = v;
            triangles[t + 1] = v + 1;
            triangles[t + 2] = v + 2;
            triangles[t + 3] = v + 2;
            triangles[t + 4] = v + 1;
            triangles[t + 5] = v + 3;
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh = mesh;
    }
}
