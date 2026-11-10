using System.Collections;
using UnityEngine;

// CyberGiorgia, il boss finale. Sul prefab (CyberGiorgia.prefab, montato da Tools/CyberGiorgia/
// Monta il boss) insieme a EnemyHealth ed EnemyRangedAttack. La genera CyberGiorgiaSpawner dopo
// 3 minuti, al centro della mappa, coi piedi a terra; da lì:
//  - avviso: per warningDuration secondi a terra c'è un segno rosso, grande quanto l'area
//    d'impatto, che si riempie man mano. Lei non si vede ancora e non ha collider;
//  - caduta: compare fallHeight metri più su e piomba giù con l'animazione FreeFall;
//  - atterraggio: se il Player è dentro il segno prende un colpo. Onda di sabbia tutt'intorno e
//    tremolio dello schermo, poi si rialza (il resto della clip FreeFall, a getUpSpeed) e resta
//    ferma landingPause secondi;
//  - da lì passa la mano a EnemyRangedAttack (spento nel prefab fino ad allora): insegue il
//    Player correndo e gli lancia le granate da lontano con l'animazione di sparo.
[RequireComponent(typeof(Rigidbody))]
public class CyberGiorgiaBoss : MonoBehaviour
{
    private const string FreeFallState = "FreeFall";
    private const string LandedParameter = "Landed";
    private const string FallSpeedParameter = "FallSpeed"; // velocità dello stato FreeFall

    [Header("Avviso")]
    [Tooltip("Secondi in cui il segno rosso resta a terra prima che lei cada.")]
    [SerializeField] private float warningDuration = 2f;
    [Tooltip("Raggio del segno rosso, ed è anche l'area d'impatto: il Player dentro quando tocca terra prende un colpo.")]
    [SerializeField] private float impactRadius = 2.5f;
    [SerializeField] private Color warningColor = new Color(1f, 0.08f, 0.05f, 0.9f);

    [Header("Caduta")]
    [Tooltip("Da quanti metri sopra il punto d'atterraggio cade.")]
    [SerializeField] private float fallHeight = 25f;
    [Tooltip("Secondi di caduta: accelera come un sasso, lenta in alto e velocissima a terra.")]
    [SerializeField] private float fallDuration = 1f;
    [Tooltip("Secondi ferma dopo che si è rialzata, prima di mettersi a inseguire il Player.")]
    [SerializeField] private float landingPause = 0.2f;

    [Header("Clip FreeFall")]
    [Tooltip("Velocità della parte della clip FreeFall in cui si rialza dopo l'atterraggio: sotto 1 più lenta.")]
    [SerializeField] private float getUpSpeed = 0.6f;
    [Tooltip("Secondi della clip FreeFall in cui tocca terra (le anche al punto più basso). Lo scrive Tools/CyberGiorgia/Monta il boss.")]
    [SerializeField] private float freeFallLandTime = 1f;
    [Tooltip("Durata della clip FreeFall in secondi. Lo scrive Tools/CyberGiorgia/Monta il boss.")]
    [SerializeField] private float freeFallLength = 1f;

    [Header("Onda di sabbia")]
    [Tooltip("Fin dove arriva l'onda di sabbia, in metri dal punto d'atterraggio. Solo un effetto: il danno lo fa l'impatto (impactRadius).")]
    [SerializeField] private float sandWaveRadius = 8f;
    [SerializeField] private Color sandColor = new Color(0.86f, 0.74f, 0.52f, 0.9f);
    [Tooltip("Quanti puffi di sabbia compongono l'anello che si allarga.")]
    [SerializeField] private int sandParticleCount = 160;
    [Tooltip("Tremolio dello schermo all'atterraggio, da 0 a 1 (CameraShake).")]
    [Range(0f, 1f)]
    [SerializeField] private float landingShake = 0.7f;

    [Header("Audio")]
    [Tooltip("Suonato una volta appena tocca terra (Audio/GIORGIA), in 2D: si sente uguale ovunque sia la camera.")]
    [SerializeField] private AudioClip landingClip;
    [Tooltip("Volume della traccia: sopra 1 (il massimo di un AudioSource) la si suona su più AudioSource sovrapposti, che si sommano. 2 = il doppio; troppo alto può distorcere.")]
    [Range(0f, 4f)]
    [SerializeField] private float landingVolume = 4f;

