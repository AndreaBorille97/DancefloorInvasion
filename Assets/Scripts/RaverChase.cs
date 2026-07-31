using UnityEngine;

// Da mettere sul prefab del Raver (l'alleato generato da RaverPickup), insieme a
// RaverAttack. Il Raver carica lo Sbirro più vicino e lo insegue in linea retta sul
// piano orizzontale (X/Z), ricalcolando periodicamente il bersaglio — stesso schema di
// EnemyChase, ma il Raver punta gli Sbirro invece che il Player. Se però si allontana
// troppo dal Player (guinzaglio), smette di inseguire lo Sbirro e si dirige verso di lui
// finché non rientra nel raggio. Smette di avvicinarsi entro minApproachDistance dal
// bersaglio, restando comunque a contatto: evita di sfondarlo/compenetrarlo quando la
// velocità è alta (es. buff drug, vedi RaverDrugBuff).
[RequireComponent(typeof(Rigidbody))]
public class RaverChase : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f; // velocità di inseguimento (unità al secondo)

    [Header("Target")]
    [Tooltip("Ogni quanti secondi ricalcola lo Sbirro più vicino da caricare.")]
    [SerializeField] private float retargetInterval = 0.5f;

    [Header("Guinzaglio dal Player")]
    [Tooltip("Se il Raver supera questa distanza dal Player, smette di inseguire lo Sbirro e si avvicina di nuovo a lui.")]
    [SerializeField] private float maxDistanceFromPlayer = 10f;
    [Tooltip("Moltiplicatore applicato a maxDistanceFromPlayer mentre il Robosbirro è in scena: il boss si allontana di più del Player durante l'inseguimento, un guinzaglio corto lo farebbe perdere di vista troppo presto.")]
    [SerializeField] private float robosbirroLeashMultiplier = 2f;

    [Tooltip("Distanza minima dal bersaglio oltre la quale il Raver smette di avvicinarsi: deve restare (di poco) inferiore alla somma dei raggi dei collider, altrimenti il Raver non tocca più il bersaglio e RaverAttack (a contatto) non parte più. Tienilo vicino a quella somma (qui 1: 0.5 Raver + 0.5 Sbirro): una sovrapposizione più marcata costringe la fisica a correggerla ad ogni FixedUpdate, ed è quello scatto/stuttering che si vede a velocità elevata (es. buff drug).")]
    [SerializeField] private float minApproachDistance = 0.95f;

    [Header("Modalità veicolo (Camper)")]
    [Tooltip("Quanto velocemente sterza mentre guida il Camper (gradi al secondo), invece di ruotare di scatto come normalmente: vedi SetVehicleMode.")]
    [SerializeField] private float vehicleTurnSpeed = 180f;

    private Rigidbody rb;
    private Transform player;
    private Transform target;
    private Transform forcedTarget; // se impostato, ignora l'AI normale e punta dritto qui (vedi SetForcedTarget)
    private float retargetTimer;
    private bool robosbirroPresent;
    private bool vehicleMode; // true mentre pilota il Camper: guida in avanti sterzando invece di puntare/ruotare di scatto verso il bersaglio

    // Usato da RaverDrugBuff per raddoppiare/ripristinare la velocità di movimento.
    public void MultiplyMoveSpeed(float factor)
    {
        moveSpeed *= factor;
    }

    // Usato da CamperVehicle per mandare questo Raver dritto al Camper, ignorando Sbirro e
    // guinzaglio dal Player finché non arriva (vedi GetMoveTarget). Riusa Move()/Rotate()
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

        player = GameObject.FindGameObjectWithTag("Player")?.transform;

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

        Transform moveTarget = GetMoveTarget();
        if (moveTarget == null)
        {
            return;
        }

        Vector3 toTarget = moveTarget.position - rb.position;
        toTarget.y = 0f;

        Move(toTarget);
        Rotate(toTarget);
    }

    // Fuori dal guinzaglio: ignora lo Sbirro e punta al Player finché non rientra nel raggio.
    private Transform GetMoveTarget()
    {
        if (forcedTarget != null)
        {
            // Priorità assoluta: usato per una destinazione specifica assegnata da fuori
            // (es. il Camper da raggiungere), niente guinzaglio né retarget automatico.
            return forcedTarget;
        }

        if (player != null)
        {
            float leash = robosbirroPresent ? maxDistanceFromPlayer * robosbirroLeashMultiplier : maxDistanceFromPlayer;
            float sqrDistanceFromPlayer = (rb.position - player.position).sqrMagnitude;
            if (sqrDistanceFromPlayer > leash * leash)
            {
                return player;
            }
        }

        return target;
    }

    private void AcquireNearestEnemy()
    {
        // Il Robosbirro (boss unico, niente tag/layer dedicato: stesso approccio "cerca il
        // componente" già usato altrove) ha sempre priorità assoluta: appena presente in scena,
        // tutti i Raver lo caricano ignorando gli Sbirro normali.
        RobosbirroHealth robosbirro = FindAnyObjectByType<RobosbirroHealth>();
        robosbirroPresent = robosbirro != null;
        if (robosbirro != null)
        {
            target = robosbirro.transform;
            return;
        }

        Transform nearest = null;
        float nearestSqrDistance = float.MaxValue;

        foreach (EnemyHealth enemy in FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None))
        {
            float sqrDistance = (enemy.transform.position - transform.position).sqrMagnitude;
            if (sqrDistance < nearestSqrDistance)
            {
                nearest = enemy.transform;
                nearestSqrDistance = sqrDistance;
            }
        }

        target = nearest;
    }

    private void Move(Vector3 toTarget)
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
        rb.MovePosition(rb.position + direction * moveSpeed * Time.fixedDeltaTime);
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
