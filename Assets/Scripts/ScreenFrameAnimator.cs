using System.Collections.Generic;
using UnityEngine;

// Da mettere sul GameObject del modello del sound system (o su un suo genitore): anima gli
// schermi con la sequenza di frame in Assets/texture/acid lines, un frame ad ogni battito.
//
// Gli schermi del modello NON vengono texturizzati direttamente: la loro mesh non ha UV
// utilizzabili (nata come superficie nera piatta), quindi qualsiasi texture applicata al
// materiale "Schermo_Nero" viene campionata in un punto solo e resta un colore piatto.
// Al suo posto, davanti a ogni submesh che usa quel materiale viene generato un quad
// (posizione/orientamento/dimensioni ricavati dalla geometria stessa) con un materiale
// Unlit: le UV del quad sono nostre e corrette, e lo schermo nero originale resta dietro
// a fare da sfondo.
public class ScreenFrameAnimator : MonoBehaviour
{
    [Header("Frame")]
    [Tooltip("Trascina qui tutti i frame di Assets/texture/acid lines, in ordine (acid_0001, acid_0002, ...).")]
    [SerializeField] private Texture2D[] frames;

    [Header("Schermi")]
    [Tooltip("Se vuoto vengono cercati automaticamente tutti i Renderer nei figli.")]
    [SerializeField] private Renderer[] screenRenderers;
    [Tooltip("Nome (anche parziale) del materiale che identifica la superficie schermo.")]
    [SerializeField] private string screenMaterialName = "Schermo_Nero";

    [Header("Ritmo")]
    [Tooltip("Battiti al minuto del pezzo, come SoundSystem.bpm.")]
    [SerializeField] private float bpm = 175f;
    [Tooltip("Quanti battiti dura un giro completo della sequenza: 4 = una battuta (più basso = più veloce).")]
    [SerializeField] private float beatsPerLoop = 4f;

    [Header("Resa")]
    [Tooltip("Moltiplicatore del colore: sopra 1 lo schermo va in HDR e accende il bloom.")]
    [SerializeField] private float brightness = 3f;

    // Si tengono i Renderer dei quad, non i Material: il frame va scritto ogni volta passando
    // per renderer.material. Un riferimento al materiale creato qui si stacca infatti al primo
    // altro script che chiama .material sullo stesso renderer - Unity ne mette una COPIA sul
    // renderer e la nostra resta orfana, cioè lo schermo si pianta sull'ultimo frame applicato.
    // Succede davvero: SoundSystem, di cui il modello è figlio, istanzia così i materiali di
    // tutti i Renderer nei figli per il lampo di danno, e l'ordine degli Awake non è garantito.
    private Renderer[] screens;
    private int frameIndex;
    private float timer;

    void Awake()
    {
        if (screenRenderers == null || screenRenderers.Length == 0)
        {
            screenRenderers = GetComponentsInChildren<Renderer>(true);
        }

        if (frames == null || frames.Length == 0)
        {
            Debug.LogError($"{nameof(ScreenFrameAnimator)}: nessun frame assegnato in 'Frames'.", this);
        }

        var built = new List<Renderer>();
        foreach (Renderer screenRenderer in screenRenderers)
        {
            MeshFilter filter = screenRenderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                continue;
            }

            Material[] shared = screenRenderer.sharedMaterials;
            int subMeshCount = filter.sharedMesh.subMeshCount;
            for (int slot = 0; slot < shared.Length && slot < subMeshCount; slot++)
            {
                if (shared[slot] != null && shared[slot].name.Contains(screenMaterialName))
                {
                    Renderer overlay = CreateOverlay(screenRenderer.transform, filter.sharedMesh, slot);
                    if (overlay != null)
                    {
                        built.Add(overlay);
                    }
                }
            }
        }
        screens = built.ToArray();

        if (screens.Length == 0)
        {
            Debug.LogError($"{nameof(ScreenFrameAnimator)}: nessuna superficie con materiale '{screenMaterialName}' trovata nei Renderer assegnati/trovati.", this);
        }

