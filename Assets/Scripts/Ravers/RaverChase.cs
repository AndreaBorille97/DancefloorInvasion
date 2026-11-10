using UnityEngine;

// Da mettere sul prefab del Raver (posizionato a mano in scena, non più generato da uno
// spawner/pickup dedicato), insieme a RaverAttack. Comportamento da guardia difensiva, non
// da inseguitore: pattuglia in continuazione tra punti vicini attorno a dove è stato
// posizionato (guardPosition, catturato in Awake, vedi patrolRadius) invece di restare fermo
// come un blocco statico, e si attiva per caricare lo Sbirro più vicino solo quando questo
// entra entro engageRadius da quel punto, ricalcolando periodicamente; appena la minaccia
// sparisce o esce dal raggio, torna a pattugliare. Smette di avvicinarsi entro
// minApproachDistance dal bersaglio, restando comunque a contatto: evita di
// sfondarlo/compenetrarlo quando la velocità è alta (es. buff drug, vedi RaverDrugBuff).
[RequireComponent(typeof(Rigidbody))]
public class RaverChase : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f; // velocità di inseguimento (unità al secondo)

    [Header("Target")]
    [Tooltip("Ogni quanti secondi ricalcola lo Sbirro più vicino entro engageRadius da guardPosition.")]
    [SerializeField] private float retargetInterval = 0.5f;
    [Tooltip("Raggio di allerta attorno al punto di guardia: un Raver ingaggia solo gli Sbirro che entrano qui dentro. Tienilo piccolo apposta (il Raver deve scattare solo quando lo Sbirro è molto vicino, non rincorrerlo da lontano).")]
    [SerializeField] private float engageRadius = 1.2f;
    [Tooltip("Se il Raver stesso (non lo Sbirro inseguito) supera questa distanza dal punto di guardia, molla l'inseguimento all'istante e torna a pattugliare: il presidio dell'obiettivo protetto ha sempre la priorità, anche se lo Sbirro nel frattempo si muove restando 'appena dentro' engageRadius. Tienilo di poco superiore a engageRadius.")]
    [SerializeField] private float maxLeashFromGuard = 2f;

    [Tooltip("Distanza minima dal bersaglio oltre la quale il Raver smette di avvicinarsi: deve restare (di poco) inferiore alla somma dei raggi dei collider, altrimenti il Raver non tocca più il bersaglio e RaverAttack (a contatto) non parte più. Tienilo vicino a quella somma (qui 1: 0.5 Raver + 0.5 Sbirro): una sovrapposizione più marcata costringe la fisica a correggerla ad ogni FixedUpdate, ed è quello scatto/stuttering che si vede a velocità elevata (es. buff drug).")]
    [SerializeField] private float minApproachDistance = 0.95f;

    [Header("Pattuglia a riposo")]
    [Tooltip("Raggio entro cui il Raver pattuglia attorno al punto di guardia quando non sta ingaggiando nessuno: tienilo piccolo, deve dare l'idea di uno spostarsi sul posto, non di un giro ampio.")]
    [SerializeField] private float patrolRadius = 0.65f;
    [Tooltip("Distanza dal punto di pattuglia corrente sotto la quale è considerato raggiunto e ne viene scelto subito un altro: bassa apposta, così il Raver non si ferma mai del tutto (a differenza di minApproachDistance, pensato per il contatto in combattimento).")]
    [SerializeField] private float patrolArrivalDistance = 0.3f;
    [Tooltip("Tempo massimo su un punto di pattuglia prima di sceglierne comunque uno nuovo, nel caso resti bloccato contro un ostacolo e non lo raggiunga mai davvero.")]
    [SerializeField] private float patrolMaxTimeOnPoint = 4f;
    [Tooltip("Velocità di movimento in pattuglia: più lenta del passo da combattimento.")]
    [SerializeField] private float patrolMoveSpeed = 0.5f;
    [Tooltip("Se il Raver è più lontano di così dal punto di guardia (es. di ritorno dalla ChilloutZone), cammina dritto lì a passo normale invece di vagare lentamente in pattuglia: prima torna in posizione, poi riprende a pattugliare.")]
    [SerializeField] private float returningThreshold = 3f;

    [Header("Obiettivo da difendere")]
    [Tooltip("Il SoundSystem o la Console che questo Raver difende. Se vuoto prende il più vicino al punto di guardia. Di guardia il Raver gli sta sempre girato verso, e quando scatta su uno Sbirro non gli dà mai le spalle (vedi maxAngleFromObjective).")]
    [SerializeField] private Transform defendedObjective;
    [Tooltip("Di quanti gradi al massimo il Raver può girarsi rispetto alla direzione dell'obiettivo mentre combatte o rientra: 90 = al massimo di fianco, mai di spalle.")]
    [SerializeField] private float maxAngleFromObjective = 90f;

    private Rigidbody rb;
    private Vector3 guardPosition; // punto attorno a cui pattuglia quando non c'è nessuna minaccia da ingaggiare, catturato in Awake
    private Vector3 patrolPoint; // punto di pattuglia corrente, entro patrolRadius da guardPosition
    private float patrolPointTimer;
    private Transform target;
    private Collider defendedObjectiveCollider; // cache per ObjectiveCenter, vedi Start
    private Vector3? forcedPosition; // se impostato, ignora l'AI normale e punta dritto a questo punto fisso (vedi SetForcedPosition, usato da RaverHealth per la ChilloutZone)
    private float retargetTimer;

    // Percorso sul NavMesh per aggirare gli ostacoli quando va da qualche parte (vedi FixedUpdate).
    private readonly NavPathFollower navPath = new NavPathFollower();

    // Letto da RaverAnimation: true nei passi in cui il Raver si sposta davvero per andare da
    // qualche parte (alla chillout, di ritorno al punto di guardia, addosso a uno
    // Sbirro). La pattuglia lenta attorno al punto di guardia non conta: lì il Raver balla.
    public bool IsWalking { get; private set; }

    // true mentre torna dritto al punto di guardia (da lontano): letto
    // dall'IdroSbirro, che chi sta rientrando non lo bagna.
    public bool IsReturningToGuard { get; private set; }

    // Usato da RaverDrugBuff per raddoppiare/ripristinare la velocità di movimento.
    public void MultiplyMoveSpeed(float factor)
    {
        moveSpeed *= factor;
    }

    // Usato da RaverHealth quando il Raver va a terra, per allontanarsi un po' dal punto
    // che stava difendendo invece di restare fermo esattamente lì (vedi GoDown).
    public Vector3 GuardPosition => guardPosition;

    // Il SoundSystem o la Console che difende: l'IdroSbirro sceglie i bersagli per obiettivo.
    public Transform DefendedObjective => defendedObjective;

    // Verso un punto fisso nel mondo: usato da
    // RaverHealth per mandare ogni Raver a terra verso il proprio punto di riposo personale
    // attorno alla ChilloutZone (calcolato una sola volta, così non cambia più durante il
    // tragitto né all'arrivo).
    public void SetForcedPosition(Vector3 worldPosition)
    {
        forcedPosition = worldPosition;
    }

    // Chiamato da RaverHealth all'arrivo alla ChilloutZone/alla rianimazione: torna a
    // bersagliare gli Sbirro come al solito.
    public void ClearForcedPosition()
    {
        forcedPosition = null;
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true; // ruotiamo noi manualmente verso il bersaglio, non vogliamo che la fisica ribalti il Raver
        rb.interpolation = RigidbodyInterpolation.Interpolate; // ammorbidisce il movimento tra un FixedUpdate e l'altro
        rb.useGravity = false;
        rb.constraints |= RigidbodyConstraints.FreezePositionY;

        guardPosition = transform.position;
        PickNewPatrolPoint();

        AcquireNearestEnemy();
    }

    void Start()
    {
        // In Start e non in Awake: SoundSystem e Console devono essere già pronti.
        if (defendedObjective == null)
        {
            defendedObjective = FindNearestObjective(guardPosition);
        }

        // Cache del collider dell'obiettivo (per ObjectiveCenter, chiamato ogni FixedUpdate):
        // l'obiettivo non viene mai distrutto, quindi la cache resta buona per tutta la partita.
        defendedObjectiveCollider = defendedObjective != null
            ? defendedObjective.GetComponentInChildren<Collider>()
            : null;
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

        // Guinzaglio duro al punto di guardia. Se inseguendo il
        // bersaglio il Raver stesso si è allontanato troppo dal punto di guardia, lo molla
        // subito: il presidio dell'obiettivo protetto ha sempre la priorità sull'inseguimento.
        if (target != null && (rb.position - guardPosition).sqrMagnitude > maxLeashFromGuard * maxLeashFromGuard)
        {
            target = null;
        }

        // A riposo (nessuna minaccia da ingaggiare, nessuna destinazione forzata):
        // normalmente pattuglia in continuazione tra punti vicini
        // invece di restare fermo come un blocco statico, al passo lento patrolMoveSpeed invece
        // del passo da combattimento. Se però è più lontano di returningThreshold dal punto di
        // guardia (es. di ritorno dalla ChilloutZone), prima ci torna dritto
        // a passo normale: niente pattuglia lenta finché non è di nuovo a casa.
        bool isIdle = !forcedPosition.HasValue && target == null;
        bool isReturning = isIdle
            && (rb.position - guardPosition).sqrMagnitude > returningThreshold * returningThreshold;
        IsReturningToGuard = isReturning;
        if (isIdle && !isReturning)
        {
            UpdatePatrol();
        }

        Vector3 destination = GetMoveDestination(isReturning);
        Vector3 toTarget = destination - rb.position;
        toTarget.y = 0f;

        bool slowIdle = isIdle && !isReturning;

        // Quando va da qualche parte (non nella pattuglia lenta, che resta a due passi dal posto)
        // aggira gli ostacoli in mezzo, es. l'IdroSbirro parcheggiato, passando per gli angoli del
        // percorso sul NavMesh: sono tappe da raggiungere davvero, quindi senza soglia d'arresto.
        // toTarget resta la direzione verso la meta vera, per come il Raver si gira qui sotto.
        Vector3 toStep = toTarget;
        bool isPathCorner = false;
        if (!slowIdle)
        {
            toStep = navPath.GetNextWaypoint(rb.position, destination, out isPathCorner) - rb.position;
            toStep.y = 0f;
        }

        bool moved = Move(toStep, slowIdle ? patrolMoveSpeed : moveSpeed,
            isPathCorner ? 0f : slowIdle ? patrolArrivalDistance : minApproachDistance);
        if (isPathCorner)
        {
            toTarget = toStep; // in giro attorno all'ostacolo guarda dove cammina
        }
        IsWalking = moved && !slowIdle;

        // Diretto alla chillout il Raver lascia il suo posto e guarda dove va. Al
        // posto invece non dà mai le spalle a quello che difende: di guardia (ballo) lo guarda
        // dritto, in combattimento o rientrando al massimo gli sta di fianco.
        bool leavingPost = forcedPosition.HasValue;
        if (leavingPost || defendedObjective == null)
        {
            Rotate(toTarget);
        }
        else
        {
            Rotate(FacingTowardObjective(slowIdle ? Vector3.zero : toTarget));
        }
    }

    // La direzione voluta, girata quanto basta perché l'obiettivo resti entro
    // maxAngleFromObjective gradi dal davanti. Direzione nulla = guarda dritto l'obiettivo.
    private Vector3 FacingTowardObjective(Vector3 wanted)
    {
        Vector3 toObjective = ObjectiveCenter() - rb.position;
        toObjective.y = 0f;
        if (toObjective.sqrMagnitude < 0.0001f)
        {
            return wanted;
        }

        if (wanted.sqrMagnitude < 0.0001f)
        {
            return toObjective;
        }

        float angle = Vector3.SignedAngle(toObjective, wanted, Vector3.up);
        angle = Mathf.Clamp(angle, -maxAngleFromObjective, maxAngleFromObjective);
        return Quaternion.AngleAxis(angle, Vector3.up) * toObjective;
    }

    // Il centro della sagoma e non il pivot: quello di un modello importato può stare di lato.
    private Vector3 ObjectiveCenter()
    {
        return defendedObjectiveCollider != null ? defendedObjectiveCollider.bounds.center : defendedObjective.position;
    }

    private static Transform FindNearestObjective(Vector3 referencePoint)
    {
        Transform nearest = null;
        float nearestSqrDistance = float.MaxValue;

        foreach (IObjective objective in ObjectiveRegistry.All)
        {
            float sqrDistance = (objective.transform.position - referencePoint).sqrMagnitude;
            if (sqrDistance < nearestSqrDistance)
            {
                nearest = objective.transform;
                nearestSqrDistance = sqrDistance;
            }
        }

        return nearest;
    }

    // Disabilitato (a terra alla chillout, vedi RaverHealth.Settle) non si muove più.
    void OnDisable()
    {
        IsWalking = false;
    }

    // In ordine di priorità: destinazione forzata verso un punto fisso (il posto personale alla
    // ChilloutZone, vedi RaverHealth) > Sbirro ingaggiato entro
    // engageRadius da guardPosition, altrimenti, se lontano da casa (isReturning), dritto al
    // punto di guardia, altrimenti punto di pattuglia corrente.
    private Vector3 GetMoveDestination(bool isReturning)
    {
        if (forcedPosition.HasValue)
        {
            return forcedPosition.Value;
        }

        if (target != null)
        {
            return target.position;
        }

        return isReturning ? guardPosition : patrolPoint;
    }

    // Appena il punto di pattuglia corrente è raggiunto (o dopo patrolMaxTimeOnPoint secondi,
    // nel caso resti bloccato contro un ostacolo prima di arrivarci) ne sceglie subito un
    // altro: è il ciclo continuo raggiungi-poi-scegli-il-prossimo che dà il senso di pattuglia,
    // a differenza di un timer fisso che lascerebbe il Raver fermo tra un punto e l'altro.
    private void UpdatePatrol()
    {
        patrolPointTimer += Time.fixedDeltaTime;

        float sqrDistanceToPoint = (patrolPoint - transform.position).sqrMagnitude;
        bool reachedPoint = sqrDistanceToPoint <= patrolArrivalDistance * patrolArrivalDistance;

        if (reachedPoint || patrolPointTimer >= patrolMaxTimeOnPoint)
        {
            PickNewPatrolPoint();
        }
    }

    private void PickNewPatrolPoint()
    {
        patrolPointTimer = 0f;
        Vector2 offset = Random.insideUnitCircle * patrolRadius;
        patrolPoint = guardPosition + new Vector3(offset.x, 0f, offset.y);
    }

    // Lo Sbirro più vicino a guardPosition, ed entro engageRadius da lì
    // — fuori da quel raggio resta null, e GetMoveDestination riporta il Raver in posizione.
    private void AcquireNearestEnemy()
    {
        target = FindNearestEnemy(guardPosition, engageRadius);
    }

    private static Transform FindNearestEnemy(Vector3 referencePoint, float maxRadius)
    {
        Transform nearest = null;
        float nearestSqrDistance = float.MaxValue;
        float sqrMaxRadius = maxRadius * maxRadius;

        foreach (EnemyHealth enemy in EnemyHealth.All)
        {
            if (enemy.IsCharmed)
            {
                continue; // convertito dalla serotonina: sta dalla nostra parte (SerotoninBrawl)
            }

            float sqrDistance = (enemy.transform.position - referencePoint).sqrMagnitude;
            if (sqrDistance <= sqrMaxRadius && sqrDistance < nearestSqrDistance)
            {
                nearest = enemy.transform;
                nearestSqrDistance = sqrDistance;
            }
        }

        return nearest;
    }

    // Restituisce true se in questo passo si è spostato.
    private bool Move(Vector3 toTarget, float speed, float stopDistance)
    {
        if (toTarget.magnitude <= stopDistance)
        {
            // Già abbastanza vicino (a contatto in combattimento, o al punto di pattuglia
            // corrente): non avanzare oltre, altrimenti a velocità elevata il Raver sfonda e
            // compenetra il bersaglio.
            return false;
        }

        rb.MovePosition(rb.position + toTarget.normalized * speed * Time.fixedDeltaTime);
        return true;
    }

    private void Rotate(Vector3 toTarget)
    {
        if (toTarget.sqrMagnitude < 0.0001f)
        {
            return;
        }

        rb.MoveRotation(Quaternion.LookRotation(toTarget.normalized, Vector3.up));
    }
}
