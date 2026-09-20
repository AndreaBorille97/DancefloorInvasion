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

    private Rigidbody rb;
    private Vector3 moveDirection; // direzione di movimento letta dallo stick sinistro (aggiornata ogni frame)
    private Vector3 aimDirection;  // direzione di mira/rotazione letta dallo stick destro (mantiene l'ultimo valore valido)
    private Vector3 pendingDash;   // offset accodato da Dash(), consumato dal prossimo Move()
    // Onda d'urto dello scatto: basta aggiungere il componente Air Shot VFX al Player (o a un
    // suo figlio) e viene agganciato da solo, niente da collegare a mano nell'Inspector.
    // Resta null finché il componente non c'è: in quel caso lo scatto funziona come prima,
    // senza effetto.
    private AirShotVFX dashVfx;

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

        dashVfx = GetComponentInChildren<AirShotVFX>();
    }

    void Update()
    {
        // L'input va letto in Update (viene campionato una volta per frame),
        // mentre il movimento fisico va applicato in FixedUpdate.
        ReadInput();
    }

    void FixedUpdate()
    {
        // FixedUpdate è il posto corretto per muovere un Rigidbody:
        // gira a intervalli fissi, sincronizzati con il motore fisico.
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
        bool isDashing = pendingDash.sqrMagnitude > 0.0001f;
        Vector3 delta = moveDirection * moveSpeed * Time.fixedDeltaTime + pendingDash;
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
            MoveDashThroughEnemies(delta);
        }
        else
        {
            rb.MovePosition(rb.position + ClampToObstacles(delta));
        }
    }

    // Chiamato da PlayerSpeedAmmo quando si consuma una munizione Speed: accoda uno scatto di
    // "distance" unità nella direzione in cui il Player si sta muovendo (l'ultimo moveDirection
    // letto dallo stick sinistro); se il Player è fermo, usa la direzione verso cui è rivolto
    // (transform.forward) come fallback. Viene applicato dal prossimo Move(), non subito qui,
    // per non entrare in conflitto con la sua chiamata a MovePosition (vedi commento in Move()).
    public void Dash(float distance)
    {
        Vector3 direction = moveDirection.sqrMagnitude > 0.0001f ? moveDirection.normalized : transform.forward;
        pendingDash += direction * distance;

        // Fire(punto) invece del semplice Fire(): quest'ultimo punterebbe verso
        // transform.forward, cioè la direzione di MIRA (stick destro), mentre lo scatto va
        // nella direzione di MOVIMENTO (stick sinistro). Passando il punto di arrivo, l'onda
        // segue anche la lunghezza reale dello scatto invece del "range" del componente.
        if (dashVfx != null)
        {
            dashVfx.Fire(transform.position + direction * distance);
        }
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
            bool isIdroSbirro = enemyHealth != null && enemyHealth.GetComponent<IdroSbirroHoseAttack>() != null;

            if (enemyHealth != null && !isIdroSbirro)
            {
                rb.position += direction * hit.distance;
                remainingDistance -= hit.distance;

                // Disabilitato subito (non solo Destroy, che è rimandato a fine frame): senza
                // questo, il prossimo SweepTest dello stesso scatto lo ricolpirebbe di nuovo a
                // distanza ~0 e resterebbe bloccato lì invece di proseguire attraverso di lui.
                hit.collider.enabled = false;
                enemyHealth.TakeDamage(float.MaxValue, transform.position);
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
