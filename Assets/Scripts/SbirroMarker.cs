using UnityEngine;

// Segno di riconoscimento degli Sbirro, per distinguerli a colpo d'occhio nella ressa coi Raver:
//  - un lampeggiante da polizia sopra la testa, rosso e blu che si alternano, sempre girato
//    verso la camera;
//  - un cerchio nero sulla sabbia sotto i piedi, uguale per tutti.
// Uno Sbirro convertito dalla serotonina perde la sirena e ha il cerchio rosa.
// Lo aggiunge da solo EnemyHealth.Start, niente da collegare: sprite disegnati a runtime con
// Sprites/Default, come le altre texture procedurali del progetto.
public class SbirroMarker : MonoBehaviour
{
    private static readonly Color RingColor = new Color(0f, 0f, 0f, 0.85f);
    // Convertito dalla serotonina (EnemyHealth.IsCharmed): sta dalla nostra parte, quindi via la
    // sirena e cerchio rosa, come i cuoricini di SerotoninBrawl.
    private static readonly Color CharmedRingColor = new Color(1f, 0.35f, 0.7f, 0.85f);

    private static readonly Color SirenRed = new Color(1f, 0.1f, 0.1f, 1f);
    private static readonly Color SirenBlue = new Color(0.15f, 0.4f, 1f, 1f);

    private const float FlashInterval = 0.25f;  // secondi per colore
    private const float SirenSize = 0.55f;      // metri
    private const float SirenGap = 0.25f;       // dalla cima del modello
    private const float RingRadiusScale = 1.1f; // raggio del cerchio rispetto a quello dello Sbirro
    private const float RingLift = 0.03f;       // sopra la sabbia, per non sfarfallare con lei
    private const float RingBorder = 0.86f;     // dove sta il bordo nello sprite, in frazione del raggio
    private const float BossRingScale = 1.4f;   // il boss è 1.4 volte lo Sbirro1 (CyberGiorgiaSetup)

    private static Sprite glowSprite;
    private static Sprite ringSprite;
    private static Material spriteMaterial;
    private static Camera cachedCamera;

    private Transform siren;
    private SpriteRenderer sirenRed;
    private SpriteRenderer sirenBlue;
    private Transform ring;
    // Centro del cerchio rispetto alla radice: zero per gli Sbirro, il centro del mezzo per
    // l'IdroSbirro (il pivot del camion non sta per forza nel mezzo).
    private Vector3 ringLocalCenter;
    private SpriteRenderer ringRenderer;
    private EnemyHealth enemyHealth;
    private float phase; // sfasamento, così non lampeggiano tutti all'unisono

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetState()
    {
        glowSprite = null;
        ringSprite = null;
        loggedKinds.Clear();
        spriteMaterial = null;
        cachedCamera = null;
    }

    void Start()
    {
        Bounds bounds = MeasureBounds();
        // Entro dei limiti: l'ingombro di una mesh animata può venire sballato, e un cerchio enorme
        // e tenue non si legge più come cerchio.
        float radius = RingRadius(bounds);
        // L'IdroSbirro è un camion: niente limiti, il cerchio deve contenerlo tutto. Il bordo
        // dello sprite sta a RingBorder del raggio (vedi GetRingSprite), più un filo di margine.
        if (GetComponent<IdroSbirro>() != null)
        {
            radius = MeasureVehicleFootprint(out ringLocalCenter) * 1.05f / (RingBorder * RingRadiusScale);
        }

        // Il boss (CyberGiorgia, aggiunto da CyberGiorgiaBoss all'atterraggio) solo il cerchio,
        // senza lampeggiante, e più largo quanto è più grande di uno Sbirro.
        bool boss = GetComponent<CyberGiorgiaBoss>() != null;
        if (boss)
        {
            radius = RingRadius(bounds) * BossRingScale;
        }
        phase = Random.value * FlashInterval * 2f;

        // Lampeggiante: due luci affiancate, una rossa e una blu, sopra la testa.
        // Figlio del modello e non della radice: SbirroGroundAlign sposta il modello in su o in
        // giù per appoggiargli i piedi a terra, e il lampeggiante deve restargli sopra la testa.
        if (!boss)
        {
            Animator animator = GetComponentInChildren<Animator>();
            siren = new GameObject("SbirroSiren").transform;
            siren.SetParent(animator != null ? animator.transform : transform, false);
            siren.position = new Vector3(bounds.center.x, bounds.max.y + SirenGap + SirenSize * 0.5f, bounds.center.z);
            sirenRed = CreateSprite("Red", siren, GetGlowSprite(), SirenRed, new Vector3(-SirenSize * 0.35f, 0f, 0f), SirenSize, 10);
            sirenBlue = CreateSprite("Blue", siren, GetGlowSprite(), SirenBlue, new Vector3(SirenSize * 0.35f, 0f, 0f), SirenSize, 10);
        }

        // Cerchio nero a terra.
        ringRenderer = CreateRing("SbirroRing", transform, radius, RingColor);
        ring = ringRenderer.transform;
        enemyHealth = GetComponent<EnemyHealth>();
        PlaceRing();

        // Controllo: una riga per tipo di Sbirro, con dove è finito il cerchio.
        string kind = name.Replace("(Clone)", "").Trim();
        if (loggedKinds.Add(kind))
        {
            bool onGround = TryFindGroundY(out float rayY);
            Debug.Log($"[SbirroMarker] {kind}: Sbirro a quota {transform.position.y:0.00}, " +
                      $"pavimento (FloorBounds) a {(FloorBounds.Instance != null ? FloorBounds.Instance.Bounds.max.y.ToString("0.00") : "assente")}, " +
                      (onGround ? $"raggio trova {rayY:0.00}" : "raggio non trova niente") +
                      $", cerchio a {ring.position.y:0.00}, raggio {radius * RingRadiusScale:0.00} m.");
        }
    }

