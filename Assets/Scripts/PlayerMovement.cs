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
        // scatto accodato da Dash(): sommarli in un'unica chiamata a MovePosition evita che le
        // due chiamate si accavallino sullo stesso step fisico, dove vincerebbe solo l'ultima
        // (Rigidbody.MovePosition non è cumulativo tra chiamate diverse nello stesso step).
        Vector3 delta = moveDirection * moveSpeed * Time.fixedDeltaTime + pendingDash;
        pendingDash = Vector3.zero;

        if (delta.sqrMagnitude < 0.0000001f)
        {
            return;
        }

        rb.MovePosition(rb.position + ClampToObstacles(delta));
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
