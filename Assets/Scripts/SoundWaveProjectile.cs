using System.Collections.Generic;
using UnityEngine;

// Da mettere su un GameObject vuoto usato come prefab (vedi PlayerShooting.projectilePrefab).
// L'onda sonora non si muove: si espande come un cono (tipo shotgun) davanti al punto in cui
// è stata creata, nella direzione transform.forward, colpendo i nemici che entrano nell'area
// finché non arriva al raggio massimo.
public class SoundWaveProjectile : MonoBehaviour
{
    [Header("Espansione")]
    [SerializeField] private float maxRadius = 15f; // raggio massimo raggiunto dall'onda
    [SerializeField] private float expandSpeed = 20f; // velocità di espansione (unità al secondo)

    [Header("Forma del cono")]
    [SerializeField] private float coneAngle = 60f; // apertura totale del cono, in gradi, centrata su transform.forward
    [SerializeField] private int wedgeSegments = 16; // quanti triangoli usare per approssimare l'arco (più alto = più arrotondato)

    [Header("Danno")]
    [SerializeField] private float damage = 12f;
    [SerializeField] private LayerMask targetMask; // layer dei nemici colpibili (da impostare in Inspector)

    [Header("Cura Raver")]
    [Tooltip("Quanta vita restituisce ad ogni Raver colpito dall'onda (una sola volta per onda, come il danno ai nemici). Bastano 10 onde per rianimare del tutto un Raver a terra (50 vita): il tempo reale dipende anche da quanto l'onda impiega ad espandersi fino a lui (vedi expandSpeed), non solo dalla cadenza di fuoco.")]
    [SerializeField] private float healAmount = 5f;

    [Header("Visuale")]
    [SerializeField] private Color waveColor = new Color(0.4f, 0.9f, 1f, 0.35f); // colore e trasparenza dell'area visibile
    [SerializeField] private float visualYOffset = 0.05f; // quanto sollevare il disco da terra, per evitare z-fighting col pavimento

    [Header("Visuale 3D opzionale (mesh + materiale nuovi)")]
    [Tooltip("Se assegnato (es. il modello shockwave_mat.glb, che porta con sé sia la mesh sia i materiali), viene creato come figlio e scalato in base al raggio dell'onda. Di default SOSTITUISCE lo spicchio piatto (vedi alsoShowFlatWave). Se lasciato vuoto resta solo lo spicchio.")]
    [SerializeField] private GameObject customVisualPrefab;
    [Tooltip("Se attivo, oltre al modello 3D viene disegnato anche il vecchio spicchio piatto a terra (le 'onde concentriche'). Di default spento: con il modello 3D assegnato si vede solo il cono.")]
    [SerializeField] private bool alsoShowFlatWave = false;
    [Tooltip("Correzione di orientamento del modello rispetto alla direzione dell'onda (che punta lungo transform.forward, +Z locale). Es. X -90 se il modello nasce sdraiato.")]
    [SerializeField] private Vector3 customVisualEulerAngles = Vector3.zero;
    [Tooltip("La mesh viene scalata a currentRadius * questo valore. Alza o abbassa se il modello è più grande/piccolo di 1 unità di raggio.")]
    [SerializeField] private float customVisualScaleMultiplier = 1f;
    [Tooltip("Scala minima del modello appena creato, come frazione di quella massima (0 = parte da un punto, 0.25 = parte già a un quarto). Evita che il cono sia invisibile nei primi istanti.")]
    [Range(0f, 1f)]
    [SerializeField] private float customVisualMinScaleFraction = 0.15f;
    [Tooltip("Quanto il modello avanza lungo la direzione di tiro mentre l'onda si espande, come frazione del raggio corrente. 0 = resta centrato sul player (metà cono dietro di lui). ~0.5 = segue il fronte d'onda e copre il cono che fa danno. 1 = sta sul bordo esterno.")]
    [Range(0f, 1f)]
    [SerializeField] private float customVisualForwardFollow = 0.5f;
    [Tooltip("Spinta in avanti fissa (in unità locali) aggiunta sempre, indipendente dall'espansione. Utile per staccare il modello dal player fin dal primo frame.")]
    [SerializeField] private float customVisualForwardOffset = 0f;
    [Tooltip("Se attivo, l'opacità del modello 3D cala man mano che l'onda raggiunge il raggio massimo.")]
    [SerializeField] private bool customVisualFadeOut = true;

