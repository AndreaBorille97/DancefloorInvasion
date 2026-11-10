using System.Collections;
using UnityEngine;

// Da mettere sul prefab del Raver (l'alleato generato raccogliendo un Raver Ammo, vedi
// RaverPickup). Viene attaccato dagli Sbirro (EnemyAttack) esattamente come il Player, e le
// onde sonore del Player (SoundWaveProjectile) gli restituiscono un po' di vita invece di
// fargli danno.
//
// Quando la vita arriva a zero il Raver non sparisce: entra in stato "a terra" (isDown),
// disabilita subito RaverAttack (non ingaggia più nessuno) e cambia colore, ma il collider
// resta attivo così l'onda sonora del Player continua a rilevarlo e può rianimarlo; la
// polizia lo ignora già da questo momento, anche mentre è ancora in cammino — sia come
// bersaglio da inseguire (EnemyChase/EnemyRangedAttack/IdroSbirro, filtrano su
// IsDown) sia come cosa da colpire a contatto (EnemyAttack e gli altri attacchi filtrano su
// CanBeAttacked, che qui vale !isDown). Se in scena c'è una ChilloutZone, invece di
// teletrasportarsi ci cammina sopra a piedi (RaverChase resta attivo, dirottato con
// SetForcedPosition verso il suo centro, stesso meccanismo usato dal Camper ma verso un
// punto fisso invece che un Transform): appena entra nella sua area (il BoxCollider della
// ChilloutZone allargato di un margine, vedi ChilloutZone.ContainsArrivalPoint e
// CheckChilloutArrival) smette subito di camminare e si ferma esattamente dov'è in quel
// momento, senza alcun teletrasporto verso un punto preciso, e comincia a curarsi
// lentamente da solo, oltre a poter essere aiutato dal cono del Player come sempre.
// Altrimenti (nessuna ChilloutZone in
// scena) si allontana subito e semplicemente dal punto di guardia come faceva in origine,
// senza cura passiva. La rianimazione richiede di riportarlo a vita piena (non basta una
// cura parziale): sotto quella soglia resta a terra.
public class RaverHealth : MonoBehaviour, IEnemyAttackTarget
{
    [Header("Vita")]
    [SerializeField] private float maxHealth = 50f;
    [Tooltip("Danno subito ad ogni attacco andato a segno di uno Sbirro.")]
    [SerializeField] private float damagePerHit = 25f;

    [Header("Stato a terra")]
    [Tooltip("Colore del Raver mentre è a terra, in attesa di essere rianimato dal cono del Player.")]
    [SerializeField] private Color downColor = new Color(0.35f, 0.35f, 0.35f, 1f);
    [Tooltip("Fallback se non c'è nessuna ChilloutZone in scena: di quanto si allontana dal punto di guardia quando va a terra, per non fare comunque da muro passivo (il collider resta solido anche se la polizia lo ignora come bersaglio).")]
    [SerializeField] private float downDisplacementDistance = 3.5f;
    [Tooltip("Vita recuperata al secondo mentre è a terra E fermo alla ChilloutZone (non durante il tragitto per arrivarci): molto lenta apposta, il Player può sempre velocizzare la rianimazione col cono.")]
    [SerializeField] private float passiveHealPerSecond = 4f;

    [Header("Evidenziazione onda")]
    [Tooltip("Colore del lampo quando il Raver viene curato da un'onda sonora: verde 'salute', come nei videogiochi.")]
    [SerializeField] private Color healHighlightColor = new Color(0.25f, 1f, 0.3f, 1f);
    [Tooltip("Durata del lampo di colore, in secondi.")]
    [SerializeField] private float healHighlightDuration = 0.2f;
    [Tooltip("Quante stelline verdi salgono dal Raver ad ogni cura.")]
    [SerializeField] private int healStarCount = 6;

    [Header("Buff di gruppo (giocoliere)")]
    [Tooltip("Colore persistente applicato mentre il buff di gruppo del giocoliere è attivo (vedi RaverJuggler/SetGroupBuffTint), finché non va a terra (in quel caso resta downColor).")]
    [SerializeField] private Color groupBuffColor = Color.red;

