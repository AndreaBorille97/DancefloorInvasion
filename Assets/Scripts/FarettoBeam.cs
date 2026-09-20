using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Da mettere sul GameObject del modello del sound system: monta su alcuni faretti il classico
// fascio da festa, cioè due cose insieme - una spot light vera, che illumina le superfici, e il
// cono visibile in aria, che la luce da sola non dà.
//
// Il fascio segue i movimenti senza niente da aggiornare a ogni frame: l'oggetto luce è figlio
// del Faretto, quindi eredita l'animazione che PlayAllAnimations fa girare sul faretto stesso.
//
// Da dove esce: la lente del faretto è l'unica faccia con materiale "Giallo_Faretto_Centro",
// sta a (0, -0.395, -0.400) nello spazio locale della mesh e la sua normale guarda dritta in
// -Y. Sono i default qui sotto, esposti perché su un altro modello di faretto cambierebbero.
//
// Perché il cono è una mesh e non basta la Light: URP non ha luci volumetriche, quindi una spot
// illumina ciò che colpisce ma in aria è invisibile. Il cono è una mesh a parte con materiale
// Unlit additivo e una texture a gradiente lungo l'asse, così sfuma allontanandosi dalla lente
// invece di tagliarsi di netto. Essendo depth-testato, il pavimento e le geometrie davanti lo
// occludono come si aspetta l'occhio.
public class FarettoBeam : MonoBehaviour
{
    [Header("Quali faretti")]
    [Tooltip("Prefisso del nome delle mesh dei faretti ('Faretto_01', ...).")]
    public string farettoNamePrefix = "Faretto";

    [Tooltip("Su quanti faretti montare il fascio, presi in ordine di nome. Il modello ne ha 4: " +
             "alza a 4 se li vuoi tutti accesi.")]
    [Range(1, 8)]
    public int quantiFaretti = 2;

    [Header("Lente (spazio locale della mesh del faretto)")]
    [Tooltip("Punto da cui parte il fascio: il centro della lente gialla del modello.")]
    public Vector3 lensLocalPosition = new Vector3(0f, -0.395f, -0.4f);

    [Tooltip("Direzione in cui guarda la lente, nel locale della mesh: il faretto punta in giù " +
             "e a orientarlo davvero ci pensa l'animazione.")]
    public Vector3 lensLocalDirection = Vector3.down;

    [Header("Luce")]
    [Tooltip("Colore del fascio: azzurro tendente al bianco, come i faretti da festa.")]
    public Color beamColor = new Color(0.6f, 0.85f, 1f);

    [Tooltip("Intensità della spot light che illumina davvero le superfici.")]
    public float lightIntensity = 4f;

    [Tooltip("Lunghezza del fascio in unità di scena: deve bastare ad arrivare dal traliccio " +
             "fino a terra. Vale sia per la luce sia per il cono visibile.")]
    public float range = 18f;

    [Tooltip("Apertura del cono in gradi: stretto (10-25) per il fascio da discoteca.")]
    [Range(4f, 90f)]
    public float coneAngle = 18f;

    [Tooltip("Ombre proiettate dai fasci: belle da vedere ma costose, una shadow map per faretto.")]
    public bool castShadows = false;

    [Header("Cono visibile")]
    [Tooltip("Il cono di luce visibile in aria. Spegnilo se vuoi solo l'illuminazione.")]
    public bool showBeam = true;

    [Tooltip("Quanto è denso il cono in aria: è additivo, quindi valori alti slavano ciò che " +
             "ha dietro. Sopra 1 va in HDR e viene preso dal bloom.")]
    [Range(0f, 3f)]
    public float beamBrightness = 0.5f;

    [Tooltip("Lati del cono: più alto = bordo più tondo, ma è una mesh in più da disegnare.")]
    [Range(8, 48)]
    public int beamSegments = 24;

    private void Awake()
    {
        var faretti = new List<Transform>();
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.gameObject.name.StartsWith(farettoNamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                faretti.Add(renderer.transform);
            }
        }

        if (faretti.Count == 0)
        {
            Debug.LogWarning($"[{nameof(FarettoBeam)}] {name}: nessuna mesh che inizi per " +
                             $"'{farettoNamePrefix}' fra i figli, nessun fascio creato.", this);
            return;
        }

        // Ordine per nome: così "quanti faretti" prende sempre gli stessi (_01, _02, ...) e non
        // dipende dall'ordine in cui capitano nella gerarchia.
        faretti.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        // Mesh e materiale sono identici per tutti i fasci: uno solo, condiviso.
        Mesh beamMesh = null;
        Material beamMaterial = null;
        if (showBeam)
        {
            float radius = range * Mathf.Tan(Mathf.Max(coneAngle, 0.01f) * 0.5f * Mathf.Deg2Rad);
            beamMesh = BuildConeMesh(range, radius, beamSegments);
            beamMaterial = CreateBeamMaterial();
        }

