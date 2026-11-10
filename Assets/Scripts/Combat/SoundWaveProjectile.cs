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

    [Header("Visuale anello sonico (SonicDoubleWave)")]
    [Tooltip("Se assegnato (es. SonicRingShot da VFX/SonicDoubleWave/Prefabs), ad ogni onda partono due anelli affiancati che volano in avanti fino a maxRadius nello stesso tempo dell'espansione. Sono solo grafica: il loro danno viene azzerato, colpi e cure restano di questo script, che però al posto del cono usa la zona coperta dai due anelli. Sostituisce lo spicchio piatto (vedi alsoShowFlatWave).")]
    [SerializeField] private SonicRingShot ringVisualPrefab;
    [Tooltip("Distanza fra i centri dei due anelli, di lato rispetto alla direzione di tiro.")]
    [SerializeField] private float ringSpacing = 1.4f;
    [Tooltip("Margine aggiunto al raggio degli anelli per decidere chi viene colpito: tiene conto della stazza dei nemici, che vengono misurati dal centro.")]
    [SerializeField] private float ringHitPadding = 0.5f;
    [Tooltip("Anelli usati al posto di ringVisualPrefab in modalità berserk (SonicRingShot_Berserk). Se vuoto restano quelli normali.")]
    [SerializeField] private SonicRingShot berserkRingPrefab;
    [Tooltip("Anelli usati al posto di ringVisualPrefab con il potere serotonina attivo (SonicRingShot di VFX/Onde Serotonina). Se vuoto restano quelli normali.")]
    [SerializeField] private SonicRingShot serotoninRingPrefab;
    [Tooltip("Con serotonina e berserk insieme partono sia gli anelli serotonina sia quelli rossi berserk: questa è la luminosità dei rossi (1 = piena), per non coprire del tutto gli altri.")]
    [Range(0f, 1f)] [SerializeField] private float comboRingOpacity = 0.6f;

    private float currentRadius;
    private float elapsed;
    private float ringStartRadius;
    private float ringEndRadius;
    private float bonusConeAngleApplied; // gradi del bonus Area, riusati per allargare gli anelli
    private bool isBerserk; // vedi MakeBerserk
    private bool isSerotonin; // vedi MakeSerotonin
    private float? ringHeight; // quota degli anelli impostata da LaunchFromSpeakers; null = transform.position.y + visualYOffset
    private readonly HashSet<Collider> alreadyHit = new HashSet<Collider>();
    private readonly HashSet<Collider> alreadyHealed = new HashSet<Collider>();

    // Buffer condiviso per le OverlapSphere di tutte le onde: niente array nuovo ad ogni frame
    // per ogni onda viva (prima era la principale fonte di garbage del gioco). Se si riempie
    // viene raddoppiato e la ricerca ripetuta, così nessun bersaglio va perso per troncamento.
    private static Collider[] overlapBuffer = new Collider[64];

    private int OverlapAll(float radius, int mask)
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, radius, overlapBuffer, mask);
        while (count == overlapBuffer.Length)
        {
            overlapBuffer = new Collider[overlapBuffer.Length * 2];
            count = Physics.OverlapSphereNonAlloc(transform.position, radius, overlapBuffer, mask);
        }
        return count;
    }

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
        if ((customVisualPrefab == null && ringVisualPrefab == null) || alsoShowFlatWave)
        {
            CreateVisual();
        }

        CreateCustomVisual();
    }

    // In Start e non in Awake: così maxRadius include già i bonus di ApplyPowerUps,
    // chiamato da PlayerShooting subito dopo l'Instantiate.
    void Start()
    {
        CreateRingVisuals();
    }

    private bool HasRings => ringVisualPrefab != null;

    private SonicRingShot ActiveRingPrefab =>
        isSerotonin && serotoninRingPrefab != null ? serotoninRingPrefab
        : isBerserk && berserkRingPrefab != null ? berserkRingPrefab
        : ringVisualPrefab;

    // Chiamato da PlayerShooting col potere serotonina attivo (vedi PlayerSerotonin), subito dopo
    // l'Instantiate: anelli serotonina, e il colpo di grazia converte invece di uccidere (vedi
    // EnemyHealth.TakeSerotoninDamage).
    public void MakeSerotonin()
    {
        isSerotonin = true;
    }

    // Chiamato da PlayerShooting col berserk attivo (vedi PlayerBerserk), subito dopo
    // l'Instantiate: anelli rossi più larghi e danno moltiplicato.
    public void MakeBerserk(float damageMultiplier)
    {
        isBerserk = true;
        damage *= damageMultiplier;
    }

    private float Duration => maxRadius / Mathf.Max(expandSpeed, 0.01f);

    // Gli anelli non sono figli dell'onda: SonicRingShot si muove da solo in coordinate mondo
    // dal punto di sparo e si distrugge a fine corsa.
    private void CreateRingVisuals()
    {
        if (!HasRings)
        {
            return;
        }

        // Il bonus Area, pensato in gradi di apertura del cono, qui allarga gli anelli: a fine
        // corsa ciascuno è più largo di quanto si sarebbe aperto il cono di quei gradi in più.
        // In berserk gli anelli rossi sono più larghi: la zona colpita (IsInHitZone) li segue.
        SonicRingShot prefab = ActiveRingPrefab;
        // Serotonina e berserk insieme: partono anche gli anelli rossi, sopra quelli serotonina,
        // e la zona colpita segue i più larghi dei due.
        SonicRingShot comboPrefab = isSerotonin && isBerserk && prefab != berserkRingPrefab ? berserkRingPrefab : null;
        float baseEndRadius = comboPrefab != null ? Mathf.Max(prefab.endRadius, comboPrefab.endRadius) : prefab.endRadius;
        ringStartRadius = comboPrefab != null ? Mathf.Max(prefab.startRadius, comboPrefab.startRadius) : prefab.startRadius;
        ringEndRadius = baseEndRadius + maxRadius * Mathf.Tan(bonusConeAngleApplied * 0.5f * Mathf.Deg2Rad);

        Vector3 right = Flat(transform.right);
        Vector3 origin = transform.position + Vector3.up * visualYOffset;
        if (ringHeight.HasValue)
        {
            origin.y = ringHeight.Value;
        }
        SpawnRing(prefab, origin - right * (ringSpacing * 0.5f), 1f);
        SpawnRing(prefab, origin + right * (ringSpacing * 0.5f), 1f);
        if (comboPrefab != null)
        {
            SpawnRing(comboPrefab, origin - right * (ringSpacing * 0.5f), comboRingOpacity);
            SpawnRing(comboPrefab, origin + right * (ringSpacing * 0.5f), comboRingOpacity);
        }
    }

    private void SpawnRing(SonicRingShot prefab, Vector3 position, float opacity)
    {
        SonicRingShot ring = Instantiate(prefab, position, Quaternion.LookRotation(Flat(transform.forward)));
        ring.knockback = 0f;
        ring.hitMask = 0; // nessun collider trovato: niente danno né spinta, solo grafica
        ring.range = maxRadius;
        ring.duration = Duration;
        ring.endRadius = ringEndRadius;
        ring.opacity = opacity;
        ring.Init(1f);
    }

    // Chiamato da PlayerShooting subito dopo l'Instantiate, prima di Start (dove nascono gli
    // anelli): i due anelli partono dalle casse del modello del Player invece che a ringSpacing
    // di distanza attorno al punto di sparo. L'onda si sposta in pianta a metà fra le due casse,
    // ma resta alla sua quota (la zona colpita è misurata in piano); solo gli anelli salgono
    // all'altezza delle casse.
    public void LaunchFromSpeakers(Vector3 left, Vector3 right)
    {
        Vector3 middle = (left + right) * 0.5f;
        transform.position = new Vector3(middle.x, transform.position.y, middle.z);
        ringSpacing = Mathf.Abs(Vector3.Dot(right - left, Flat(transform.right)));
        ringHeight = middle.y;
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
        bonusConeAngleApplied += bonusConeAngle;
        maxRadius += bonusMaxRadius;

        if (visualMeshFilter != null)
        {
            visualMeshFilter.mesh = BuildWedgeMesh(coneAngle, wedgeSegments);
        }
    }

    void Update()
    {
        if (HasRings)
        {
            // Il fronte d'onda segue gli anelli, che rallentano verso la fine della corsa (stessa
            // curva di SonicRingShot): così un nemico viene colpito quando l'anello lo raggiunge.
            elapsed += Time.deltaTime;
            float p = Mathf.Clamp01(elapsed / Duration);
            currentRadius = maxRadius * (1f - Mathf.Pow(1f - p, 2.2f));
            if (p >= 1f)
            {
                currentRadius = maxRadius;
            }
        }
        else
        {
            currentRadius += expandSpeed * Time.deltaTime;
        }

        DamageEnemiesInRange();
        HealRaversInRange();
        UpdateVisual();
        UpdateCustomVisual();

        if (currentRadius >= maxRadius)
        {
            Destroy(gameObject);
        }
    }

    // Physics non ha un OverlapCone: si cerca in una sfera che contiene tutta la zona colpita e
    // poi si filtra con IsInHitZone. Con gli anelli la zona sporge di lato oltre il fronte
    // d'onda, quindi la sfera va allargata di quanto sporge.
    private float SearchRadius => HasRings
        ? currentRadius + ringSpacing * 0.5f + ringEndRadius + ringHitPadding
        : currentRadius;

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }

    // Zona colpita: con gli anelli, i due "tubi" che lasciano dietro di sé (larghi quanto
    // l'anello in quel punto, più ringHitPadding) fino al fronte d'onda; senza, il cono classico.
    private bool IsInHitZone(Vector3 targetPosition)
    {
        Vector3 toTarget = targetPosition - transform.position;
        toTarget.y = 0f; // confronto sul piano orizzontale, come EnemyChase

        if (!HasRings)
        {
            return Vector3.Angle(transform.forward, toTarget) <= coneAngle * 0.5f;
        }

        float along = Vector3.Dot(toTarget, Flat(transform.forward));
        if (along < 0f || along > currentRadius)
        {
            return false;
        }

        float halfWidth = Mathf.Lerp(ringStartRadius, ringEndRadius, along / Mathf.Max(maxRadius, 0.01f)) + ringHitPadding;
        float side = Vector3.Dot(toTarget, Flat(transform.right));
        return Mathf.Abs(side + ringSpacing * 0.5f) <= halfWidth || Mathf.Abs(side - ringSpacing * 0.5f) <= halfWidth;
    }

    private void DamageEnemiesInRange()
    {
        int hitCount = OverlapAll(SearchRadius, targetMask);
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = overlapBuffer[i];
            if (alreadyHit.Contains(hit))
            {
                continue;
            }

            if (!IsInHitZone(hit.transform.position))
            {
                continue; // dentro alla sfera di ricerca ma fuori dalla zona colpita
            }

            alreadyHit.Add(hit);
            if (hit.TryGetComponent(out EnemyHealth enemyHealth))
            {
                // Uno Sbirro convertito dalla serotonina sta col Player: niente danno. Le onde
                // serotonina non fanno niente a chi non si può convertire (IdroSbirro), nemmeno
                // il feedback di colpo.
                if (enemyHealth.IsCharmed || (isSerotonin && !SerotoninBrawl.CanBrawl(enemyHealth)))
                {
                    continue;
                }

                if (isSerotonin)
                {
                    enemyHealth.TakeSerotoninDamage(damage, transform.position);
                }
                else
                {
                    enemyHealth.TakeDamage(damage, transform.position);
                }
                HitEvents.RaiseEnemyHit(enemyHealth.gameObject, transform.position); // feedback di colpo (Hit*Feedback.cs)
            }
        }
    }

    private void HealRaversInRange()
    {
        // Nessun LayerMask qui: il Raver non è detto sia sul layer nemici (targetMask),
        // quindi cerchiamo direttamente chi ha un RaverHealth.
        int hitCount = OverlapAll(SearchRadius, Physics.AllLayers);
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = overlapBuffer[i];
            if (alreadyHealed.Contains(hit))
            {
                continue;
            }

            if (!hit.TryGetComponent(out RaverHealth raverHealth))
            {
                continue;
            }

            if (!IsInHitZone(hit.transform.position))
            {
                continue; // dentro alla sfera di ricerca ma fuori dalla zona colpita
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
        visualMaterial = new Material(MaterialLibrary.SpriteDefault);
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