    // Quanti Sbirro hanno attualmente questo Raver come bersaglio (vedi EnemyChase.SetTarget,
    // che chiama Engage/Disengage ad ogni cambio di target): usato da
    // EnemyChase.FindNearestEngageableRaver per preferire, tra più Raver a portata, quello
    // libero invece di accalcarsi tutti sullo stesso.
    private int engagedCount;
    public int EngagedCount => engagedCount;

    public void Engage()
    {
        engagedCount++;
    }

    public void Disengage()
    {
        engagedCount = Mathf.Max(0, engagedCount - 1);
    }

    private float currentHealth;
    // Moltiplicatore della modalità facile sulla vita (vedi GameDifficulty): 1 in normale. La
    // rianimazione non ne risente, vedi ReviveHealth.
    private float difficultyMultiplier = 1f;
    private bool isDown;
    private bool isWalkingToChillout; // true dal momento in cui va a terra fino all'arrivo alla ChilloutZone (vedi CheckChilloutArrival): durante il tragitto niente cura passiva
    private bool isInvulnerable;
    private bool isGroupBuffTinted; // true mentre il buff di gruppo del giocoliere è attivo (vedi SetGroupBuffTint)

    private RaverChase raverChase;
    private RaverAttack raverAttack;
    private Rigidbody rb;

    // Notifica chi è interessato al momento esatto in cui questo Raver va a terra (es.
    // RaverJuggler, per interrompere subito l'esibizione del giocoliere e i suoi bonus).
    public event System.Action OnDown;

    // Il contrario di OnDown: il Raver è stato appena rianimato ed è di nuovo in piedi (es.
    // RaverJuggler, per rimandare il giocoliere al suo posto a riprendere l'esibizione).
    public event System.Action OnRevived;

    private Renderer raverRenderer;
    private MaterialPropertyBlock propertyBlock;
    private Color baseColor;
    private Coroutine highlightRoutine;

    // Usato da CamperVehicle/PlayerCamperSummon: un Raver a terra non può essere richiamato a pilotare il Camper.
    public bool IsDown => isDown;

    // IEnemyAttackTarget: un Raver sconfitto non va più toccato da nessuno, né mentre cammina
    // verso la ChilloutZone né una volta fermo lì. Il suo collider resta attivo apposta (serve
    // al cono del Player per rianimarlo), quindi senza questo chi attacca a contatto lo
    // incontrerebbe come un bersaglio qualunque — vedi il commento sull'interfaccia.
    public bool CanBeAttacked => !isDown;

    // Usato dalla minimappa (vedi MinimapUI) per mostrare quanta vita resta: riflette la
    // vita reale anche a terra, così si vede quanto manca alla rianimazione (che richiede
    // di tornare a currentHealth == ReviveHealth, vedi Heal).
    public float HealthFraction => Mathf.Clamp01(currentHealth / (isDown ? ReviveHealth : maxHealth));

    // Vita da accumulare a terra per rialzarsi: quella della modalità normale anche in facile, così
    // rianimare costa lo stesso. Il triplo della vita conta solo da in piedi (vedi Reactivate).
    private float ReviveHealth => maxHealth / difficultyMultiplier;

    // Chiamato da CamperVehicle su Mount/Dismount: mentre pilota il Camper il Raver non
    // subisce alcun danno, a prescindere da chi/cosa lo colpisce.
    public void SetInvulnerable(bool value)
    {
        isInvulnerable = value;
    }

    // Chiamato da RaverJuggler quando il buff di gruppo del giocoliere si attiva/disattiva
    // (vedi RaverJuggler.ApplyGroupBuff/RevertGroupBuff): mentre attivo, tinge il Raver di
    // groupBuffColor come indicatore visivo, a meno che non sia già a terra (dove resta
    // grigio, downColor ha sempre la priorità). Se in quel momento è in corso un lampo
    // temporaneo (cura/fendente), non tocca nulla: ci penserà il lampo stesso a rivelare il
    // colore persistente corretto quando finisce, vedi ApplyPersistentColor.
    public void SetGroupBuffTint(bool active)
    {
        isGroupBuffTinted = active;
        UpdateBuffAura();

        if (raverRenderer == null || highlightRoutine != null)
        {
            return;
        }

        ApplyPersistentColor();
    }