    private float currentRadius;
    private readonly HashSet<Collider> alreadyHit = new HashSet<Collider>();
    private readonly HashSet<Collider> alreadyHealed = new HashSet<Collider>();

    private Transform visual;
    private MeshFilter visualMeshFilter;
    private MeshRenderer visualRenderer;
    private Material visualMaterial;

    // Istanze del modello 3D opzionale (customVisualPrefab): materiale + colore base originale
    // + id della property di colore da usare per il fade (-1 se il materiale non ne ha una nota).
    private Transform customVisual;
    private Vector3 customVisualBaseScale = Vector3.one; // scala originale del prefab (preserva segno/handedness e unità di misura del modello)
    private Material[] customVisualMaterials;
    private Color[] customVisualBaseColors;
    private int[] customVisualColorPropIds;

    // Nomi di property di colore provati per il fade, in ordine: URP/Lit, Standard, particellari,
    // glTFast. Il primo che il materiale possiede viene usato.
    private static readonly string[] ColorPropertyNames = { "_BaseColor", "_Color", "_TintColor", "baseColorFactor" };

    void Awake()
    {
        // Lo spicchio piatto si crea solo se non c'è un modello 3D che lo sostituisce
        // (o se alsoShowFlatWave chiede di tenerli entrambi).
        if (customVisualPrefab == null || alsoShowFlatWave)
        {
            CreateVisual();
        }

        CreateCustomVisual();
    }

    // Chiamato da PlayerShooting subito dopo l'Instantiate, prima che Update parta:
    // applica i bonus attivi su PlayerSoundWavePowerUps (area = coneAngle, deep = maxRadius)
    // e ricostruisce la mesh dello spicchio, già creata in Awake con coneAngle di base.
    public void ApplyPowerUps(float bonusConeAngle, float bonusMaxRadius)
    {
        if (bonusConeAngle <= 0f && bonusMaxRadius <= 0f)
        {
            return;
        }

        coneAngle += bonusConeAngle;
        maxRadius += bonusMaxRadius;

        if (visualMeshFilter != null)
        {
            visualMeshFilter.mesh = BuildWedgeMesh(coneAngle, wedgeSegments);
        }
    }

    void Update()
    {
        currentRadius += expandSpeed * Time.deltaTime;

        DamageEnemiesInRange();
        HealRaversInRange();
        UpdateVisual();
        UpdateCustomVisual();

        if (currentRadius >= maxRadius)
        {
            Destroy(gameObject);
        }
    }

    private void DamageEnemiesInRange()
    {
        // Il raggio di ricerca resta una sfera (Physics non ha un OverlapCone),
        // il filtro angolare sotto esclude chi è dentro il raggio ma fuori dal cono.
        Collider[] hits = Physics.OverlapSphere(transform.position, currentRadius, targetMask);
        foreach (var hit in hits)
        {
            if (alreadyHit.Contains(hit))
            {
                continue;
            }

            Vector3 toTarget = hit.transform.position - transform.position;
            toTarget.y = 0f; // confronto sul piano orizzontale, come EnemyChase

            if (Vector3.Angle(transform.forward, toTarget) > coneAngle * 0.5f)
            {
                continue; // dentro al raggio ma fuori dall'apertura del cono
            }

            alreadyHit.Add(hit);
            hit.GetComponent<EnemyHealth>()?.TakeDamage(damage, transform.position);
        }
    }