    private static Sprite ringSprite;
    private static Sprite discSprite;
    private static Material spriteMaterial;

    private Rigidbody rb;
    private EnemyHealth health;
    private EnemyRangedAttack ranged;
    private Animator animator;
    private Renderer[] renderers;
    private Collider[] colliders;
    private Vector3 landingPoint;
    private GameObject fallWarning; // il segno rosso, dall'avviso fino all'atterraggio

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetState()
    {
        ringSprite = null;
        discSprite = null;
        spriteMaterial = null;
    }

    // true da quando compare (segno rosso compreso) finché è viva: EnemySpawner nel frattempo
    // manda meno Sbirro (vedi bossSpawnReduction).
    public static bool IsPresent => instance != null;
    private static CyberGiorgiaBoss instance;

    // In Awake e non in Start: non si deve vedere nemmeno per un fotogramma dove è nata.
    void Awake()
    {
        instance = this;
        rb = GetComponent<Rigidbody>();
        health = GetComponent<EnemyHealth>();
        ranged = GetComponent<EnemyRangedAttack>();
        animator = GetComponentInChildren<Animator>();
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            int count = GetComponentsInChildren<Animator>(true).Length;
            Debug.LogError($"[CyberGiorgia] Animator senza controller ({(animator != null ? animator.name : "nessun Animator")}, " +
                           $"{count} Animator in tutto): niente animazioni. Rilancia Tools/CyberGiorgia/Monta il boss.");
        }
        renderers = GetComponentsInChildren<Renderer>();

        // Ingombro ricalcolato sulla posa animata: quello fisso dell'FBX è preso su un'altra posa,
        // e Unity può crederla fuori dallo schermo e smettere di disegnarla.
        foreach (SkinnedMeshRenderer skin in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            skin.updateWhenOffscreen = true;
        }
        colliders = GetComponentsInChildren<Collider>();
        landingPoint = transform.position;

        // Cinematico finché non è a terra: la caduta la muoviamo noi, e niente la deve spingere.
        rb.isKinematic = true;
        if (ranged != null)
        {
            ranged.enabled = false;
        }