        ApplyFrame();
    }

    // Costruisce il quad davanti al submesh indicato e ne restituisce il Renderer.
    private Renderer CreateOverlay(Transform parent, Mesh mesh, int subMesh)
    {
        int[] triangles = mesh.GetTriangles(subMesh);
        if (triangles.Length == 0)
        {
            return null;
        }

        Vector3[] vertices = mesh.vertices;

        // Normale del triangolo più grande, non la somma di tutte: se il pannello è modellato
        // a doppia faccia le normali opposte si annullano a vicenda, e con la mesh ~100 volte
        // più piccola delle unità di scena nessuna soglia assoluta sull'area è affidabile.
        Vector3 normal = Vector3.zero;
        float largestArea = 0f;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = vertices[triangles[i]];
            Vector3 b = vertices[triangles[i + 1]];
            Vector3 c = vertices[triangles[i + 2]];
            Vector3 cross = Vector3.Cross(b - a, c - a);
            float area = cross.sqrMagnitude;
            if (area > largestArea)
            {
                largestArea = area;
                normal = cross;
            }
        }
        if (largestArea <= 0f)
        {
            Debug.LogWarning($"{nameof(ScreenFrameAnimator)}: submesh {subMesh} di '{parent.name}' degenere, schermo saltato.", this);
            return null;
        }
        normal.Normalize();

        Vector3 centroid = Vector3.zero;
        foreach (int index in triangles)
        {
            centroid += vertices[index];
        }
        centroid /= triangles.Length;

        // Lo schermo guarda verso l'esterno della struttura: se la normale scelta punta
        // all'interno va girata, altrimenti il quad finirebbe dietro al pannello.
        if (Vector3.Dot(normal, centroid - mesh.bounds.center) < 0f)
        {
            normal = -normal;
        }

        Vector3 upHint = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
        Quaternion rotation = Quaternion.LookRotation(normal, upHint);
        Vector3 right = rotation * Vector3.right;
        Vector3 up = rotation * Vector3.up;

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        float maxDepth = float.MinValue;
        foreach (int index in triangles)
        {
            Vector3 v = vertices[index];
            float x = Vector3.Dot(v, right);
            float y = Vector3.Dot(v, up);
            float depth = Vector3.Dot(v, normal);
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
            if (depth > maxDepth) maxDepth = depth;
        }

        float width = maxX - minX;
        float height = maxY - minY;
        if (width <= 0f || height <= 0f)
        {
            return null;
        }

        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            Debug.LogError($"{nameof(ScreenFrameAnimator)}: shader 'Universal Render Pipeline/Unlit' non trovato.", this);
            return null;
        }

        var material = new Material(shader);
        material.color = Color.white * brightness;
        if (material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        }

        var screen = new GameObject($"AcidScreen_{parent.name}");
        screen.transform.SetParent(parent, false);
        // Staccato dalla superficie di una frazione della sua dimensione, per non compenetrare
        // lo schermo nero che resta dietro.
        screen.transform.localPosition = right * ((minX + maxX) * 0.5f)
                                       + up * ((minY + maxY) * 0.5f)
                                       + normal * (maxDepth + width * 0.005f);
        screen.transform.localRotation = rotation;
        screen.transform.localScale = Vector3.one;

        screen.AddComponent<MeshFilter>().sharedMesh = BuildQuad(width, height);
        MeshRenderer renderer = screen.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        Debug.Log($"{nameof(ScreenFrameAnimator)}: schermo su '{parent.name}' (submesh {subMesh}), dimensioni locali {width:0.###} x {height:0.###}.", screen);
        return renderer;
    }

    // Quad costruito a mano invece della primitiva di Unity: winding, normale e UV certi.
    private static Mesh BuildQuad(float width, float height)
    {
        float halfWidth = width * 0.5f;
        float halfHeight = height * 0.5f;
        var quad = new Mesh
        {
            name = "AcidScreenQuad",
            vertices = new[]
            {
                new Vector3(-halfWidth, -halfHeight, 0f),
                new Vector3(halfWidth, -halfHeight, 0f),
                new Vector3(halfWidth, halfHeight, 0f),
                new Vector3(-halfWidth, halfHeight, 0f)
            },
            uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            },
            triangles = new[] { 0, 1, 2, 0, 2, 3 }
        };
        quad.RecalculateNormals();
        quad.RecalculateBounds();
        return quad;
    }

    void Update()
    {
        if (frames == null || frames.Length == 0 || screens.Length == 0)
        {
            return;
        }

        timer += Time.deltaTime;

        // evita una divisione per zero (o un ciclo infinito) se bpm/beatsPerLoop vengono lasciati a 0
        float loopDuration = 60f / Mathf.Max(bpm, 0.01f) * Mathf.Max(beatsPerLoop, 0.01f);
        // l'indice si ricava dal tempo trascorso invece di avanzare di uno per volta: così
        // resta agganciato al ritmo anche se un giro dura meno di un frame di gioco
        float progress = Mathf.Repeat(timer, loopDuration) / loopDuration;
        int next = Mathf.Min((int)(progress * frames.Length), frames.Length - 1);
        if (next != frameIndex)
        {
            frameIndex = next;
            ApplyFrame();
        }
    }

    private void ApplyFrame()
    {
        if (frames == null || frames.Length == 0)
        {
            return;
        }

        Texture2D frame = frames[frameIndex];
        if (frame == null)
        {
            return; // slot vuoto in 'Frames': meglio tenere l'ultimo frame buono che spegnere lo schermo
        }

        foreach (Renderer screen in screens)
        {
            // .material e non .sharedMaterial: se qualcun altro ha nel frattempo istanziato il
            // materiale del quad, questa è la copia che il renderer sta davvero usando.
            screen.material.mainTexture = frame;
        }
    }
}