    private void HealRaversInRange()
    {
        // Nessun LayerMask qui: il Raver non è detto sia sul layer nemici (targetMask),
        // quindi cerchiamo direttamente chi ha un RaverHealth, come fa CamperProjectile con EnemyHealth.
        Collider[] hits = Physics.OverlapSphere(transform.position, currentRadius);
        foreach (var hit in hits)
        {
            if (alreadyHealed.Contains(hit))
            {
                continue;
            }

            RaverHealth raverHealth = hit.GetComponent<RaverHealth>();
            if (raverHealth == null)
            {
                continue;
            }

            Vector3 toTarget = hit.transform.position - transform.position;
            toTarget.y = 0f;

            if (Vector3.Angle(transform.forward, toTarget) > coneAngle * 0.5f)
            {
                continue; // dentro al raggio ma fuori dall'apertura del cono
            }

            alreadyHealed.Add(hit);
            raverHealth.Heal(healAmount);
        }
    }

    // Crea come figlio uno spicchio piatto e semitrasparente che mostra a schermo (non solo
    // nella Scene view) l'area colpita dall'onda mentre si espande.
    private void CreateVisual()
    {
        GameObject visualObject = new GameObject("WaveVisual");
        visualObject.transform.SetParent(transform, false);
        visualObject.transform.localPosition = Vector3.up * visualYOffset;

        visualMeshFilter = visualObject.AddComponent<MeshFilter>();
        visualMeshFilter.mesh = BuildWedgeMesh(coneAngle, wedgeSegments);

        visualRenderer = visualObject.AddComponent<MeshRenderer>();
        visualRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        visualRenderer.receiveShadows = false;

        // Sprites/Default supporta la trasparenza via material.color ed è a due facce,
        // senza bisogno di configurare manualmente un materiale Standard Transparent.
        visualMaterial = new Material(Shader.Find("Sprites/Default"));
        visualRenderer.material = visualMaterial;

        visual = visualObject.transform;
        UpdateVisual();
    }

    // Istanzia come figlio il modello 3D opzionale (customVisualPrefab, tipicamente
    // shockwave_mat.glb che contiene già mesh + materiali). Non viene parentato con
    // worldPositionStays=true come lo spicchio: parte a scala 0 dall'origine dell'onda
    // e viene ingrandito in UpdateCustomVisual insieme a currentRadius, come lo spicchio.
    private void CreateCustomVisual()
    {
        if (customVisualPrefab == null)
        {
            return;
        }

        GameObject visualObject = Instantiate(customVisualPrefab, transform);
        visualObject.name = "CustomWaveVisual";
        customVisualBaseScale = visualObject.transform.localScale;
        visualObject.transform.localRotation = Quaternion.Euler(customVisualEulerAngles);

        // Il modello è solo decorativo: il danno resta gestito da OverlapSphere + cono.
        foreach (Collider col in visualObject.GetComponentsInChildren<Collider>())
        {
            col.enabled = false;
        }

        Renderer[] renderers = visualObject.GetComponentsInChildren<Renderer>();
        var mats = new List<Material>();
        foreach (Renderer r in renderers)
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            mats.AddRange(r.materials); // .materials crea istanze: non tocca gli asset condivisi
        }

        customVisualMaterials = mats.ToArray();
        customVisualBaseColors = new Color[customVisualMaterials.Length];
        customVisualColorPropIds = new int[customVisualMaterials.Length];
        for (int i = 0; i < customVisualMaterials.Length; i++)
        {
            customVisualColorPropIds[i] = -1;
            foreach (string propName in ColorPropertyNames)
            {
                if (customVisualMaterials[i].HasProperty(propName))
                {
                    int id = Shader.PropertyToID(propName);
                    customVisualColorPropIds[i] = id;
                    customVisualBaseColors[i] = customVisualMaterials[i].GetColor(id);
                    break;
                }
            }
        }

