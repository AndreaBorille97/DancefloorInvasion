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
    [SerializeField] private float patrolRadius = 1f;
    [Tooltip("Distanza dal punto di pattuglia corrente sotto la quale è considerato raggiunto e ne viene scelto subito un altro: bassa apposta, così il Raver non si ferma mai del tutto (a differenza di minApproachDistance, pensato per il contatto in combattimento).")]
    [SerializeField] private float patrolArrivalDistance = 0.3f;
    [Tooltip("Tempo massimo su un punto di pattuglia prima di sceglierne comunque uno nuovo, nel caso resti bloccato contro un ostacolo e non lo raggiunga mai davvero.")]
    [SerializeField] private float patrolMaxTimeOnPoint = 4f;
    [Tooltip("Velocità di movimento in pattuglia: più lenta del passo da combattimento.")]
    [SerializeField] private float patrolMoveSpeed = 0.7f;
    [Tooltip("Se il Raver è più lontano di così dal punto di guardia (es. appena sceso dal Camper dopo un giro), cammina dritto lì a passo normale invece di vagare lentamente in pattuglia: prima torna in posizione, poi riprende a pattugliare.")]
    [SerializeField] private float returningThreshold = 3f;

    [Header("Modalità veicolo (Camper)")]
    [Tooltip("Comportamento della v1: mentre pilota il Camper insegue sempre lo Sbirro più vicino, senza limiti di raggio, ma se si allontana oltre questa distanza dal Player smette e punta dritto verso di lui finché non rientra nel raggio.")]
    [SerializeField] private float maxDistanceFromPlayer = 10f;

    private Rigidbody rb;
    private Transform player; // usato solo in modalità veicolo, per il guinzaglio dal Player (v1)
    private Vector3 guardPosition; // punto attorno a cui pattuglia quando non c'è nessuna minaccia da ingaggiare, catturato in Awake
    private Vector3 patrolPoint; // punto di pattuglia corrente, entro patrolRadius da guardPosition
    private float patrolPointTimer;
    private Transform target;
    private Transform forcedTarget; // se impostato, ignora l'AI normale e punta dritto qui (vedi SetForcedTarget)
    private Vector3? forcedPosition; // come forcedTarget ma verso un punto fisso invece che un Transform (vedi SetForcedPosition, usato da RaverHealth per la ChilloutZone)
    private float retargetTimer;
    private bool vehicleMode; // true mentre pilota il Camper: guida in avanti sterzando invece di puntare/ruotare di scatto verso il bersaglio
    // Come si guida il mezzo: i valori qui sotto sono solo un ripiego ragionevole, in partita
    // arrivano dal Camper (CamperVehicle.Mount -> SetVehicleTuning), che è dove si regolano
    // nell'Inspector. La velocità è una caratteristica del mezzo, non del Raver che ci sale.
    private VehicleTuning tuning = new VehicleTuning();

    // Il mezzo che sta guidando, se è a bordo (vedi SetVehicle): serve solo a chiedergli di
    // tenere la propria sagoma fuori dagli obiettivi da proteggere.
    private CamperVehicle vehicle;

    private float vehicleSpeed; // velocità corrente alla guida del Camper: sale e scende per gradi, vedi DriveVehicle
    private float steerInput;   // posizione del volante, -1 tutto a sinistra, +1 tutto a destra: si muove per gradi, non a scatti

    // Usato da RaverDrugBuff per raddoppiare/ripristinare la velocità di movimento.
    public void MultiplyMoveSpeed(float factor)
    {
        moveSpeed *= factor;
    }

    // Usato da RaverHealth quando il Raver va a terra, per allontanarsi un po' dal punto
    // che stava difendendo invece di restare fermo esattamente lì (vedi GoDown).
    public Vector3 GuardPosition => guardPosition;

    // Usato da PlayerJugglerSummon per non scegliere come giocoliere un Raver già dirottato
    // altrove (es. in cammino verso il Camper, vedi CamperVehicle.TrySendNearestRaver):
    // altrimenti BeginJuggling chiamerebbe SetForcedPosition sopra un forcedTarget già
    // impostato da qualcun altro, cancellandolo senza che nessuno se ne accorga.
    public bool IsForced => forcedTarget != null || forcedPosition.HasValue;

    // Usato da CamperVehicle per mandare questo Raver dritto al Camper, ignorando qualunque
    // Sbirro ingaggiato finché non arriva (vedi GetMoveDestination). Riusa Move()/Rotate()
    // così esistenti, compreso lo stop entro minApproachDistance.
    public void SetForcedTarget(Transform newTarget)
    {
        forcedTarget = newTarget;
        forcedPosition = null;
    }

    // Come SetForcedTarget ma verso un punto fisso nel mondo invece che un Transform: usato da
    // RaverHealth per mandare ogni Raver a terra verso il proprio punto di riposo personale
    // attorno alla ChilloutZone (calcolato una sola volta, così non cambia più durante il
    // tragitto né all'arrivo).
    public void SetForcedPosition(Vector3 worldPosition)
    {
        forcedPosition = worldPosition;
        forcedTarget = null;
    }

    // Chiamato da CamperVehicle appena il Raver sale a bordo (o da RaverHealth all'arrivo alla
    // ChilloutZone/alla rianimazione): torna a bersagliare gli Sbirro (o il Robosbirro) come al solito.
    public void ClearForcedTarget()
    {
        forcedTarget = null;
        forcedPosition = null;
    }

    // Chiamato da CamperVehicle su Mount/Dismount: mentre è true, Move()/Rotate() si
    // comportano da veicolo (avanza lungo la propria direzione sterzando gradualmente verso
    // il bersaglio) invece che puntare dritto lì e ruotare di scatto come fa normalmente a piedi.
    // Chiamato da CamperVehicle prima di SetVehicleMode: i valori di guida sono del mezzo, non
    // del Raver, quindi mezzi diversi possono guidarsi in modo diverso senza toccare il Raver.
    public void SetVehicleTuning(VehicleTuning value)
    {
        if (value != null)
        {
            tuning = value;
        }
    }

    // Chiamato da CamperVehicle su Mount/Dismount: mentre è a bordo, il mezzo ha voce in
    // capitolo su dove il Raver può andare. La sua sagoma è molto più larga del Raver, e da
    // guidato il guscio solido del mezzo è spento: senza questo, il camion passerebbe dritto
    // sopra sound system, Console e chillout (vedi CamperVehicle.KeepBodyClearOfTargets).
    public void SetVehicle(CamperVehicle value)
    {
        vehicle = value;
    }

    public void SetVehicleMode(bool enabled)
    {
        vehicleMode = enabled;
        vehicleSpeed = 0f; // si parte sempre da fermo, sia salendo sul mezzo che scendendone
        steerInput = 0f;   // e col volante dritto
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true; // ruotiamo noi manualmente verso il bersaglio, non vogliamo che la fisica ribalti il Raver
        rb.interpolation = RigidbodyInterpolation.Interpolate; // ammorbidisce il movimento tra un FixedUpdate e l'altro
        rb.useGravity = false;
        rb.constraints |= RigidbodyConstraints.FreezePositionY;

        player = GameObject.FindGameObjectWithTag("Player")?.transform;

        guardPosition = transform.position;
        PickNewPatrolPoint();

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

        // Guinzaglio duro al punto di guardia: solo da fermi (non in modalità veicolo, dove
        // vale invece il guinzaglio dal Player, vedi GetMoveDestination). Se inseguendo il
        // bersaglio il Raver stesso si è allontanato troppo dal punto di guardia, lo molla
        // subito: il presidio dell'obiettivo protetto ha sempre la priorità sull'inseguimento.
        if (!vehicleMode && target != null && (rb.position - guardPosition).sqrMagnitude > maxLeashFromGuard * maxLeashFromGuard)
        {
            target = null;
        }

        // A riposo (nessuna minaccia da ingaggiare, nessuna destinazione forzata dal Camper,
        // e non in modalità veicolo): normalmente pattuglia in continuazione tra punti vicini
        // invece di restare fermo come un blocco statico, al passo lento patrolMoveSpeed invece
        // del passo da combattimento. Se però è più lontano di returningThreshold dal punto di
        // guardia (es. appena sceso dal Camper dopo un giro per l'arena), prima ci torna dritto
        // a passo normale: niente pattuglia lenta finché non è di nuovo a casa. In modalità
        // veicolo non pattuglia mai: se non ha nessuno da inseguire resta semplicemente fermo
        // (vedi GetMoveDestination).
        bool isIdle = !vehicleMode && forcedTarget == null && !forcedPosition.HasValue && target == null;
        bool isReturning = isIdle && (rb.position - guardPosition).sqrMagnitude > returningThreshold * returningThreshold;
        if (isIdle && !isReturning)
        {
            UpdatePatrol();
        }

        Vector3 destination = GetMoveDestination(isReturning);
        Vector3 toTarget = destination - rb.position;
        toTarget.y = 0f;

        // Alla guida del Camper il movimento ha regole tutte sue (vedi DriveVehicle): a piedi il
        // Raver continua invece a puntare e camminare come ha sempre fatto.
        if (vehicleMode)
        {
            DriveVehicle(toTarget);
            return;
        }

        bool slowIdle = isIdle && !isReturning;
        Move(toTarget, slowIdle ? patrolMoveSpeed : moveSpeed, slowIdle ? patrolArrivalDistance : minApproachDistance);
        Rotate(toTarget);
    }

    // In ordine di priorità: destinazione forzata verso un Transform (il Camper da
    // raggiungere) > destinazione forzata verso un punto fisso (il posto personale alla
    // ChilloutZone, vedi RaverHealth) > [modalità veicolo: guinzaglio dal Player (v1), poi lo
    // Sbirro ingaggiato, altrimenti resta fermo] > [guardia normale: Sbirro ingaggiato entro
    // engageRadius da guardPosition, altrimenti, se lontano da casa (isReturning), dritto al
    // punto di guardia, altrimenti punto di pattuglia corrente].
    private Vector3 GetMoveDestination(bool isReturning)
    {
        if (forcedTarget != null)
        {
            return forcedTarget.position;
        }

        if (forcedPosition.HasValue)
        {
            return forcedPosition.Value;
        }

        if (vehicleMode)
        {
            if (player != null && (rb.position - player.position).sqrMagnitude > maxDistanceFromPlayer * maxDistanceFromPlayer)
            {
                // Troppo lontano dal Player: molla lo Sbirro e punta dritto verso di lui.
                return player.position;
            }

            return target != null ? target.position : rb.position;
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

    // In modalità veicolo (v1): lo Sbirro più vicino al Raver stesso, senza limiti di
    // raggio (il guinzaglio dal Player è gestito a parte in GetMoveDestination). Da fermi
    // (guardia normale): solo il più vicino a guardPosition, ed entro engageRadius da lì
    // — fuori da quel raggio resta null, e GetMoveDestination riporta il Raver in posizione.
    private void AcquireNearestEnemy()
    {
        if (vehicleMode)
        {
            // Alla guida il bersaglio si sceglie una volta e ci si tiene finché non sparisce.
            // Riprendere ogni mezzo secondo "il più vicino" lo faceva alternare fra due Sbirro
            // che si muovono, e il mezzo cambiava direzione di continuo invece di puntare dritto.
            if (target == null)
            {
                target = FindNearestEnemy(transform.position, float.MaxValue);
            }

            return;
        }

        target = FindNearestEnemy(guardPosition, engageRadius);
    }

    private static Transform FindNearestEnemy(Vector3 referencePoint, float maxRadius)
    {
        Transform nearest = null;
        float nearestSqrDistance = float.MaxValue;
        float sqrMaxRadius = maxRadius * maxRadius;

        foreach (EnemyHealth enemy in FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None))
        {
            float sqrDistance = (enemy.transform.position - referencePoint).sqrMagnitude;
            if (sqrDistance <= sqrMaxRadius && sqrDistance < nearestSqrDistance)
            {
                nearest = enemy.transform;
                nearestSqrDistance = sqrDistance;
            }
        }

        return nearest;
    }

    // Guida del Camper. Le regole sono quelle di un mezzo con le ruote, non di un inseguitore
    // a piedi, ed è tutta qui la differenza fra "curva" e "piroetta":
    //
    //   - la direzione cambia solo perché si avanza. I gradi girati vengono dallo spazio
    //     percorso in quel fotogramma diviso minTurnRadius, cioè dal raggio di sterzata: da
    //     fermo il mezzo non gira affatto, e in curva descrive un arco della taglia giusta.
    //     Con una velocità di rotazione in gradi/secondo, come prima, il raggio veniva fuori
    //     dal caso (alla velocità del Camper erano ~1.4 unità, meno della sua lunghezza: il
    //     mezzo ruotava dentro il proprio ingombro);
    //   - la velocità sale e scende per gradi (vehicleAcceleration): spunto alla partenza,
    //     frenata in arrivo;
    //   - più il bersaglio è di traverso, meno gas: un mezzo rallenta per curvare;
    //   - sotto steerDeadZone gradi non si corregge niente, altrimenti il mezzo serpeggia
    //     dietro a un bersaglio che si muove.
    private void DriveVehicle(Vector3 toTarget)
    {
        Vector3 heading = rb.rotation * Vector3.forward;
        bool hasDirection = toTarget.sqrMagnitude > 0.0001f;
        Vector3 wanted = hasDirection ? toTarget.normalized : heading;

        // Errore con segno: positivo se il bersaglio è a destra. Serve col segno perché il
        // volante ha un verso, non è solo "di quanto sono storto".
        float headingError = hasDirection ? Vector3.SignedAngle(heading, wanted, Vector3.up) : 0f;

        // Il volante si muove per gradi verso la posizione voluta invece di stare sempre a fondo
        // corsa: è questo che dà l'ingresso e l'uscita di curva morbidi. E la posizione voluta è
        // proporzionale all'errore, così a una correzione piccola corrisponde una sterzatina.
        float wantedSteer = Mathf.Abs(headingError) <= tuning.steerDeadZone
            ? 0f
            : Mathf.Clamp(headingError / Mathf.Max(1f, tuning.fullLockAngle), -1f, 1f);
        steerInput = Mathf.MoveTowards(steerInput, wantedSteer, tuning.steerRate * Time.fixedDeltaTime);

        // Si rallenta in base a quanto si sta sterzando, non a quanto si è storti: essendo lo
        // sterzo già smorzato, anche il gas cala e risale con la stessa morbidezza.
        bool arrived = !hasDirection || toTarget.magnitude <= minApproachDistance;
        float throttle = Mathf.Lerp(1f, tuning.minThrottle, Mathf.Abs(steerInput));
        float targetSpeed = arrived ? 0f : tuning.maxSpeed * throttle;
        vehicleSpeed = Mathf.MoveTowards(vehicleSpeed, targetSpeed, tuning.acceleration * Time.fixedDeltaTime);

        float distance = vehicleSpeed * Time.fixedDeltaTime;

        // Un radiante ogni minTurnRadius unità percorse è il fondo corsa: qui si gira la frazione
        // che il volante concede in questo momento. Il limite all'errore residuo evita di
        // scavalcare la direzione voluta e ripartire a correggere dall'altra parte.
        float degrees = distance / Mathf.Max(0.01f, tuning.minTurnRadius) * Mathf.Rad2Deg * steerInput;
        degrees = Mathf.Clamp(degrees, -Mathf.Abs(headingError), Mathf.Abs(headingError));
        Quaternion nextRotation = rb.rotation * Quaternion.AngleAxis(degrees, Vector3.up);

        // Dove finirebbe il Raver con questo passo, prima che il mezzo dica la sua: la sagoma
        // del camion è molto più larga del Raver che lo guida, e gli obiettivi da proteggere
        // (sound system, Console e chillout) non vanno mai compenetrati. Se il passo ce lo porterebbe
        // sopra, il mezzo lo respinge fuori quel tanto che basta: il camion ci struscia contro
        // e scivola via, invece di entrarci dentro. La correzione vale anche da fermi (distance
        // 0), così un mezzo che nasce a ridosso dell'obiettivo si scosta subito.
        Vector3 nextPosition = rb.position + heading * distance;
        if (vehicle != null)
        {
            nextPosition = vehicle.KeepBodyClearOfTargets(nextPosition, nextRotation);
        }

        if (nextPosition != rb.position)
        {
            rb.MovePosition(nextPosition);
        }

        if (distance <= 0f)
        {
            // Fermo: non avanza e soprattutto non gira. È il motivo per cui il mezzo non
            // piroetta più sul posto quando arriva addosso a uno Sbirro.
            return;
        }

        rb.MoveRotation(nextRotation);
    }

    private void Move(Vector3 toTarget, float speed, float stopDistance)
    {
        if (toTarget.magnitude <= stopDistance)
        {
            // Già abbastanza vicino (a contatto in combattimento, o al punto di pattuglia
            // corrente): non avanzare oltre, altrimenti a velocità elevata il Raver sfonda e
            // compenetra il bersaglio.
            return;
        }

        rb.MovePosition(rb.position + toTarget.normalized * speed * Time.fixedDeltaTime);
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