    void Awake()
    {
        currentHealth = maxHealth;

        // Modalità facile: triplo della vita. I Raver già in scena nascono prima della scelta dal
        // menu (vedi DifficultyMenu), quindi aspettano l'evento; quelli generati dopo la leggono subito.
        if (GameDifficulty.IsEasy)
        {
            ApplyEasyHealth();
        }
        else
        {
            GameDifficulty.Chosen += HandleDifficultyChosen;
        }

        rb = GetComponent<Rigidbody>();
        raverChase = GetComponent<RaverChase>();
        raverAttack = GetComponent<RaverAttack>();

        raverRenderer = GetComponent<Renderer>();
        if (raverRenderer != null)
        {
            propertyBlock = new MaterialPropertyBlock();
            baseColor = raverRenderer.sharedMaterial.color;
        }

        // Cerchio verde tenue sotto i piedi, come quello degli Sbirro (vedi RaverMarker).
        if (GetComponent<RaverMarker>() == null)
        {
            gameObject.AddComponent<RaverMarker>();
        }
    }

    void Update()
    {
        if (isWalkingToChillout)
        {
            CheckChilloutArrival();
        }

        // Cura passiva solo da fermo (non durante il tragitto verso la ChilloutZone, vedi
        // isWalkingToChillout): riusa Heal(), che si occupa già da sé di farlo rialzare appena
        // raggiunge la vita piena (vedi Reactivate).
        if (isDown && !isWalkingToChillout && passiveHealPerSecond > 0f)
        {
            Heal(passiveHealPerSecond * Time.deltaTime);
        }
    }

    public void TakeHit()
    {
        // Chiamato da EnemyAttack tramite IEnemyAttackTarget, stesso schema del Player.
        TakeDamage(damagePerHit);
    }

    public void TakeDamage(float amount)
    {
        if (isDown || isInvulnerable)
        {
            // Già a terra: nessun ulteriore danno, evita che la vita scenda sotto zero
            // all'infinito mentre aspetta di essere rianimato. Alla guida del Camper:
            // invulnerabile, vedi SetInvulnerable.
            return;
        }

        currentHealth -= amount;
        if (currentHealth <= 0f)
        {
            GoDown();
        }
    }

    // Chiamato dall'onda sonora del Player. Da vivo cura normalmente (capped a maxHealth).
    // Da terra invece accumula verso la rianimazione: serve riportarlo a vita piena, una
    // cura parziale non basta a farlo rialzare. Il lampo di feedback (vedi
    // HealHighlightRoutine) parte in entrambi i casi: senza, da terra, non avresti alcun modo
    // di sapere se un'onda sta davvero colpendo il Raver o lo sta mancando.
    public void Heal(float amount)
    {
        currentHealth = Mathf.Min(currentHealth + amount, maxHealth);
        Debug.Log($"[DEBUG TEMP] {name} curato di {amount} -> {currentHealth}/{maxHealth}");

        if (raverRenderer != null)
        {
            if (highlightRoutine != null)
            {
                StopCoroutine(highlightRoutine);
            }
            highlightRoutine = StartCoroutine(HealHighlightRoutine());
        }

        HealStarsBurst.Spawn(gameObject, healStarCount);

        if (isDown && currentHealth >= ReviveHealth)
        {
            Reactivate();
        }
    }

    // Va a terra: disabilita subito l'attacco e tinge il Raver di downColor, ma lascia il
    // collider attivo così l'onda sonora del Player continua a rilevarlo (vedi
    // SoundWaveProjectile.HealRaversInRange) e può rianimarlo. Se c'è una ChilloutZone in
    // scena ci cammina sopra (RaverChase resta attivo, dirottato con SetForcedPosition verso
    // il suo centro: vedi CheckChilloutArrival per il momento in cui entra nell'area e si
    // ferma davvero); altrimenti si allontana subito dal punto di guardia come faceva in origine.
    private void GoDown()
    {
        isDown = true;
        currentHealth = 0f;

        OnDown?.Invoke();

        if (raverAttack != null)
        {
            raverAttack.enabled = false;
        }

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        ApplyPersistentColor();

        if (ChilloutZone.Instance != null && raverChase != null)
        {
            isWalkingToChillout = true;
            raverChase.SetForcedPosition(ChilloutZone.Instance.transform.position);
            return;
        }

        Settle(GetFallbackDownPosition());
    }