    private static readonly System.Collections.Generic.HashSet<string> loggedKinds = new System.Collections.Generic.HashSet<string>();

    void LateUpdate()
    {
        if (ring == null)
        {
            return;
        }

        PlaceRing();

        // Convertito: niente sirena, cerchio rosa. Ricontrollato ogni fotogramma, così vale
        // anche se un giorno la conversione finisce.
        bool charmed = enemyHealth != null && enemyHealth.IsCharmed;
        ringRenderer.color = charmed ? CharmedRingColor : RingColor;
        if (siren == null)
        {
            return; // il boss: solo il cerchio
        }
        if (siren.gameObject.activeSelf == charmed)
        {
            siren.gameObject.SetActive(!charmed);
        }
        if (charmed)
        {
            return;
        }

        if (cachedCamera == null)
        {
            cachedCamera = Camera.main;
        }
        if (cachedCamera != null)
        {
            siren.rotation = cachedCamera.transform.rotation; // billboard
        }

        // Una luce alla volta, a piena intensità; l'altra resta appena accesa.
        bool redOn = Mathf.Repeat(Time.time + phase, FlashInterval * 2f) < FlashInterval;
        sirenRed.color = WithAlpha(SirenRed, redOn ? 1f : 0.2f);
        sirenBlue.color = WithAlpha(SirenBlue, redOn ? 0.2f : 1f);
    }

    // Il cerchio sta sulla sabbia sotto lo Sbirro (vedi GroundY), e non al fondo del suo
    // ingombro: quello di un modello animato può scendere sotto terra, e il
    // cerchio ci restava sepolto (si vedeva solo sotto il corazzato). Rifatto ogni fotogramma:
    // gli Sbirro nascono fuori dall'arena e possono cadere o salire su un dislivello.
    private void PlaceRing()
    {
        PlaceRingAt(ring, transform.TransformPoint(ringLocalCenter), GroundY());
    }

    // Quota della sabbia: il piano del pavimento di FloorBounds, lo stesso su cui EnemyChase
    // appoggia gli Sbirro quando nascono (snapToFloorOnSpawn). Il raggio verso il basso da solo
    // non bastava: sotto lo Sbirro1 non trovava niente e il cerchio ripiegava sul fondo del
    // modello, sotto il pavimento. Senza FloorBounds in scena si torna al raggio.
    private float GroundY()
    {
        if (FloorBounds.Instance != null)
        {
            return FloorBounds.Instance.Bounds.max.y;
        }
        return TryFindGroundY(out float y) ? y : MeasureBounds().min.y;
    }

    // La superficie solida più alta all'altezza dei piedi, che non sia dello Sbirro né di un altro
    // personaggio. Il raggio parte dall'alto e non da dentro lo Sbirro: lo Sbirro1 viene
    // appoggiato a pochi centimetri dal piano del pavimento (EnemyChase.snapToFloorOnSpawn), e un
    // raggio che parte già dentro il pavimento non lo trova. Tettoie e simili sopra la testa non
    // contano: si prende solo quello che sta entro GroundSearchAbove dalla sua origine.
    private const float GroundSearchAbove = 1f;

    private bool TryFindGroundY(out float groundY)
    {
        groundY = 0f;
        Vector3 origin = transform.position + Vector3.up * 5f;
        float maxY = transform.position.y + GroundSearchAbove;
        bool found = false;
        foreach (RaycastHit hit in Physics.RaycastAll(origin, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore))
        {
            Transform other = hit.collider.transform;
            if (hit.point.y > maxY
                || (found && hit.point.y <= groundY)
                || other.IsChildOf(transform)
                || other.GetComponentInParent<EnemyHealth>() != null
                || other.GetComponentInParent<RaverHealth>() != null
                || other.GetComponentInParent<PlayerHealth>() != null)
            {
                continue;
            }

            groundY = hit.point.y;
            found = true;
        }

        return found;
    }