        SetRenderersVisible(false);
        SetCollidersEnabled(false);
    }

    // Morta ammazzata (non scaricata col cambio di scena): se anche gli IdroSbirro sono già
    // abbattuti la partita è vinta (vedi FinalVictory).
    void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }

        if (health != null && health.CurrentHealth <= 0f)
        {
            FinalVictory.BossKilled();
        }
    }

    private IEnumerator Start()
    {
        yield return Warn();
        yield return Fall();
        Land();

        // Si rialza (il resto della clip FreeFall, rallentato), poi una breve pausa.
        float getUpDuration = Mathf.Max(0f, freeFallLength - freeFallLandTime) / Mathf.Max(getUpSpeed, 0.01f);
        yield return new WaitForSeconds(getUpDuration + landingPause);

        rb.isKinematic = false;
        if (ranged != null)
        {
            ranged.enabled = true;
        }
    }

    private IEnumerator Warn()
    {
        GameObject warning = new GameObject("CyberGiorgiaWarning");
        warning.transform.position = new Vector3(landingPoint.x, landingPoint.y + 0.04f, landingPoint.z);
        warning.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        // Il bordo dice dove arriva l'impatto, il disco che si allarga dentro quanto manca.
        SpriteRenderer ring = CreateFlatSprite("Ring", warning.transform, GetRingSprite(), impactRadius * 2f, 1);
        SpriteRenderer disc = CreateFlatSprite("Fill", warning.transform, GetDiscSprite(), 0f, 0);
        disc.color = WithAlpha(warningColor, warningColor.a * 0.45f);

        float elapsed = 0f;
        while (elapsed < warningDuration)
        {
            float t = elapsed / warningDuration;
            disc.transform.localScale = Vector3.one * (impactRadius * 2f * t);

            // Lampeggia sempre più in fretta man mano che lei arriva.
            float blink = 0.5f + 0.5f * Mathf.Cos(elapsed * Mathf.Lerp(8f, 30f, t));
            ring.color = WithAlpha(warningColor, warningColor.a * Mathf.Lerp(0.45f, 1f, blink));

            elapsed += Time.deltaTime;
            yield return null;
        }

        // Il segno resta finché non tocca terra: lo toglie Fall.
        fallWarning = warning;
    }

    private IEnumerator Fall()
    {
        FacePlayer();
        Vector3 top = landingPoint + Vector3.up * fallHeight;
        rb.position = top;
        transform.position = top;
        SetRenderersVisible(true);

        // La parte in aria della clip (fino a freeFallLandTime) dura quanto la caduta: tocca terra
        // nella clip proprio quando tocca terra davvero.
        if (animator != null)
        {
            animator.SetFloat(FallSpeedParameter, freeFallLandTime / Mathf.Max(fallDuration, 0.01f));
            animator.Play(FreeFallState, 0, 0f);
        }

        float elapsed = 0f;
        while (elapsed < fallDuration)
        {
            yield return new WaitForFixedUpdate();
            elapsed += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(elapsed / fallDuration);
            rb.MovePosition(landingPoint + Vector3.up * (fallHeight * (1f - t * t)));
        }

        rb.position = landingPoint;
        transform.position = landingPoint;

        if (fallWarning != null)
        {
            Destroy(fallWarning);
        }
    }

    private void Land()
    {
        SetCollidersEnabled(true);
        if (animator != null)
        {
            animator.SetFloat(FallSpeedParameter, getUpSpeed);
            animator.SetBool(LandedParameter, true);
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null && player.TryGetComponent(out PlayerHealth playerHealth) && playerHealth.CanBeAttacked)
        {
            Vector3 toPlayer = player.transform.position - landingPoint;
            toPlayer.y = 0f;
            if (toPlayer.magnitude <= impactRadius)
            {
                playerHealth.TakeHit();
            }
        }

        CameraShake.Add(landingShake);
        SpawnSandWave(landingPoint, sandWaveRadius, sandColor, sandParticleCount);
        PlayLandingClip();

        // La barra della vita e il cerchio nero a terra (come gli altri Sbirro, vedi SbirroMarker)
        // compaiono quando è a terra: prima non la si può colpire.
        gameObject.AddComponent<IdroSbirroHealthBar>();
        gameObject.AddComponent<SbirroMarker>();
    }

    // Su un oggetto a parte, così finisce anche se il boss muore prima che la traccia sia finita.
    private void PlayLandingClip()
    {
        if (landingClip == null)
        {
            return;
        }

        GameObject audioObject = new GameObject("GiorgiaLandingAudio");
        int layers = Mathf.Max(1, Mathf.CeilToInt(landingVolume));
        for (int i = 0; i < layers; i++)
        {
            AudioSource source = audioObject.AddComponent<AudioSource>();
            source.clip = landingClip;
            source.volume = landingVolume / layers;
            source.spatialBlend = 0f;
            source.Play();
        }
        Destroy(audioObject, landingClip.length + 0.1f);
    }

    private void FacePlayer()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
        {
            return;
        }

        Vector3 toPlayer = player.transform.position - landingPoint;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > 0.0001f)
        {
            Quaternion rotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
            rb.rotation = rotation;
            transform.rotation = rotation;
        }
    }

    // Due strati, entrambi ParticleSystem creati a runtime come SbirroCloud: un anello di puffi
    // che corre via fino a sandWaveRadius, e una nuvola più grossa e lenta che resta attorno ai
    // piedi. Pubblico e statico: lo usa anche l'atterraggio del Player a inizio partita
    // (DifficultyMenu), che avviene col gioco ancora in pausa, da cui unscaledTime.
    public static void SpawnSandWave(Vector3 point, float radius, Color color, int count, bool unscaledTime = false)
    {
        const float RingLifetime = 0.9f;
        const float DustLifetime = 1.6f;

        GameObject wave = new GameObject("CyberGiorgiaSandWave");
        wave.transform.position = point + Vector3.up * 0.2f;

        CreateSandBurst(wave.transform, "Ring", count, radius / RingLifetime, 0.8f, 1.8f, RingLifetime, 0.4f, color, unscaledTime);
        CreateSandBurst(wave.transform, "Dust", count / 3, radius * 0.25f / DustLifetime, 1.8f, 3.2f, DustLifetime, 1f, color, unscaledTime);

        // A gioco in pausa un Destroy ritardato aspetterebbe la ripresa: va bene lo stesso.
        Destroy(wave, DustLifetime + 0.2f);
    }

    private static void CreateSandBurst(Transform parent, string name, int count, float speed, float minSize, float maxSize, float lifetime, float emitRadius, Color sandColor, bool unscaledTime)
    {
        GameObject burstObject = new GameObject(name);
        burstObject.transform.SetParent(parent, false);

        ParticleSystem particles = burstObject.AddComponent<ParticleSystem>();
        // Stesso motivo di SbirroCloud: AddComponent lo fa partire subito, e da "in play" Unity
        // non lascia cambiare la durata.
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = lifetime;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.8f, lifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.85f, speed * 1.15f);
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startColor = sandColor;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = count;
        main.useUnscaledTime = unscaledTime;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });

        // Cerchio steso a terra: i puffi partono dal bordo e corrono via in orizzontale.
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = emitRadius;
        shape.radiusThickness = 0f;
        shape.rotation = new Vector3(90f, 0f, 0f);

        // Scatto forte e poi frenata, come un'onda che perde forza; un filo verso l'alto, così la
        // sabbia si solleva invece di strisciare.
        ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        // Tutti e tre gli assi nello stesso modo (due costanti), altrimenti Unity li rifiuta.
        velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.speedModifier = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1.6f, 1f, 0.4f));

        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.4f));

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(sandColor, 0f), new GradientColorKey(sandColor, 1f) },
            new[] { new GradientAlphaKey(sandColor.a, 0f), new GradientAlphaKey(sandColor.a, 0.5f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;

        ParticleSystemRenderer particleRenderer = burstObject.GetComponent<ParticleSystemRenderer>();
        particleRenderer.material = new Material(Shader.Find("Sprites/Default"));
        particleRenderer.material.mainTexture = SoftParticleTexture.Get();
        particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        particles.Play();
    }

    private void SetRenderersVisible(bool visible)
    {
        foreach (Renderer r in renderers)
        {
            if (r != null)
            {
                r.enabled = visible;
            }
        }
    }

    private void SetCollidersEnabled(bool on)
    {
        foreach (Collider c in colliders)
        {
            if (c != null)
            {
                c.enabled = on;
            }
        }
    }

    // Sprite steso a terra, largo worldSize metri (lo sprite è largo 1 unità).
    private static SpriteRenderer CreateFlatSprite(string name, Transform parent, Sprite sprite, float worldSize, int sortingOrder)
    {
        GameObject spriteObject = new GameObject(name);
        spriteObject.transform.SetParent(parent, false);
        spriteObject.transform.localScale = Vector3.one * worldSize;

        SpriteRenderer spriteRenderer = spriteObject.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = sprite;
        spriteRenderer.sharedMaterial = GetSpriteMaterial();
        spriteRenderer.sortingOrder = sortingOrder;
        spriteRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        spriteRenderer.receiveShadows = false;
        return spriteRenderer;
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    private static Material GetSpriteMaterial()
    {
        if (spriteMaterial == null)
        {
            spriteMaterial = new Material(Shader.Find("Sprites/Default"));
        }
        return spriteMaterial;
    }

    // Bordo spesso e pieno, con un filo di riempimento dentro e un mirino a croce.
    private static Sprite GetRingSprite()
    {
        if (ringSprite == null)
        {
            ringSprite = DrawSprite(128, (u, v) =>
            {
                float distance = Mathf.Sqrt(u * u + v * v);
                float border = Mathf.Clamp01(1f - Mathf.Abs(distance - 0.92f) / 0.06f);
                float cross = (Mathf.Abs(u) < 0.025f || Mathf.Abs(v) < 0.025f) && distance < 0.85f ? 0.6f : 0f;
                float fill = distance < 0.9f ? 0.12f : 0f;
                return new Color(1f, 1f, 1f, Mathf.Max(border, Mathf.Max(cross, fill)));
            });
        }
        return ringSprite;
    }

    // Disco pieno con il bordo appena sfumato.
    private static Sprite GetDiscSprite()
    {
        if (discSprite == null)
        {
            discSprite = DrawSprite(64, (u, v) =>
            {
                float distance = Mathf.Sqrt(u * u + v * v);
                return new Color(1f, 1f, 1f, Mathf.Clamp01((1f - distance) / 0.08f));
            });
        }
        return discSprite;
    }

    // u, v vanno da -1 a 1 sullo sprite; lo sprite è largo 1 unità di mondo.
    private static Sprite DrawSprite(int size, System.Func<float, float, Color> pixel)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                texture.SetPixel(x, y, pixel(u, v));
            }
        }
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
