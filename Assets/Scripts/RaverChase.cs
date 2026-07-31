using UnityEngine;

// Da mettere sul prefab del Raver (posizionato a mano in scena, non più generato da uno
// spawner/pickup dedicato), insieme a RaverAttack. Comportamento da guardia difensiva, non
// da inseguitore: vaga leggermente attorno al punto in cui è stato posizionato
// (guardPosition, catturato in Awake, vedi idleWanderRadius/idleWanderInterval) invece di
// restare fermo come un blocco statico, e si attiva per caricare lo Sbirro più vicino solo
// quando questo entra entro engageRadius da quel punto, ricalcolando periodicamente; appena
// la minaccia sparisce o esce dal raggio, torna a vagare in posizione di guardia. Smette di
// avvicinarsi entro minApproachDistance dal bersaglio, restando comunque a contatto: evita
// di sfondarlo/compenetrarlo quando la velocità è alta (es. buff drug, vedi RaverDrugBuff).
[RequireComponent(typeof(Rigidbody))]
public class RaverChase : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f; // velocità di inseguimento (unità al secondo)

    [Header("Target")]
    [Tooltip("Ogni quanti secondi ricalcola lo Sbirro più vicino entro engageRadius da guardPosition.")]
    [SerializeField] private float retargetInterval = 0.5f;
    [Tooltip("Raggio di allerta attorno al punto di guardia: un Raver ingaggia solo gli Sbirro che entrano qui dentro, altrimenti resta fermo/torna in posizione. Da provare/tarare in game.")]
    [SerializeField] private float engageRadius = 8f;

    [Tooltip("Distanza minima dal bersaglio oltre la quale il Raver smette di avvicinarsi: deve restare (di poco) inferiore alla somma dei raggi dei collider, altrimenti il Raver non tocca più il bersaglio e RaverAttack (a contatto) non parte più. Tienilo vicino a quella somma (qui 1: 0.5 Raver + 0.5 Sbirro): una sovrapposizione più marcata costringe la fisica a correggerla ad ogni FixedUpdate, ed è quello scatto/stuttering che si vede a velocità elevata (es. buff drug).")]
    [SerializeField] private float minApproachDistance = 0.95f;

    [Header("Movimento a riposo")]
    [Tooltip("Raggio entro cui il Raver si sposta a caso attorno al punto di guardia quando non sta ingaggiando nessuno, per non restare fermo come un blocco statico.")]
    [SerializeField] private float idleWanderRadius = 1.5f;
    [Tooltip("Ogni quanti secondi (circa) sceglie un nuovo punto a caso attorno al punto di guardia.")]
    [SerializeField] private float idleWanderInterval = 3f;
    [Tooltip("Velocità di movimento mentre vaga a riposo: più lenta del passo da combattimento.")]
    [SerializeField] private float idleMoveSpeed = 1f;

    [Header("Modalità veicolo (Camper)")]
    [Tooltip("Quanto velocemente sterza mentre guida il Camper (gradi al secondo), invece di ruotare di scatto come normalmente: vedi SetVehicleMode.")]
    [SerializeField] private float vehicleTurnSpeed = 180f;

    private Rigidbody rb;
    private Vector3 guardPosition; // punto a cui torna quando non c'è nessuna minaccia da ingaggiare, catturato in Awake
    private Vector3 wanderTarget; // punto corrente del vagare a riposo, entro idleWanderRadius da guardPosition
    private float wanderTimer;
    private Transform target;
    private Transform forcedTarget; // se impostato, ignora l'AI normale e punta dritto qui (vedi SetForcedTarget)
    private float retargetTimer;
    private bool vehicleMode; // true mentre pilota il Camper: guida in avanti sterzando invece di puntare/ruotare di scatto verso il bersaglio

    // Usato da RaverDrugBuff per raddoppiare/ripristinare la velocità di movimento.
    public void MultiplyMoveSpeed(float factor)
    {
        moveSpeed *= factor;
    }

    // Usato da CamperVehicle per mandare questo Raver dritto al Camper, ignorando qualunque
    // Sbirro ingaggiato finché non arriva (vedi GetMoveDestination). Riusa Move()/Rotate()
    // così esistenti, compreso lo stop entro minApproachDistance.
    public void SetForcedTarget(Transform newTarget)
    {
        forcedTarget = newTarget;
    }

    // Chiamato da CamperVehicle appena il Raver sale a bordo: torna a bersagliare gli
    // Sbirro (o il Robosbirro) come al solito.
    public void ClearForcedTarget()
    {
        forcedTarget = null;
    }

    // Chiamato da CamperVehicle su Mount/Dismount: mentre è true, Move()/Rotate() si
    // comportano da veicolo (avanza lungo la propria direzione sterzando gradualmente verso
    // il bersaglio) invece che puntare dritto lì e ruotare di scatto come fa normalmente a piedi.
    public void SetVehicleMode(bool enabled)
    {
        vehicleMode = enabled;
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true; // ruotiamo noi manualmente verso il bersaglio, non vogliamo che la fisica ribalti il Raver
        rb.interpolation = RigidbodyInterpolation.Interpolate; // ammorbidisce il movimento tra un FixedUpdate e l'altro
        rb.useGravity = false;
        rb.constraints |= RigidbodyConstraints.FreezePositionY;

        guardPosition = transform.position;
        wanderTarget = guardPosition;

        AcquireNearestEnemy();
    }

    void FixedUpdate()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        retargetTimer += Time.fixedDeltaTime;
        if (retargetTimer >= retargetInterval || target == null)
        {
            retargetTimer = 0f;
            AcquireNearestEnemy();
        }

        // A riposo (nessuna minaccia da ingaggiare, nessuna destinazione forzata dal Camper):
        // vaga leggermente attorno al punto di guardia invece di restare fermo come un blocco
        // statico, e lo fa al passo lento idleMoveSpeed invece del passo da combattimento.
        bool isIdle = forcedTarget == null && target == null;
        if (isIdle)
        {
            UpdateWander();
        }

        Vector3 destination = GetMoveDestination();
        Vector3 toTarget = destination - rb.position;
        toTarget.y = 0f;

        Move(toTarget, isIdle ? idleMoveSpeed : moveSpeed);
        Rotate(toTarget);
    }

    // In ordine di priorità: destinazione forzata (il Camper da raggiungere) > Sbirro
    // ingaggiato entro engageRadius da guardPosition > punto corrente del vagare a riposo.
    private Vector3 GetMoveDestination()
    {
        if (forcedTarget != null)
        {
            return forcedTarget.position;
        }

        return target != null ? target.position : wanderTarget;
    }

    // Sceglie un nuovo punto a caso entro idleWanderRadius da guardPosition ogni
    // idleWanderInterval secondi circa: minApproachDistance in Move() ferma comunque il
    // Raver poco prima di arrivarci, quindi il risultato è uno spostarsi leggero, non un
    // vero e proprio girovagare.
    private void UpdateWander()
    {
        wanderTimer += Time.fixedDeltaTime;
        if (wanderTimer < idleWanderInterval)
        {
            return;
        }

        wanderTimer = 0f;
        Vector2 offset = Random.insideUnitCircle * idleWanderRadius;
        wanderTarget = guardPosition + new Vector3(offset.x, 0f, offset.y);
    }

    // Ricalcola lo Sbirro più vicino, ma solo se entro engageRadius da guardPosition:
    // fuori da quel raggio resta null, e GetMoveDestination riporta il Raver in posizione.
    private void AcquireNearestEnemy()
    {
        Transform nearest = null;
        float nearestSqrDistance = float.MaxValue;
        float sqrEngageRadius = engageRadius * engageRadius;

        foreach (EnemyHealth enemy in FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None))
        {
            float sqrDistance = (enemy.transform.position - guardPosition).sqrMagnitude;
            if (sqrDistance <= sqrEngageRadius && sqrDistance < nearestSqrDistance)
            {
                nearest = enemy.transform;
                nearestSqrDistance = sqrDistance;
            }
        }

        target = nearest;
    }

    private void Move(Vector3 toTarget, float speed)
    {
        if (toTarget.magnitude <= minApproachDistance)
        {
            // Già abbastanza vicino da attaccare (i collider si toccano): non avanzare oltre,
            // altrimenti a velocità elevata il Raver sfonda e compenetra il bersaglio.
            return;
        }

        // In modalità veicolo avanza lungo la direzione verso cui è già rivolto (che Rotate
        // sta sterzando gradualmente verso il bersaglio), non in linea retta dritto lì:
        // è quello che dà l'effetto di un mezzo che curva invece di planare di lato.
        Vector3 direction = vehicleMode ? rb.rotation * Vector3.forward : toTarget.normalized;
        rb.MovePosition(rb.position + direction * speed * Time.fixedDeltaTime);
    }

    private void Rotate(Vector3 toTarget)
    {
        if (toTarget.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);

        if (vehicleMode)
        {
            // Sterzata graduale (gradi/secondo) invece che ruotare di scatto sul bersaglio.
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRotation, vehicleTurnSpeed * Time.fixedDeltaTime));
            return;
        }

        rb.MoveRotation(targetRotation);
    }
}