    // Pezzi del cerchio a terra usati anche da RaverMarker, così i due cerchi sono identici.
    // Raggio del personaggio dal suo ingombro, entro dei limiti: l'ingombro di una mesh animata
    // può venire sballato, e un cerchio enorme e tenue non si legge più come cerchio.
    public static float RingRadius(Bounds bounds)
    {
        return Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z), 0.35f, 0.9f);
    }

    public static SpriteRenderer CreateRing(string name, Transform parent, float radius, Color color)
    {
        return CreateSprite(name, parent, GetRingSprite(), color, Vector3.zero, radius * RingRadiusScale * 2f, 0);
    }

    // Steso a terra sotto center, sul piano del pavimento di FloorBounds (vedi GroundY).
    public static void PlaceRingAt(Transform ring, Vector3 center, float groundY)
    {
        ring.position = new Vector3(center.x, groundY + RingLift, center.z);
        ring.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    private static SpriteRenderer CreateSprite(string name, Transform parent, Sprite sprite, Color color, Vector3 localPosition, float worldSize, int sortingOrder)
    {
        GameObject spriteObject = new GameObject(name);
        spriteObject.transform.SetParent(parent, false);

        SpriteRenderer spriteRenderer = spriteObject.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = sprite;
        spriteRenderer.color = color;
        spriteRenderer.sharedMaterial = GetMaterial();
        spriteRenderer.sortingOrder = sortingOrder;
        spriteRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        spriteRenderer.receiveShadows = false;

        // Lo sprite è largo 1 unità: la scala lo porta a worldSize metri, e lo spostamento a
        // localPosition metri, al netto della scala dello Sbirro (diversa da un tipo all'altro).
        float parentScale = Mathf.Max(0.0001f, parent.lossyScale.x);
        spriteObject.transform.localScale = Vector3.one * (worldSize / parentScale);
        spriteObject.transform.localPosition = localPosition / parentScale;
        return spriteRenderer;
    }

    // Ingombro dei Renderer visibili dello Sbirro (il modello), per la testa e la taglia.
    private Bounds MeasureBounds()
    {
        bool found = false;
        Bounds bounds = new Bounds(transform.position, Vector3.one * 2f);
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer))
            {
                continue;
            }

            if (!found)
            {
                bounds = r.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        return bounds;
    }

    // Raggio in metri del cerchio che contiene in pianta tutto il mezzo, e il suo centro
    // rispetto alla radice. Misurato sulle mesh nel sistema della radice (non sull'ingombro nel
    // mondo, che col camion girato si gonfia): il raggio è la distanza dal centro del
    // rettangolo all'angolo più lontano.
    private float MeasureVehicleFootprint(out Vector3 localCenter)
    {
        bool found = false;
        Vector2 min = Vector2.zero, max = Vector2.zero;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            Bounds meshBounds;
            if (r is SkinnedMeshRenderer skin && skin.sharedMesh != null)
            {
                meshBounds = skin.sharedMesh.bounds;
            }
            else if (r is MeshRenderer && r.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
            {
                meshBounds = filter.sharedMesh.bounds;
            }
            else
            {
                continue;
            }

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = meshBounds.center + Vector3.Scale(meshBounds.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector3 local = transform.InverseTransformPoint(r.transform.TransformPoint(corner));
                Vector2 flat = new Vector2(local.x, local.z);
                min = found ? Vector2.Min(min, flat) : flat;
                max = found ? Vector2.Max(max, flat) : flat;
                found = true;
            }
        }

        if (!found)
        {
            localCenter = Vector3.zero;
            return 2f;
        }

        Vector2 center = (min + max) * 0.5f;
        localCenter = new Vector3(center.x, 0f, center.y);
        float scale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
        return (max - min).magnitude * 0.5f * scale;
    }

    private static Material GetMaterial()
    {
        if (spriteMaterial == null)
        {
            spriteMaterial = new Material(Shader.Find("Sprites/Default"));
        }
        return spriteMaterial;
    }

    // Luce: nucleo pieno e alone che sfuma, così da lontano si legge come una lampadina accesa.
    private static Sprite GetGlowSprite()
    {
        if (glowSprite == null)
        {
            glowSprite = DrawSprite(64, (u, v) =>
            {
                float distance = Mathf.Sqrt(u * u + v * v);
                float core = Mathf.Clamp01((0.35f - distance) / 0.1f);
                float halo = Mathf.Pow(Mathf.Clamp01(1f - distance), 2.5f) * 0.8f;
                return new Color(1f, 1f, 1f, Mathf.Max(core, halo));
            });
        }
        return glowSprite;
    }

    // Cerchio: bordo pieno e interno velato. Bianco, il nero lo dà RingColor.
    private static Sprite GetRingSprite()
    {
        if (ringSprite == null)
        {
            ringSprite = DrawSprite(128, (u, v) =>
            {
                float distance = Mathf.Sqrt(u * u + v * v);
                float border = Mathf.Clamp01(1f - Mathf.Abs(distance - RingBorder) / 0.09f);
                float fill = distance < RingBorder ? 0.45f : 0f;
                return new Color(1f, 1f, 1f, Mathf.Max(border, fill));
            });
        }
        return ringSprite;
    }

    private static Sprite DrawSprite(int size, System.Func<float, float, Color> colorAt)
    {
        float half = size * 0.5f;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / half - 1f; // da -1 a 1, centro dell'immagine nell'origine
                float v = (y + 0.5f) / half - 1f;
                texture.SetPixel(x, y, colorAt(u, v));
            }
        }
        texture.Apply();

        // pixelsPerUnit = size: lo sprite è largo 1 unità, vedi CreateSprite.
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