        customVisual = visualObject.transform;
        UpdateCustomVisual();
    }

    // Genera uno spicchio (fan di triangoli) di raggio unitario sul piano XZ, centrato
    // sull'asse Z locale: verrà poi semplicemente scalato in base a currentRadius.
    private Mesh BuildWedgeMesh(float angleDegrees, int segments)
    {
        Mesh mesh = new Mesh { name = "SoundWaveWedge" };

        Vector3[] vertices = new Vector3[segments + 2];
        int[] triangles = new int[segments * 3];

        vertices[0] = Vector3.zero; // centro: il punto da cui parte l'onda

        float halfAngle = angleDegrees * 0.5f;
        float angleStep = angleDegrees / segments;

        for (int i = 0; i <= segments; i++)
        {
            float angle = -halfAngle + angleStep * i;
            vertices[i + 1] = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
        }

        for (int i = 0; i < segments; i++)
        {
            int t = i * 3;
            triangles[t] = 0;
            triangles[t + 1] = i + 1;
            triangles[t + 2] = i + 2;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    private void UpdateVisual()
    {
        if (visual == null)
        {
            return; // spicchio piatto non creato: c'è solo il modello 3D
        }

        visual.localScale = new Vector3(currentRadius, 1f, currentRadius);

        // L'onda si affievolisce man mano che si avvicina al raggio massimo.
        float fade = 1f - Mathf.Clamp01(currentRadius / maxRadius);
        Color fadedColor = waveColor;
        fadedColor.a *= fade;
        visualMaterial.color = fadedColor;
    }

    private void UpdateCustomVisual()
    {
        if (customVisual == null)
        {
            return;
        }

        // Stessa semantica dello spicchio: mesh costruita a raggio ~1, scalata al raggio corrente.
        // Con customVisualMinScaleFraction il cono parte già visibile invece che da un punto.
        float radiusFraction = maxRadius > 0f ? currentRadius / maxRadius : 1f;
        radiusFraction = Mathf.Lerp(customVisualMinScaleFraction, 1f, Mathf.Clamp01(radiusFraction));
        customVisual.localScale = customVisualBaseScale * (radiusFraction * maxRadius * customVisualScaleMultiplier);

        // Il modello avanza lungo la direzione di tiro (asse Z del proiettile) insieme al
        // fronte d'onda, così copre il cono che sta effettivamente facendo danno invece di
        // restare centrato sul player. localPosition è nello spazio del proiettile, già
        // ruotato verso il bersaglio, quindi Vector3.forward = direzione di mira.
        float forwardDistance = customVisualForwardOffset + currentRadius * customVisualForwardFollow;
        customVisual.localPosition = Vector3.up * visualYOffset + Vector3.forward * forwardDistance;

        if (!customVisualFadeOut || customVisualMaterials == null)
        {
            return;
        }

        float fade = 1f - Mathf.Clamp01(currentRadius / maxRadius);
        for (int i = 0; i < customVisualMaterials.Length; i++)
        {
            if (customVisualColorPropIds[i] < 0)
            {
                continue; // il materiale non ha una property di colore nota: niente fade, resta pieno
            }

            Color faded = customVisualBaseColors[i];
            faded.a *= fade;
            customVisualMaterials[i].SetColor(customVisualColorPropIds[i], faded);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(waveColor.r, waveColor.g, waveColor.b, 0.6f);

        float halfAngle = coneAngle * 0.5f;
        Vector3 previousPoint = transform.position + Quaternion.Euler(0f, -halfAngle, 0f) * transform.forward * maxRadius;

        Gizmos.DrawLine(transform.position, previousPoint);

        int gizmoSegments = Mathf.Max(wedgeSegments, 1);
        for (int i = 1; i <= gizmoSegments; i++)
        {
            float angle = -halfAngle + coneAngle / gizmoSegments * i;
            Vector3 point = transform.position + Quaternion.Euler(0f, angle, 0f) * transform.forward * maxRadius;
            Gizmos.DrawLine(previousPoint, point);
            previousPoint = point;
        }

        Gizmos.DrawLine(transform.position, previousPoint);
    }
}
