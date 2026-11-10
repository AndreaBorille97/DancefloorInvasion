using UnityEngine;

// Modalità berserk: parte da sola appena il Player raccoglie un'ammo berserk
// (BerserkAmmoPickup), niente cariche da spendere. Per activeSeconds secondi:
// - le onde usano gli anelli rossi berserk (più larghi, quindi colpiscono una zona più ampia)
//   e fanno più danno (SoundWaveProjectile.MakeBerserk, chiamato da PlayerShooting);
// - i nemici colpiti arretrano di più (HitKnockbackFeedback legge KnockbackMultiplier);
// - schermo e controller tremano ad ogni colpo a segno su un nemico e molto di più ad ogni
//   nemico ucciso, con un micro fermo-immagine (hit-stop) sull'uccisione per dare peso al colpo;
// - un alone rosso ai bordi dello schermo, che compare di colpo, pulsa ad ogni onda e alla fine
//   sparisce in dissolvenza.
// Un'altra ammo raccolta mentre è già attiva fa ripartire il tempo da capo.
//
// Insieme alla serotonina (PlayerSerotonin) si sommano: le onde serotonina prendono il danno
// berserk e partono insieme agli anelli rossi (SoundWaveProjectile.CreateRingVisuals), ogni
// Sbirro convertito fa tremare e fermare lo schermo come un'uccisione, e i convertiti picchiano
// più forte finché il berserk dura (BrawlDamageMultiplier).
//
// Si aggiunge da solo al Player all'avvio, come PlayerKhole.
public class PlayerBerserk : MonoBehaviour
{
    [Header("Durata")]
    [SerializeField] private float activeSeconds = 10f;

    [Header("Onde")]
    [Tooltip("Moltiplicatore del danno delle onde in berserk.")]
    [SerializeField] private float damageMultiplier = 1.6f;
    [Tooltip("Moltiplicatore di quanto arretrano i nemici colpiti in berserk.")]
    [SerializeField] private float knockbackMultiplier = 1.8f;

    [Header("Tremolio schermo (0..1, vedi CameraShake)")]
    [Tooltip("Ad ogni nemico colpito. Più nemici presi dalla stessa onda si sommano.")]
    [SerializeField] private float shakePerHit = 0.3f;
    [SerializeField] private float shakePerKill = 0.55f;

    [Header("Vibrazione controller (motori 0..1)")]
    [SerializeField] private Vector2 rumblePerHit = new Vector2(0.1f, 0.2f); // x = motore grave, y = motore acuto
    [SerializeField] private float rumbleHitSeconds = 0.07f;
    [SerializeField] private Vector2 rumblePerKill = new Vector2(0.3f, 0.45f);
    [SerializeField] private float rumbleKillSeconds = 0.15f;

    [Header("Hit-stop sulle uccisioni")]
    [Tooltip("Per quanti secondi (reali) il gioco quasi si ferma quando un nemico muore. 0 = niente.")]
    [SerializeField] private float hitStopSeconds = 0.035f;
    [Tooltip("Velocità del gioco durante l'hit-stop.")]
    [SerializeField] private float hitStopTimeScale = 0.05f;

    [Header("Alone rosso ai bordi dello schermo")]
    [SerializeField] private Color vignetteColor = new Color(1f, 0.05f, 0.03f, 1f);
    [Tooltip("Opacità dell'alone a modalità attiva.")]
    [Range(0f, 1f)] [SerializeField] private float vignetteAlpha = 0.4f;
    [Tooltip("Quanto si accende in più ad ogni onda sparata, prima di tornare normale.")]
    [Range(0f, 1f)] [SerializeField] private float vignettePulse = 0.25f;
    [Tooltip("Da dove parte l'alone: 0 = dal centro dello schermo, 1 = solo sui bordi estremi.")]
    [Range(0f, 1f)] [SerializeField] private float vignetteInner = 0.55f;
    [Tooltip("Velocità con cui l'impulso di ogni onda si spegne (al secondo).")]
    [SerializeField] private float vignetteFadeSpeed = 4f;
    [Tooltip("In quanti secondi l'alone sparisce a fine modalità. All'inizio invece compare di colpo.")]
    [SerializeField] private float vignetteFadeOutSeconds = 1.2f;

    [Header("LED sulla fronte del Player")]
    [Tooltip("Colore (HDR) del LED rosso al centro della testa a modalità attiva. Si accende e batte con l'alone: " +
             "la zona che si illumina è la maschera PLAYER_led_mask, mappa di emissione del materiale del modello.")]
    [ColorUsage(false, true)]
    [SerializeField] private Color ledColor = new Color(1f, 0.04f, 0.02f) * 6f;

    private float activeTimer;
    private float hitStopUntil = -1f;
    private bool inHitStop;

    private Vector2 rumble;        // motori attualmente accesi
    private float rumbleUntil = -1f;

