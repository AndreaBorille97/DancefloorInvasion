using System.Collections.Generic;
using UnityEngine;

// Da mettere sul prefab dell'ammo khole, accanto a CollectibleShine. Fa sembrare il modello un
// ologramma e gli mette intorno dei cristallini azzurri:
//  - il modello trema sul posto (rumore, non un'oscillazione regolare), ha sempre attorno due
//    copie azzurre leggermente sfasate e ogni tanto sfarfalla, sparendo per un fotogramma;
//  - spesso ha un "glitch": per un attimo scatta di lato, si schiaccia e le copie azzurre si
//    staccano forti dalle due parti, come un segnale disturbato;
//  - i cristallini (ottaedri allungati, generati qui) gli girano intorno a quote diverse,
//    ruotando su sé stessi e pulsando di luce.
//
// Tutto procedurale: niente da collegare. Lavora sul figlio "Modello" (vedi AmmoModel), mentre
// CollectibleShine continua a far girare e ondeggiare la radice.
public class AmmoHologram : MonoBehaviour
{
    [Header("Tremolio olografico")]
    [Tooltip("Di quanto il modello trema sul posto (unità).")]
    [SerializeField] private float jitterAmount = 0.06f;
    [Tooltip("Quanto è nervoso il tremolio.")]
    [SerializeField] private float jitterFrequency = 9f;
    [Tooltip("Secondi fra un glitch e l'altro (scelti a caso in questo intervallo).")]
    [SerializeField] private Vector2 glitchInterval = new Vector2(0.2f, 0.7f);
    [Tooltip("Durata di un glitch, in secondi.")]
    [SerializeField] private float glitchDuration = 0.14f;
    [Tooltip("Di quanto scatta di lato il modello durante un glitch (unità).")]
    [SerializeField] private float glitchOffset = 0.18f;
    [Tooltip("Colore delle copie sfasate attorno al modello: sempre accese, più forti durante il glitch.")]
    [ColorUsage(false, true)]
    [SerializeField] private Color ghostColor = new Color(0.3f, 1.1f, 2.2f, 1f);
    [Tooltip("Di quanto le copie restano sfasate dal modello fuori dai glitch (unità).")]
    [SerializeField] private float ghostDrift = 0.05f;
    [Range(0f, 0.3f)]
    [Tooltip("Probabilità, a ogni fotogramma, che il modello sparisca per un attimo (lasciando solo le copie).")]
    [SerializeField] private float flickerChance = 0.06f;

    [Header("Cristallini")]
    [SerializeField] private int crystalCount = 8;
    [ColorUsage(false, true)]
    [SerializeField] private Color crystalColor = new Color(0.5f, 1.5f, 3.2f, 1f);
    [Tooltip("Distanza dei cristallini dal centro del modello (unità).")]
    [SerializeField] private float orbitRadius = 0.65f;
    [Tooltip("Giri al secondo attorno al modello.")]
    [SerializeField] private float orbitSpeed = 0.3f;
    [Tooltip("Altezza di un cristallino (unità).")]
    [SerializeField] private float crystalSize = 0.22f;

    private Transform holder;
    private Vector3 holderBasePosition;
    private Vector3 holderBaseScale;
    private float noiseSeed;
    private float nextGlitchTime;
    private float glitchEndTime;
    private Vector3 glitchDirection;

    private Renderer[] modelRenderers; // quelli del modello vero, senza copie e cristallini
    private readonly List<Transform> ghosts = new List<Transform>();
    private readonly List<Material> ghostMaterials = new List<Material>();

    private struct Crystal
    {
        public Transform Transform;
        public Material Material;
        public float Angle;
        public float Height;
        public float SpinSpeed;
        public float PulsePhase;
    }

    private readonly List<Crystal> crystals = new List<Crystal>();
    private Transform crystalRoot;

    void Start()
    {
        holder = AmmoModel.FindHolder(transform);
        AmmoModel.Center(holder);
        holderBasePosition = holder.localPosition;
        holderBaseScale = holder.localScale;
        noiseSeed = Random.value * 100f;
        nextGlitchTime = Time.time + Random.Range(glitchInterval.x, glitchInterval.y);

        modelRenderers = holder.GetComponentsInChildren<Renderer>(); // prima di creare le copie
        CreateGhosts();
        CreateCrystals();
    }