        int quanti = Mathf.Clamp(quantiFaretti, 1, faretti.Count);
        for (int i = 0; i < quanti; i++)
        {
            Mount(faretti[i], beamMesh, beamMaterial);
        }
    }

    private void Mount(Transform faretto, Mesh beamMesh, Material beamMaterial)
    {
        Vector3 forward = lensLocalDirection.sqrMagnitude > 0f ? lensLocalDirection.normalized : Vector3.down;
        // Con forward verticale l'up di default sarebbe parallelo e LookRotation degenererebbe.
        Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;

        var go = new GameObject("FarettoBeam");
        go.transform.SetParent(faretto, false);
        go.transform.localPosition = lensLocalPosition;
        go.transform.localRotation = Quaternion.LookRotation(forward, up);

        Light light = go.AddComponent<Light>();
        light.type = LightType.Spot;
        light.color = beamColor;
        light.intensity = lightIntensity;
        light.range = range;
        light.spotAngle = coneAngle;
        light.innerSpotAngle = coneAngle * 0.6f;
        light.shadows = castShadows ? LightShadows.Soft : LightShadows.None;

        if (beamMesh == null || beamMaterial == null)
        {
            return;
        }

        var cone = new GameObject("FarettoCono");
        cone.transform.SetParent(go.transform, false);

        // Il cono è costruito in unità di scena, come Light.range che non risente della scala
        // del transform. Il modello però è scalato, quindi la scala ereditata va annullata o il
        // cono visibile risulterebbe lungo il doppio di quanto illumina la luce.
        Vector3 inherited = cone.transform.lossyScale;
        cone.transform.localScale = new Vector3(
            Mathf.Approximately(inherited.x, 0f) ? 1f : 1f / inherited.x,
            Mathf.Approximately(inherited.y, 0f) ? 1f : 1f / inherited.y,
            Mathf.Approximately(inherited.z, 0f) ? 1f : 1f / inherited.z);

        cone.AddComponent<MeshFilter>().sharedMesh = beamMesh;
        MeshRenderer meshRenderer = cone.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = beamMaterial;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    // Cono aperto con l'apice sulla lente e la bocca a distanza 'length' lungo +Z (che dopo la
    // LookRotation è la direzione del fascio). UV: v=0 all'apice, v=1 in fondo, per il gradiente.
    private static Mesh BuildConeMesh(float length, float radius, int segments)
    {
        int rings = segments + 1;
        var vertices = new Vector3[rings * 2];
        var uv = new Vector2[rings * 2];

        for (int i = 0; i < rings; i++)
        {
            float t = (float)i / segments;
            float angle = t * Mathf.PI * 2f;
            vertices[i * 2] = Vector3.zero;
            vertices[i * 2 + 1] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, length);
            uv[i * 2] = new Vector2(t, 0f);
            uv[i * 2 + 1] = new Vector2(t, 1f);
        }

        var triangles = new int[segments * 3];
        for (int i = 0; i < segments; i++)
        {
            triangles[i * 3] = i * 2;
            triangles[i * 3 + 1] = i * 2 + 1;
            triangles[i * 3 + 2] = i * 2 + 3;
        }

        var mesh = new Mesh { name = "FarettoConoMesh", vertices = vertices, uv = uv, triangles = triangles };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private Material CreateBeamMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            Debug.LogError($"[{nameof(FarettoBeam)}]: shader 'Universal Render Pipeline/Unlit' " +
                           "non trovato, niente cono visibile.", this);
            return null;
        }

        var material = new Material(shader)
        {
            name = "FarettoConoMat",
            mainTexture = BuildBeamGradient(),
            color = beamColor * beamBrightness
        };

        // Additivo: il fascio si somma a quello che ha dietro invece di coprirlo, che è come si
        // comporta la luce in aria. Niente scrittura sul depth (i coni non si nascondono a
        // vicenda) e niente culling (il cono si attraversa e si guarda anche da dentro).
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 2f);
        material.SetFloat("_SrcBlend", (float)BlendMode.One);
        material.SetFloat("_DstBlend", (float)BlendMode.One);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_Cull", (float)CullMode.Off);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
        return material;
    }

    // Gradiente lungo il fascio: pieno sulla lente, spento in fondo. In additivo conta l'RGB
    // (il nero non aggiunge niente), l'alpha lo tengo allineato per chi volesse passare a un
    // blending tradizionale.
    private static Texture2D BuildBeamGradient()
    {
        const int height = 64;
        var texture = new Texture2D(1, height, TextureFormat.RGBA32, false)
        {
            name = "FarettoConoGradiente",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        for (int y = 0; y < height; y++)
        {
            float t = (float)y / (height - 1);
            float value = Mathf.Pow(1f - t, 2f);   // quadratico: si spegne presto, come il fascio vero
            texture.SetPixel(0, y, new Color(value, value, value, value));
        }

        texture.Apply();
        return texture;
    }
}