    // Chiamato da Update() ogni frame mentre isWalkingToChillout è true: appena il Raver
    // entra nell'area della ChilloutZone (il suo BoxCollider allargato di arrivalMargin,
    // vedi ChilloutZone.ContainsArrivalPoint) smette subito di camminare e si ferma
    // esattamente dov'è in quel momento, senza alcun
    // teletrasporto verso un punto preciso: non serve più centrare un punto esatto, ed evita
    // che più Raver vicini restino a spingersi a vicenda senza mai fermarsi davvero.
    private void CheckChilloutArrival()
    {
        if (!ChilloutZone.Instance.ContainsArrivalPoint(transform.position))
        {
            return;
        }

        isWalkingToChillout = false;
        raverChase.ClearForcedTarget();
        Settle(transform.position);
    }

    // Ferma davvero il Raver nella posizione data: disabilita RaverChase e blocca il
    // Rigidbody, così nessuno lo sposta più per inerzia. Chiamata con transform.position
    // (nessun teletrasporto) quando entra nell'area della ChilloutZone, o con una posizione
    // calcolata al volo nel fallback senza ChilloutZone.
    private void Settle(Vector3 position)
    {
        if (raverChase != null)
        {
            raverChase.enabled = false;
        }

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            // L'interpolazione (impostata da RaverChase.Awake per ammorbidire il movimento
            // normale) va disattivata prima del teletrasporto: su uno spostamento con
            // FreezeAll subito dopo che blocca ogni ulteriore FixedUpdate, l'interpolazione
            // resta a metà e il modello visivo si blocca dov'era invece di seguire subito la
            // nuova posizione del Rigidbody.
            rb.interpolation = RigidbodyInterpolation.None;
            rb.position = position;

            // Con RaverChase disabilitato nessuno azzera più la velocità ad ogni FixedUpdate:
            // senza bloccare anche X/Z (oltre a rotazione e Y, già frozen da RaverChase.Awake),
            // gli urti di chi lo tocca lo spingerebbero via per inerzia. A terra deve restare
            // esattamente dov'è (nella nuova posizione).
            rb.constraints = RigidbodyConstraints.FreezeAll;
        }
    }

    // Fallback se non c'è nessuna ChilloutZone in scena: si allontana semplicemente dal punto
    // di guardia, come faceva prima di avere un vero posto dove andare.
    private Vector3 GetFallbackDownPosition()
    {
        Vector3 awayDirection = raverChase != null
            ? rb.position - raverChase.GuardPosition
            : Vector3.zero;
        awayDirection.y = 0f;
        if (awayDirection.sqrMagnitude < 0.01f)
        {
            // Praticamente in piedi sul punto di guardia: nessuna direzione sensata da cui
            // allontanarsi, ne sceglie una a caso.
            Vector2 randomOffset = Random.insideUnitCircle;
            awayDirection = new Vector3(randomOffset.x, 0f, randomOffset.y);
        }

        return rb.position + awayDirection.normalized * downDisplacementDistance;
    }

    // Chiamato da Heal() quando la vita accumulata da terra raggiunge ReviveHealth: torna a
    // fare la guardia come un Raver normale (RaverChase.Awake è già passato, guardPosition
    // resta quella originale anche riabilitando il componente). Può succedere anche mentre è
    // ancora in cammino verso la ChilloutZone (il cono del Player cura più in fretta della
    // cura passiva): in quel caso molla la destinazione forzata e riprende la guardia da dove si trova.
    private void Reactivate()
    {
        isDown = false;
        // In facile la soglia di rianimazione è sotto la vita massima: si rialza comunque a vita piena.
        currentHealth = maxHealth;

        if (isWalkingToChillout)
        {
            isWalkingToChillout = false;
            raverChase.ClearForcedTarget();
        }

        if (raverChase != null)
        {
            raverChase.enabled = true;
        }
        if (raverAttack != null)
        {
            raverAttack.enabled = true;
        }

        if (rb != null)
        {
            // Stesso vincolo impostato da RaverChase.Awake(): libera X/Z, tiene rotazione e Y frozen.
            rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
            rb.interpolation = RigidbodyInterpolation.Interpolate; // ripristinata, disattivata in GoDown per il teletrasporto
        }

        ApplyPersistentColor();

        // Per ultimo: chi ascolta trova il Raver già rimesso in piedi da guardia normale e può
        // sovrascrivere quello che gli serve (il giocoliere, per esempio, rispegne l'attacco).
        OnRevived?.Invoke();
    }

    // Lampo di colore che segnala visivamente il momento in cui l'onda sonora del Player cura
    // questo Raver (Heal viene chiamato una sola volta a onda, vedi SoundWaveProjectile.alreadyHealed).
    // Il Raver diventa verde (healHighlightColor) e al termine torna esattamente al colore
    // persistente corrente (downColor a terra, groupBuffColor se il buff del giocoliere è attivo,
    // altrimenti baseColor): altrimenti tornerebbe visivamente al colore "di base" perdendo
    // qualunque tint fosse attivo in quel momento.
    private IEnumerator HealHighlightRoutine()
    {
        Color highlighted = healHighlightColor;

        raverRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor("_BaseColor", highlighted); // URP/Lit
        propertyBlock.SetColor("_Color", highlighted); // Standard/fallback
        raverRenderer.SetPropertyBlock(propertyBlock);

        yield return new WaitForSeconds(healHighlightDuration);

        highlightRoutine = null;
        ApplyPersistentColor();
    }

    private void HandleDifficultyChosen(GameDifficulty.Mode mode)
    {
        GameDifficulty.Chosen -= HandleDifficultyChosen;
        if (mode == GameDifficulty.Mode.Easy)
        {
            ApplyEasyHealth();
        }
    }

    private void ApplyEasyHealth()
    {
        difficultyMultiplier = GameDifficulty.EasyRaverHealthMultiplier;
        MultiplyHealth(difficultyMultiplier);
    }

    void OnDestroy()
    {
        GameDifficulty.Chosen -= HandleDifficultyChosen;
    }

    // Usato da RaverDrugBuff: scala vita massima e attuale insieme, così la percentuale di
    // vita resta invariata (es. moltiplicando per 2 e poi per 0.5 si torna esattamente al valore di partenza).
    public void MultiplyHealth(float factor)
    {
        maxHealth *= factor;
        currentHealth *= factor;
    }

    // Colore persistente "sotto" a un eventuale lampo temporaneo (cura/fendente): downColor
    // se a terra (priorità massima, indipendente dal buff), altrimenti groupBuffColor se il
    // buff di gruppo del giocoliere è attivo (vedi SetGroupBuffTint), altrimenti il colore
    // originale del material.
    private Color GetPersistentColor()
    {
        if (isDown)
        {
            return downColor;
        }

        return isGroupBuffTinted ? groupBuffColor : baseColor;
    }

    // Aura rossa del boost del giocoliere (vedi CharacterAura): il tint della capsula non si vede
    // più, coi modelli animati la capsula è spenta. Solo sulle guardie in piedi: non a terra (lì
    // il boost non serve a niente) e non sul giocoliere stesso, che il boost lo dà.
    private void UpdateBuffAura()
    {
        bool isJuggler = TryGetComponent(out RaverJuggler juggler) && juggler.IsJuggler;
        CharacterAura.Set(gameObject, CharacterAura.Style.RaverBoost, isGroupBuffTinted && !isDown && !isJuggler);
    }

    // Applica subito GetPersistentColor() al Renderer: usata ogni volta che lo stato
    // persistente cambia (a terra, rianimato, tint di gruppo) o che un lampo temporaneo
    // finisce e deve rivelare il colore che c'era sotto.
    private void ApplyPersistentColor()
    {
        UpdateBuffAura();

        if (raverRenderer == null)
        {
            return;
        }

        if (!isDown && !isGroupBuffTinted)
        {
            // Nessun tint attivo: Clear() invece di riscrivere baseColor, così il Renderer
            // torna a leggere il colore del material condiviso senza un override ridondante.
            propertyBlock.Clear();
            raverRenderer.SetPropertyBlock(propertyBlock);
            return;
        }

        Color color = GetPersistentColor();
        raverRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor("_BaseColor", color); // URP/Lit
        propertyBlock.SetColor("_Color", color); // Standard/fallback
        raverRenderer.SetPropertyBlock(propertyBlock);
    }
}