    void Update()
    {
        UpdateHologram();
        UpdateCrystals();
    }

    private void UpdateHologram()
    {
        float t = Time.time * jitterFrequency;
        Vector3 jitter = new Vector3(
            Mathf.PerlinNoise(noiseSeed, t) - 0.5f,
            Mathf.PerlinNoise(noiseSeed + 20f, t) - 0.5f,
            Mathf.PerlinNoise(noiseSeed + 40f, t) - 0.5f) * (2f * jitterAmount);

        if (Time.time >= nextGlitchTime)
        {
            glitchEndTime = Time.time + glitchDuration;
            nextGlitchTime = Time.time + Random.Range(glitchInterval.x, glitchInterval.y);
            glitchDirection = Random.value < 0.5f ? Vector3.right : Vector3.left;
        }

        bool glitching = Time.time < glitchEndTime;
        Vector3 offset = jitter;
        Vector3 scale = holderBaseScale;
        if (glitching)
        {
            // Il tremolio è in coordinate locali della radice, che gira: lo scatto va di lato
            // rispetto a come si vede in quel momento, quindi va bene così.
            offset += glitchDirection * glitchOffset;
            scale = Vector3.Scale(holderBaseScale, new Vector3(1.08f, 0.9f, 1.08f));
        }

        holder.localPosition = holderBasePosition + offset;
        holder.localScale = scale;

        // Sfarfallio: per un fotogramma il modello vero sparisce e restano solo le copie azzurre.
        bool modelVisible = Random.value >= flickerChance;
        foreach (Renderer modelRenderer in modelRenderers)
        {
            modelRenderer.enabled = modelVisible;
        }

        float toLocal = 1f / Mathf.Max(holderBaseScale.x, 0.01f);
        for (int i = 0; i < ghosts.Count; i++)
        {
            // Le copie sono figlie di holder: per sfasarle rispetto al modello basta uno
            // spostamento locale, una per parte. Fuori dal glitch ondeggiano poco attorno al
            // modello (come i colori sfasati di uno schermo rovinato), nel glitch si staccano.
            float side = i % 2 == 0 ? -1f : 1f;
            Vector3 drift = new Vector3(Mathf.Sin(Time.time * 7f + i * 2f), Mathf.Sin(Time.time * 5.3f + i), 0f) * ghostDrift;
            Vector3 ghostOffset = glitching ? glitchDirection * (side * glitchOffset * 1.6f) : drift * side;
            ghosts[i].localPosition = ghostOffset * toLocal;
            float strength = glitching ? 0.7f + 0.3f * Random.value : 0.18f + 0.1f * Random.value;
            ghostMaterials[i].color = ghostColor * (modelVisible ? strength : 1f);
        }
    }