    private UnityEngine.UI.RawImage vignette;
    private float vignetteShown; // opacità attuale, insegue quella voluta
    private float pulse;

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
    private Renderer[] ledRenderers; // le mesh del modello con la maschera del LED
    private MaterialPropertyBlock ledBlock;
    private bool ledLit;

    public bool IsActive => activeTimer > 0f;
    public float DamageMultiplier => damageMultiplier;

    // Letto da HitKnockbackFeedback, che non sa chi ha colpito: c'è un solo Player.
    public static float KnockbackMultiplier { get; private set; } = 1f;

    // Letto da SerotoninBrawl: col berserk attivo anche gli Sbirro convertiti picchiano più forte.
    public static float BrawlDamageMultiplier => instance != null && instance.IsActive ? instance.damageMultiplier : 1f;
    private static PlayerBerserk instance;

    void Awake()
    {
        instance = this;
        ledRenderers = System.Array.FindAll(GetComponentsInChildren<SkinnedMeshRenderer>(true),
            r => r.sharedMaterial != null && r.sharedMaterial.HasProperty(EmissionMapId)
                 && r.sharedMaterial.GetTexture(EmissionMapId) != null);

        // Senza la keyword _EMISSION lo shader Lit non calcola l'emissione: la maschera c'è, il
        // colore arriva dal property block, ma il LED resta spento. È successo con PLAYER_mat
        // (keyword persa, ora rimessa nell'asset); qui la si riaccende per sicurezza. In build
        // conta quella salvata nell'asset: è lei a far includere la variante dello shader.
        foreach (Renderer led in ledRenderers)
        {
            if (!led.sharedMaterial.IsKeywordEnabled("_EMISSION"))
            {
                led.sharedMaterial.EnableKeyword("_EMISSION");
            }
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToPlayer()
    {
        KnockbackMultiplier = 1f;
        PlayerMovement player = FindAnyObjectByType<PlayerMovement>();
        if (player != null && player.GetComponent<PlayerBerserk>() == null)
        {
            player.gameObject.AddComponent<PlayerBerserk>();
        }
    }

    void OnEnable()
    {
        HitEvents.EnemyHit += OnEnemyHit;
        HitEvents.EnemyKilled += OnEnemyKilled;
        HitEvents.EnemyConverted += OnEnemyKilled; // con la serotonina, convertire vale come uccidere
    }

    void OnDisable()
    {
        HitEvents.EnemyHit -= OnEnemyHit;
        HitEvents.EnemyKilled -= OnEnemyKilled;
        HitEvents.EnemyConverted -= OnEnemyKilled;
        KnockbackMultiplier = 1f;
        EndHitStop();
        StopRumble();
        UpdateLed(0f);
        if (vignette != null)
        {
            vignette.enabled = false;
        }
    }

    void OnDestroy()
    {
        if (vignette != null)
        {
            Destroy(vignette.canvas.gameObject);
        }
    }

    void Update()
    {
        if (IsActive)
        {
            activeTimer -= Time.deltaTime;
            if (!IsActive)
            {
                KnockbackMultiplier = 1f;
            }
        }

        if (inHitStop && Time.unscaledTime >= hitStopUntil)
        {
            EndHitStop();
        }

        if (rumbleUntil >= 0f && Time.unscaledTime >= rumbleUntil)
        {
            StopRumble();
        }

        UpdateVignette();
    }

    // Tempo non scalato: l'alone non si blocca durante l'hit-stop.
    private void UpdateVignette()
    {
        float dt = Time.unscaledDeltaTime;
        pulse = Mathf.MoveTowards(pulse, 0f, vignetteFadeSpeed * dt);
        if (IsActive)
        {
            // Di colpo: nessuna rampa all'inizio, e l'impulso delle onde si vede subito.
            vignetteShown = Mathf.Clamp01(vignetteAlpha + pulse);
        }
        else
        {
            float fadeSpeed = vignetteAlpha / Mathf.Max(vignetteFadeOutSeconds, 0.01f);
            vignetteShown = Mathf.MoveTowards(vignetteShown, 0f, fadeSpeed * dt);
        }

        // Il LED segue l'alone: pieno a modalità attiva, più forte ad ogni onda, si spegne in dissolvenza.
        UpdateLed(vignetteShown / Mathf.Max(vignetteAlpha, 0.01f));

        if (vignetteShown <= 0f && vignette == null)
        {
            return; // mai servito finora: non lo si costruisce nemmeno
        }

        if (vignette == null)
        {
            vignette = BuildVignette();
        }

        Color c = vignetteColor;
        c.a = vignetteShown;
        vignette.color = c;
        vignette.enabled = vignetteShown > 0f;
    }

    // Il materiale del modello ha già l'emissione accesa (nera) con la maschera del LED come
    // mappa: qui si cambia solo il colore, con un MaterialPropertyBlock per non creare copie del
    // materiale. A LED spento il blocco si toglie, e il renderer torna come prima.
    private void UpdateLed(float brightness)
    {
        if (ledRenderers == null || ledRenderers.Length == 0)
        {
            return;
        }

        bool lit = brightness > 0f;
        if (!lit && !ledLit)
        {
            return;
        }
        ledLit = lit;

        if (ledBlock == null)
        {
            ledBlock = new MaterialPropertyBlock();
        }
        foreach (Renderer led in ledRenderers)
        {
            if (led == null)
            {
                continue;
            }
            if (!lit)
            {
                led.SetPropertyBlock(null);
                continue;
            }
            led.GetPropertyBlock(ledBlock);
            ledBlock.SetColor(EmissionColorId, ledColor * brightness);
            led.SetPropertyBlock(ledBlock);
        }
    }

    // Canvas a tutto schermo con un'immagine generata qui: trasparente al centro, piena sui
    // bordi. Niente post-processing né asset da collegare. Sta sotto l'HUD (sortingOrder basso)
    // e non blocca i click.
    private UnityEngine.UI.RawImage BuildVignette()
    {
        GameObject canvasObject = new GameObject("BerserkVignette", typeof(Canvas));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -10;

        GameObject imageObject = new GameObject("Alone", typeof(RectTransform), typeof(UnityEngine.UI.RawImage));
        imageObject.transform.SetParent(canvasObject.transform, false);
        RectTransform rect = (RectTransform)imageObject.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;

        UnityEngine.UI.RawImage image = imageObject.GetComponent<UnityEngine.UI.RawImage>();
        image.texture = BuildVignetteTexture(128, vignetteInner);
        image.raycastTarget = false;
        return image;
    }

    // Bianco con alpha che cresce dal centro verso i bordi (il colore lo dà la RawImage). La
    // distanza è misurata sul quadrato, che poi viene stirato sullo schermo: l'alone segue la
    // forma dello schermo invece di essere un cerchio.
    private static Texture2D BuildVignetteTexture(int size, float inner)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            name = "BerserkVignette",
        };

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v) / Mathf.Sqrt(2f); // 0 al centro, 1 negli angoli
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner * 0.75f, 1f, d));
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    // Chiamato dall'ammo berserk raccolta.
    public void Activate()
    {
        activeTimer = activeSeconds;
        KnockbackMultiplier = knockbackMultiplier;
    }

    // Chiamato da PlayerShooting ad ogni onda (vera) sparata in berserk.
    public void OnShot()
    {
        pulse = vignettePulse; // l'alone batte a tempo con le onde
    }

    private void OnEnemyHit(GameObject enemy, Vector3 origin, Vector3 direction)
    {
        if (!IsActive)
        {
            return;
        }

        CameraShake.Add(shakePerHit);
        Rumble(rumblePerHit, rumbleHitSeconds);
    }

    private void OnEnemyKilled(GameObject enemy)
    {
        if (!IsActive)
        {
            return;
        }

        CameraShake.Add(shakePerKill);
        Rumble(rumblePerKill, rumbleKillSeconds);

        // Non toccare il tempo se il gioco è già fermo (sconfitta, vittoria: timeScale 0).
        if (hitStopSeconds > 0f && Time.timeScale > 0f)
        {
            hitStopUntil = Time.unscaledTime + hitStopSeconds;
            inHitStop = true;
            Time.timeScale = hitStopTimeScale;
        }
    }

    // Un impulso più debole non spegne né accorcia uno più forte ancora in corso (es. il colpo
    // che arriva subito dopo un'uccisione): vale il massimo, fino al più lontano dei due.
    private void Rumble(Vector2 motors, float seconds)
    {
        UnityEngine.InputSystem.Gamepad pad = UnityEngine.InputSystem.Gamepad.current;
        if (pad == null)
        {
            return;
        }

        bool running = rumbleUntil >= 0f;
        rumble = running ? Vector2.Max(rumble, motors) : motors;
        rumbleUntil = Mathf.Max(running ? rumbleUntil : 0f, Time.unscaledTime + seconds);
        pad.SetMotorSpeeds(rumble.x, rumble.y);
    }

    private void StopRumble()
    {
        if (rumbleUntil < 0f)
        {
            return;
        }

        rumbleUntil = -1f;
        rumble = Vector2.zero;
        UnityEngine.InputSystem.Gamepad.current?.SetMotorSpeeds(0f, 0f);
    }

    private void EndHitStop()
    {
        if (!inHitStop)
        {
            return;
        }

        inHitStop = false;
        // Solo se il tempo è ancora quello messo da noi: se nel frattempo qualcuno ha fermato il
        // gioco (schermata di sconfitta) resta fermo.
        if (Mathf.Approximately(Time.timeScale, hitStopTimeScale))
        {
            Time.timeScale = 1f;
        }
    }
}
