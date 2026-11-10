using System.Collections.Generic;
using AirShot;
using UnityEngine;
using UnityEngine.InputSystem;

// Richiede un Rigidbody sullo stesso GameObject: il movimento e la rotazione
// vengono applicati tramite fisica (MovePosition/MoveRotation) invece che
// spostando direttamente il Transform.
[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement (left stick)")]
    [SerializeField] private float moveSpeed = 6f; // velocità di spostamento (unità al secondo)

    [Header("Aiming (right stick)")]
    [SerializeField] private float rotationSpeed = 15f; // quanto velocemente il player ruota verso la direzione di mira

    [Header("Sticks")]
    [SerializeField] private float stickDeadzone = 0.2f; // soglia sotto la quale l'input dello stick viene ignorato (evita drift)

    [Header("Ostacoli")]
    [Tooltip("Margine di sicurezza mantenuto dagli ostacoli, per non restare incastrati esattamente a contatto con la loro superficie.")]
    [SerializeField] private float obstacleSkin = 0.05f;

    [Header("Scatto in berserk")]
    [Tooltip("Larghezza della fascia in cui gli Sbirro muoiono sul colpo, rispetto allo scatto normale " +
             "(che uccide solo quelli che il corpo del Player attraversa): 2 = larga il doppio.")]
    [SerializeField] private float berserkKillWidthMultiplier = 2f;
    [Tooltip("Tremolio dello schermo allo scatto berserk (0..1, vedi CameraShake).")]
    [Range(0f, 1f)]
    [SerializeField] private float berserkDashShake = 0.7f;

    private Rigidbody rb;
    private Collider body;
    private float bodyRadius; // mezza larghezza del Player in pianta, misurata dal suo collider
    private bool pendingBerserkDash;
    private Vector3 moveDirection; // direzione di movimento letta dallo stick sinistro (aggiornata ogni frame)
    private Vector3 aimDirection;  // direzione di mira/rotazione letta dallo stick destro (mantiene l'ultimo valore valido)
    private Vector3 pendingDash;   // offset accodato da Dash(), consumato dal prossimo Move()
    private Vector3 lastFixedPosition; // per Velocity
    // Rallentamento temporaneo (vedi ApplySlow): fattore sulla camminata e fino a quando dura.
    private float slowMultiplier = 1f;
    private float slowUntil;
    // Accelerazione temporanea (vedi ApplySpeedBoost): fattore sulla camminata e fino a quando
    // dura. speedAuraOn = l'aura oro (CharacterAura.SpeedBoost) è accesa.
    private float boostMultiplier = 1f;
    private float boostUntil;
    private bool speedAuraOn;
    // Fermo sul posto fino a questo istante (vedi Root): niente camminata né scatto.
    private float rootedUntil;
    // Onda d'urto dello scatto: basta aggiungere il componente Air Shot VFX al Player (o a un
    // suo figlio) e viene agganciato da solo, niente da collegare a mano nell'Inspector.
    // Resta null finché il componente non c'è: in quel caso lo scatto funziona come prima,
    // senza effetto.
    private AirShotVFX dashVfx;
    // Lo stesso effetto, rosso e più violento, per lo scatto in berserk (vedi CreateBerserkDashVfx).
    private AirShotVFX berserkDashVfx;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate; // ammorbidisce il movimento tra un FixedUpdate e l'altro, eliminando lo sfarfallio visivo

        // Kinematic: il player viene mosso solo da MovePosition/MoveRotation, mai dalla
        // fisica. Con tantissimi nemici che gli sbattono contro, un Rigidbody dinamico
        // riceverebbe velocità dagli urti e continuerebbe a scivolare per inerzia anche
        // a stick fermo; da kinematic non riceve mai quella spinta (ma continua a
        // bloccare/collidere con i nemici, che invece restano dinamici).
        rb.isKinematic = true;

        aimDirection = transform.forward; // direzione iniziale = quella verso cui il player guarda in scena
        lastFixedPosition = rb.position;

        dashVfx = GetComponentInChildren<AirShotVFX>();
        berserkDashVfx = CreateBerserkDashVfx();

        body = GetComponent<Collider>();
        bodyRadius = body != null ? Mathf.Max(body.bounds.extents.x, body.bounds.extents.z) : 0.5f;
    }

    void Update()
    {
        // L'input va letto in Update (viene campionato una volta per frame),
        // mentre il movimento fisico va applicato in FixedUpdate.
        ReadInput();

        // Aura e patina oro restano accese finché dura l'accelerazione: si spengono qui allo scadere.
        if (IsSpeedBoosted != speedAuraOn)
        {
            speedAuraOn = IsSpeedBoosted;
            CharacterAura.Set(gameObject, CharacterAura.Style.SpeedBoost, speedAuraOn);
            MetallicPatina.Set(gameObject, speedAuraOn);
        }
    }

    void FixedUpdate()
    {
        // FixedUpdate è il posto corretto per muovere un Rigidbody:
        // gira a intervalli fissi, sincronizzati con il motore fisico.
        Velocity = (rb.position - lastFixedPosition) / Time.fixedDeltaTime;
        Velocity = new Vector3(Velocity.x, 0f, Velocity.z);
        lastFixedPosition = rb.position;

        Move();
        Rotate();
    }

    private void ReadInput()
    {
        // Gamepad.current è il pad attualmente "attivo" (l'ultimo usato).
        // Se non c'è nessun pad collegato, azzeriamo il movimento e usciamo.
        Gamepad pad = Gamepad.current;
        if (pad == null)
        {
            moveDirection = Vector3.zero;
            return;
        }

        // Stick sinistro -> movimento. ReadValue() restituisce un Vector2 (x = sinistra/destra, y = avanti/indietro).
        // Applichiamo la deadzone per ignorare piccoli input residui quando lo stick è a riposo.
        Vector2 move = ApplyDeadzone(pad.leftStick.ReadValue(), stickDeadzone);
        // Convertiamo il Vector2 in un Vector3 sul piano orizzontale (X/Z), lasciando Y a 0 (nessun movimento verticale).
        moveDirection = new Vector3(move.x, 0f, move.y);

        // Stick destro -> direzione di mira, indipendente dal movimento (tipico dei twin-stick shooter).
        Vector2 aim = ApplyDeadzone(pad.rightStick.ReadValue(), stickDeadzone);
        if (aim.sqrMagnitude > 0f)
        {
            // Aggiorniamo la direzione di mira solo se lo stick destro è effettivamente inclinato;
            // altrimenti manteniamo l'ultima direzione valida (il player non "scatta" al centro quando si rilascia lo stick).
            aimDirection = new Vector3(aim.x, 0f, aim.y).normalized;
        }
    }

    private void Move()
    {
        // Sposta il Rigidbody nella direzione di movimento, scalata per velocità e per il tempo del fixed step
        // (Time.fixedDeltaTime rende il movimento indipendente dal framerate), più l'eventuale
        // scatto accodato da Dash(): sommarli in un'unica chiamata evita che le due si
        // accavallino sullo stesso step fisico, dove vincerebbe solo l'ultima
        // (Rigidbody.MovePosition non è cumulativo tra chiamate diverse nello stesso step).
        bool berserkDash = pendingBerserkDash;
        pendingBerserkDash = false;

        if (IsRooted)
        {
            pendingDash = Vector3.zero;
            return;
        }

        bool isDashing = pendingDash.sqrMagnitude > 0.0001f;
        float speed = Time.time < slowUntil ? moveSpeed * slowMultiplier : moveSpeed;
        if (IsSpeedBoosted)
        {
            speed *= boostMultiplier;
        }
        Vector3 delta = moveDirection * speed * Time.fixedDeltaTime + pendingDash;
        pendingDash = Vector3.zero;

        if (delta.sqrMagnitude < 0.0000001f)
        {
            return;
        }

        // Durante lo scatto vero e proprio, gli Sbirro normali incontrati muoiono sul colpo
        // invece di fermarlo (vedi MoveDashThroughEnemies); il movimento normale (a piedi)
        // resta invece bloccato da tutto, Sbirro compresi, come sempre.
        if (isDashing)
        {
            Vector3 start = rb.position;
            // Prima di muoversi: la spazzata parte dalla posizione di partenza (vedi CollectAlongDash).
            RaycastHit[] pickupHits = rb.SweepTestAll(delta.normalized, delta.magnitude, QueryTriggerInteraction.Collide);
            MoveDashThroughEnemies(delta);
            if (berserkDash)
            {
                KillAlongBerserkDash(start, rb.position);
            }
            CollectAlongDash(pickupHits, Vector3.Distance(start, rb.position));
        }
        else
        {
            rb.MovePosition(rb.position + ClampToObstacles(delta));
        }
    }

    // Rallenta la camminata (non lo scatto) a multiplier volte la velocità normale per duration
    // secondi: usato dalla nube dello Sbirro3 (vedi SbirroCloud). Un nuovo rallentamento mentre
    // uno è già in corso non si somma: vale il più forte, e dura fino al più lontano dei due.
    public void ApplySlow(float multiplier, float duration)
    {
        bool isSlowed = Time.time < slowUntil;
        slowMultiplier = isSlowed ? Mathf.Min(slowMultiplier, multiplier) : multiplier;
        slowUntil = Mathf.Max(slowUntil, Time.time + duration);
    }

    // Accelera la camminata (non lo scatto) a multiplier volte la velocità normale per duration
    // secondi: usato dall'ammo anfetamina (SpeedBoostAmmoPickup). Raccogliendone un'altra mentre
    // è già attiva il tempo riparte da capo (non si somma a quello rimasto), come il SoundSystem.
    // Si combina con un rallentamento (ApplySlow): i due fattori si moltiplicano.
    public void ApplySpeedBoost(float multiplier, float duration)
    {
        boostMultiplier = IsSpeedBoosted ? Mathf.Max(boostMultiplier, multiplier) : multiplier;
        boostUntil = Time.time + duration;
    }

    public bool IsSpeedBoosted => Time.time < boostUntil;

    // Pianta il Player sul posto per duration secondi: non cammina e non scatta, ma continua a
    // girarsi verso lo stick destro. Usato dall'Hoffman (PlayerHoffman) mentre il frattale è aperto.
    public void Root(float duration)
    {
        rootedUntil = Mathf.Max(rootedUntil, Time.time + duration);
    }

    public bool IsRooted => Time.time < rootedUntil;

    // Spostamento orizzontale vero dell'ultimo passo fisico (muri, Hoffman e scatto compresi),
    // in unità al secondo. Misurato qui e non per fotogramma: il Player si muove solo nei passi
    // fisici, e misurato a ogni fotogramma oscillerebbe (fotogrammi con e senza passo fisico),
    // facendo andare a scatti la corsa del modello (TwinStickLocomotion).
    public Vector3 Velocity { get; private set; }

    // Dove va lo scatto: verso lo stick sinistro, o davanti se il Player è fermo.
    public Vector3 DashDirection => moveDirection.sqrMagnitude > 0.0001f ? moveDirection.normalized : transform.forward;

    // Chiamato da PlayerDash quando si scatta (R1): accoda uno scatto di
    // "distance" unità nella direzione in cui il Player si sta muovendo (l'ultimo moveDirection
    // letto dallo stick sinistro); se il Player è fermo, usa la direzione verso cui è rivolto
    // (transform.forward) come fallback. Viene applicato dal prossimo Move(), non subito qui,
    // per non entrare in conflitto con la sua chiamata a MovePosition (vedi commento in Move()).
    // In berserk (berserk = true, lo decide PlayerDash) parte l'effetto rosso invece di quello
    // normale, lo schermo trema e la fascia di uccisione si allarga (vedi KillAlongBerserkDash).
    public void Dash(float distance, bool berserk = false)
    {
        Dash(distance, berserk, DashDirection);
    }

    // Come sopra, ma in una direzione data.
    public void Dash(float distance, bool berserk, Vector3 direction)
    {
        direction.y = 0f;
        direction.Normalize();
        pendingDash += direction * distance;
        pendingBerserkDash |= berserk;

        // Fire(punto) invece del semplice Fire(): quest'ultimo punterebbe verso
        // transform.forward, cioè la direzione di MIRA (stick destro), mentre lo scatto va
        // nella direzione di MOVIMENTO (stick sinistro). Passando il punto di arrivo, l'onda
        // segue anche la lunghezza reale dello scatto invece del "range" del componente.
        Vector3 end = transform.position + direction * distance;
        if (berserk)
        {
            berserkDashVfx.Fire(end);
            CameraShake.Add(berserkDashShake);
        }
        else if (dashVfx != null)
        {
            dashVfx.Fire(end);
        }
    }

    // L'effetto dello scatto normale (Air Shot VFX) ricolorato di rosso e spinto: fulmine più
    // grosso, più onde d'urto, più scintille e più veloci. Copia i valori di quello normale (se
    // c'è) e ci mette sopra i suoi. Nasce su un oggetto spento perché AirShotVFX legge i colori
    // in Awake: va acceso solo dopo averli impostati.
    private AirShotVFX CreateBerserkDashVfx()
    {
        GameObject holder = new GameObject("BerserkDashVFX");
        holder.SetActive(false);
        holder.transform.SetParent(dashVfx != null ? dashVfx.transform : transform, false);

        AirShotVFX vfx = holder.AddComponent<AirShotVFX>();
        if (dashVfx != null)
        {
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(dashVfx), vfx);
            vfx.muzzle = dashVfx.muzzle;
        }
        vfx.target = null;
        vfx.hotColor = new Color(1f, 0.85f, 0.8f, 1f);
        vfx.glowColor = new Color(1f, 0.08f, 0.05f, 1f);
        vfx.power = 1.6f;
        vfx.boltThickness = 1.7f;
        vfx.rings = 4;
        vfx.ringSize = 0.75f;
        vfx.density = 110;
        vfx.particleSize = 0.75f;
        vfx.trailSparks = 1f;
        vfx.zigzag = 1.1f;

        holder.SetActive(true);
        return vfx;
    }

    // Dopo uno scatto berserk: uccide anche gli Sbirro ai lati della traiettoria percorsa, in una
    // fascia larga berserkKillWidthMultiplier volte il corpo del Player. Quelli dritti davanti li
    // ha già uccisi MoveDashThroughEnemies (collider spento, qui non li ritrova). Usa il tratto
    // percorso davvero, non quello chiesto: se un muro o un IdroSbirro ha fermato lo scatto, non
    // uccide al di là. IdroSbirro escluso come nello scatto normale.
    private void KillAlongBerserkDash(Vector3 start, Vector3 end)
    {
        Vector3 travelled = end - start;
        float distance = travelled.magnitude;
        if (distance < 0.001f)
        {
            return;
        }

        Vector3 direction = travelled / distance;
        RaycastHit[] hits = Physics.SphereCastAll(start, bodyRadius * berserkKillWidthMultiplier, direction, distance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        HashSet<EnemyHealth> killed = new HashSet<EnemyHealth>(); // uno Sbirro con più collider va colpito una volta sola
        foreach (RaycastHit hit in hits)
        {
            EnemyHealth enemyHealth = hit.collider.GetComponentInParent<EnemyHealth>();
            if (enemyHealth == null || enemyHealth.IsBoss || !killed.Add(enemyHealth))
            {
                continue;
            }

            hit.collider.enabled = false; // stesso motivo di MoveDashThroughEnemies: non va ricolpito
            enemyHealth.TakeDashHit(direction);
        }
    }

    // Lo scatto sposta il Player di colpo: la fisica vede solo dove arriva, e i power-up passati
    // in mezzo non sentono nessun OnTriggerEnter. hits è la spazzata del collider vero del Player
    // lungo lo scatto (rb.SweepTestAll, la stessa di MoveDashThroughEnemies per gli Sbirro, fatta
    // dalla partenza: quelli già toccati lì non compaiono), travelled la strada fatta davvero:
    // oltre un muro o un IdroSbirro che ha fermato lo scatto non si raccoglie niente.
    // Ogni power-up trovato si raccoglie come se il Player l'avesse toccato: gli si manda lo
    // stesso OnTriggerEnter col collider del Player, così fa quello che fa sempre (cariche,
    // modalità facile, distruzione). Valgono solo i componenti *Pickup (tutti i raccoglibili si
    // chiamano così), non gli altri trigger dell'arena. Come per gli Sbirro, i suoi collider si
    // spengono subito: Destroy è rimandato a fine frame, e se lo scatto finisce sopra il
    // power-up la fisica gli manderebbe un secondo OnTriggerEnter (raccolta doppia).
    private void CollectAlongDash(RaycastHit[] hits, float travelled)
    {
        if (body == null)
        {
            return;
        }

        foreach (RaycastHit hit in hits)
        {
            Collider other = hit.collider;
            if (other == null || !other.enabled || !other.isTrigger || hit.distance > travelled + obstacleSkin)
            {
                continue;
            }

            GameObject pickup = FindPickup(other);
            if (pickup == null)
            {
                continue;
            }

            pickup.SendMessage("OnTriggerEnter", body, SendMessageOptions.DontRequireReceiver);
            foreach (Collider pickupCollider in pickup.GetComponentsInChildren<Collider>())
            {
                pickupCollider.enabled = false;
            }
        }
    }

    // L'oggetto col componente *Pickup a cui appartiene questo collider (lui o un suo genitore).
    private static GameObject FindPickup(Collider collider)
    {
        for (Transform t = collider.transform; t != null; t = t.parent)
        {
            foreach (MonoBehaviour behaviour in t.GetComponents<MonoBehaviour>())
            {
                if (behaviour != null && behaviour.GetType().Name.EndsWith("Pickup"))
                {
                    return t.gameObject;
                }
            }
        }

        return null;
    }

    // Essendo kinematic, questo Rigidbody non viene mai fermato dalla fisica contro nessun
    // collider (statico o dinamico che sia) — SweepTest verifica manualmente cosa
    // incontrerebbe lungo la traiettoria, senza spostarlo, così possiamo accorciare il
    // movimento invece di attraversare gli ostacoli. Condiviso da Move() e Dash().
    private Vector3 ClampToObstacles(Vector3 delta)
    {
        float distance = delta.magnitude;
        if (rb.SweepTest(delta.normalized, out RaycastHit hit, distance, QueryTriggerInteraction.Ignore))
        {
            distance = Mathf.Max(0f, hit.distance - obstacleSkin);
            delta = delta.normalized * distance;
        }

        return delta;
    }

    // Come ClampToObstacles, ma usata solo durante lo scatto vero e proprio: ogni Sbirro
    // normale (EnemyHealth) incontrato lungo la traiettoria muore sul colpo e lo scatto
    // prosegue dritto attraverso di lui, potenzialmente uccidendone più d'uno in fila.
    // L'IdroSbirro fa eccezione ed è escluso dall'insta-kill (ha troppa vita per un "colpo
    // secco", va affrontato con le armi normali): per lui, come per un muro o qualunque altro
    // ostacolo, lo scatto si ferma esattamente come ClampToObstacles.
    // Muove rb.position passo dopo passo invece di usare MovePosition (che non è cumulativo
    // tra chiamate diverse nello stesso step, vedi Move()): essendo kinematic è sicuro
    // spostarlo così più volte di seguito nello stesso FixedUpdate, nessun evento fisico
    // intermedio nel frattempo, il render vede solo la posizione finale.
    private void MoveDashThroughEnemies(Vector3 delta)
    {
        float remainingDistance = delta.magnitude;
        Vector3 direction = delta.normalized;

        while (remainingDistance > 0f)
        {
            if (!rb.SweepTest(direction, out RaycastHit hit, remainingDistance, QueryTriggerInteraction.Ignore))
            {
                rb.position += direction * remainingDistance;
                return;
            }

            EnemyHealth enemyHealth = hit.collider.GetComponentInParent<EnemyHealth>();
            // I boss (IdroSbirro, CyberGiorgia) fermano lo scatto come un muro.
            if (enemyHealth != null && !enemyHealth.IsBoss)
            {
                rb.position += direction * hit.distance;
                remainingDistance -= hit.distance;

                // Disabilitato subito (non solo Destroy, che è rimandato a fine frame): senza
                // questo, il prossimo SweepTest dello stesso scatto lo ricolpirebbe di nuovo a
                // distanza ~0 e resterebbe bloccato lì invece di proseguire attraverso di lui.
                hit.collider.enabled = false;

                // TakeDashHit e non TakeDamage: è quello che fa tornare indietro lo schizzo
                // rosso, verso il punto da cui lo scatto è partito invece che in avanti (vedi
                // il suo commento). Gli passiamo la direzione dello scatto perché la posizione
                // del Player, qui, è già quella dello Sbirro appena attraversato.
                enemyHealth.TakeDashHit(direction);
                continue;
            }

            rb.position += direction * Mathf.Max(0f, hit.distance - obstacleSkin);
            return;
        }
    }

    private void Rotate()
    {
        // Se non abbiamo mai ricevuto una direzione di mira valida, non ruotare.
        if (aimDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        // Calcola la rotazione che fa guardare il player verso aimDirection (con "su" = Vector3.up).
        Quaternion targetRotation = Quaternion.LookRotation(aimDirection, Vector3.up);
        // Ruota gradualmente verso la rotazione target invece di scattare istantaneamente,
        // per un turning più fluido; rotationSpeed regola quanto rapida è l'interpolazione.
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime));
    }

    private static Vector2 ApplyDeadzone(Vector2 value, float deadzone)
    {
        // Se il vettore è più corto della deadzone, lo consideriamo "fermo" (Vector2.zero);
        // usiamo sqrMagnitude invece di magnitude per evitare una radice quadrata inutile.
        return value.sqrMagnitude < deadzone * deadzone ? Vector2.zero : value;
    }
}