    // Due copie di tutte le mesh del modello, additive e azzurre, accese solo durante i glitch.
    private void CreateGhosts()
    {
        MeshFilter[] filters = holder.GetComponentsInChildren<MeshFilter>();
        for (int g = 0; g < 2; g++)
        {
            GameObject ghostRoot = new GameObject("HologramGhost" + g);
            ghostRoot.transform.SetParent(holder, false);
            Material material = AmmoModel.CreateAdditiveMaterial(ghostColor * 0.3f);

            foreach (MeshFilter filter in filters)
            {
                GameObject copy = new GameObject(filter.name, typeof(MeshFilter), typeof(MeshRenderer));
                copy.transform.SetParent(ghostRoot.transform, false);
                // Stessa posizione del pezzo originale rispetto a holder.
                copy.transform.localPosition = holder.InverseTransformPoint(filter.transform.position);
                copy.transform.localRotation = Quaternion.Inverse(holder.rotation) * filter.transform.rotation;
                copy.transform.localScale = Vector3.Scale(filter.transform.lossyScale, Inverse(holder.lossyScale));
                copy.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                MeshRenderer renderer = copy.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            ghosts.Add(ghostRoot.transform);
            ghostMaterials.Add(material);
        }
    }

    private static Vector3 Inverse(Vector3 v)
    {
        return new Vector3(1f / Mathf.Max(v.x, 0.0001f), 1f / Mathf.Max(v.y, 0.0001f), 1f / Mathf.Max(v.z, 0.0001f));
    }

    // I cristallini stanno sotto un oggetto proprio, figlio della radice ma che non gira con lei
    // (vedi UpdateCrystals): girano attorno al modello alla loro velocità.
    private void CreateCrystals()
    {
        crystalRoot = new GameObject("Cristallini").transform;
        crystalRoot.SetParent(transform, false);
        Mesh mesh = BuildCrystalMesh();

        for (int i = 0; i < crystalCount; i++)
        {
            GameObject crystalObject = new GameObject("Cristallino" + i, typeof(MeshFilter), typeof(MeshRenderer));
            crystalObject.transform.SetParent(crystalRoot, false);
            crystalObject.transform.localScale = Vector3.one * crystalSize * Random.Range(0.7f, 1.2f);
            crystalObject.GetComponent<MeshFilter>().sharedMesh = mesh;

            Material material = AmmoModel.CreateAdditiveMaterial(crystalColor);
            MeshRenderer renderer = crystalObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            crystals.Add(new Crystal
            {
                Transform = crystalObject.transform,
                Material = material,
                Angle = (float)i / Mathf.Max(crystalCount, 1) * 360f,
                Height = Random.Range(-0.25f, 0.25f),
                SpinSpeed = Random.Range(90f, 200f) * (Random.value < 0.5f ? -1f : 1f),
                PulsePhase = Random.value * Mathf.PI * 2f,
            });
        }
    }

    private void UpdateCrystals()
    {
        if (crystalRoot == null)
        {
            return;
        }

        // Annulla la rotazione che CollectibleShine dà alla radice: i cristallini girano per conto loro.
        crystalRoot.rotation = Quaternion.identity;
        crystalRoot.position = holder.position;

        for (int i = 0; i < crystals.Count; i++)
        {
            Crystal crystal = crystals[i];
            float angle = (crystal.Angle + Time.time * orbitSpeed * 360f) * Mathf.Deg2Rad;
            float height = crystal.Height + Mathf.Sin(Time.time * 1.7f + crystal.PulsePhase) * 0.06f;
            crystal.Transform.localPosition = new Vector3(Mathf.Cos(angle) * orbitRadius, height, Mathf.Sin(angle) * orbitRadius);
            crystal.Transform.localRotation = Quaternion.Euler(15f, Time.time * crystal.SpinSpeed, 10f);

            float pulse = 0.8f + 0.35f * Mathf.Sin(Time.time * 4f + crystal.PulsePhase);
            crystal.Material.color = crystalColor * pulse;
        }
    }

    // Ottaedro allungato in verticale (alto 1, largo 0.45): la forma classica del cristallo.
    private static Mesh BuildCrystalMesh()
    {
        Vector3 top = new Vector3(0f, 0.5f, 0f), bottom = new Vector3(0f, -0.5f, 0f);
        const float w = 0.225f;
        Vector3[] ring = { new Vector3(w, 0f, 0f), new Vector3(0f, 0f, w), new Vector3(-w, 0f, 0f), new Vector3(0f, 0f, -w) };

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = ring[i], b = ring[(i + 1) % 4];
            AddTriangle(vertices, triangles, top, b, a);
            AddTriangle(vertices, triangles, bottom, a, b);
        }

        Mesh mesh = new Mesh { name = "Cristallino" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Vertici separati per ogni faccia: sfaccettature nette invece che smussate.
    private static void AddTriangle(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c)
    {
        triangles.Add(vertices.Count); vertices.Add(a);
        triangles.Add(vertices.Count); vertices.Add(b);
        triangles.Add(vertices.Count); vertices.Add(c);
    }

    void OnDestroy()
    {
        foreach (Material material in ghostMaterials)
        {
            Destroy(material);
        }
        foreach (Crystal crystal in crystals)
        {
            Destroy(crystal.Material);
        }
    }
}
