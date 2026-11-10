using System.Collections.Generic;
using UnityEngine;

// Da mettere sul prefab del nemico (lo stesso che spawna EnemySpawner).
// Il nemico insegue sul piano orizzontale (X/Z) uno tra Console/DJ e SoundSystem (i due
// obiettivi da proteggere, assegnato allo spawn: vedi GetAssignedObjective). Con probabilità
// guardie a terra / guardie totali su quell'obiettivo (vedi GetGuardsFor) punta dritto
// all'obiettivo invece che alla propria corsia di guardia (vedi FindLaneRaver): in quel caso
// non cerca né aggira i Raver vivi, ingaggiandoli solo se ci collide fisicamente lungo la
// strada. Il tiro (vedi AcquireNearestTarget/attacksObjectiveDirectly) si ritira ad ogni
// cambio del numero di guardie vive ma solo finché non è stato vinto: una volta deciso di
// puntare all'obiettivo la scelta è definitiva. Se il Player entra entro playerAggroRadius
// diventa lui il bersaglio, e ha la precedenza su tutto il resto, giocoliere compreso.
// Nessun controllo separato "ingaggia il Raver più vicino": se uno vivo blocca fisicamente la
// strada verso la destinazione scelta, la collisione fisica con lui fa scattare EnemyAttack
// (basato su OnCollisionEnter/Stay) a prescindere da cosa punta ufficialmente questo nemico.
[RequireComponent(typeof(Rigidbody))]
public class EnemyChase : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 4.5f; // velocità di inseguimento (unità al secondo)
    [Tooltip("Distanza dal punto di avvicinamento (vedi FixedUpdate) sotto la quale il nemico smette di avvicinarsi ulteriormente. Senza questa soglia il nemico continuerebbe a spingersi verso il bersaglio ad ogni FixedUpdate anche a contatto avvenuto: dato che sia i nemici sia i Raver sono Rigidbody non kinematic, quella spinta continua li sposta fisicamente invece di restarci semplicemente a contatto. È misurata dal centro del nemico alla superficie del bersaglio, quindi per toccarlo deve stare sotto il raggio del nemico: in gioco viene comunque limitata al 90% del raggio, altrimenti EnemyAttack (a contatto) non partirebbe mai.")]
    [SerializeField] private float stopDistance = 0.9f;
    [Tooltip("A quanti metri dalla faccia di Console/SoundSystem il nemico si ferma per attaccarla, misurati dal bordo del suo collider (non dal centro, quindi vale uguale per nemici piccoli e grandi). L'attacco agli obiettivi parte da qui, senza contatto fisico (vedi EnemyAttack/IsAtObjective): così nessuno si schiaccia contro le casse o ci si infila dentro.")]
    [SerializeField] private float objectiveStandoff = 2f;

    [Header("Animazione")]
    [Tooltip("Moltiplicatore di velocità dell'Animator (1 = normale): il movimento è gestito interamente da script (niente root motion), quindi il ciclo di camminata va accelerato a parte per restare a passo con moveSpeed.")]
    [SerializeField] private float animationSpeedMultiplier = 1.5f;

    [Header("Target")]
    [Tooltip("Ogni quanti secondi ricalcola il bersaglio più vicino tra Console/DJ e SoundSystem, e ricontrolla la distanza dal Player.")]
    [SerializeField] private float retargetInterval = 0.5f;
    [Tooltip("Se il Player entra entro questa distanza, diventa bersaglio prioritario rispetto a Console/DJ e SoundSystem: da provare/tarare in game.")]
    [SerializeField] private float playerAggroRadius = 16f;

    [Header("Distribuzione sull'obiettivo")]
    [Tooltip("Scarto laterale casuale (in metri), assegnato una volta per nemico allo spawn, applicato solo quando punta a Console/SoundSystem: sposta il punto di arrivo sulla superficie dell'obiettivo invece di puntare sempre al punto più vicino in assoluto (che, per nemici provenienti dalla stessa direzione, sarebbe sempre lo stesso). Per il SoundSystem, i cui collider (\"muro1..muro4\") sono separati su quasi 20 unità, va tenuto abbastanza grande (non pochi metri) da poter far scegliere a un nemico un muro diverso da quello semplicemente più vicino, non solo un punto diverso sullo stesso muro. Non tocca l'ingaggio ravvicinato dei Raver né l'aggro sul Player.")]
    [SerializeField] private float objectiveApproachSpread = 10f;
    [Tooltip("Su 5 nemici nati (contati tra tutti gli spawner insieme, vedi objectiveAssignmentCounter), quanti puntano di default alla Console invece che al SoundSystem: i restanti vanno al SoundSystem. Dai punti di spawn (a metà dei lati sx/dx) il SoundSystem risulta quasi sempre l'obiettivo geometricamente più vicino, quindi senza questa quota i nemici convergerebbero quasi tutti lì, ignorando la Console. Ignorato dai nemici che stanno già inseguendo Raver/Player: sceglie solo tra Console e SoundSystem quando non c'è nient'altro da fare.")]
    [SerializeField] private int consoleSharePerFive = 2;

    [Header("Posizionamento")]
    [Tooltip("Se attivo, alla comparsa ignora l'altezza di spawn impostata su EnemySpawner e si riposiziona esattamente sulla superficie di FloorBounds. Utile per modelli con la Y tarata a occhio (es. il rig di Sbirro1), a cui il buffer verticale dello spawner farebbe fluttuare i piedi sopra il pavimento.")]
    [SerializeField] private bool snapToFloorOnSpawn = false;
    [Tooltip("Piccolo scarto verticale aggiunto dopo lo snap al pavimento, per compensare i piedi del modello che compenetrano leggermente il suolo (alza se serve).")]
    [SerializeField] private float floorSnapYOffset = 0f;

    private Rigidbody rb;
    private Animator animator;
    private Transform target;
    // Collider più vicino del bersaglio corrente (vedi SetTarget/FindNearestCollider):
    // usato per calcolare il punto di avvicinamento reale in FixedUpdate invece del semplice
    // target.position, che per un bersaglio grande come la Console sarebbe il centro della
    // sua geometria, sepolto ben dentro il BoxCollider e mai raggiungibile.
    private Collider targetCollider;
    private Vector3 lastApproachPoint; // per il debug via gizmo, vedi OnDrawGizmosSelected
    private float retargetTimer;
    private bool hasEnteredArena;
    private Vector3 spawnPoint; // dove è nato: ci torna quando scappa (vedi FleeHome)
    private Vector3[] arrivalPoints; // il percorso d'arrivo intero, da rifare a ritroso in fuga
    private RoutHome routHome;
    private Vector3[] pathPoints;
    private int pathIndex;
    private float pathReachRadius = 1.5f;
    // Scarto fisso assegnato in Awake (vedi objectiveApproachSpread): stesso nemico, stesso
    // scarto per tutta la sua vita, così non "trema" tra un punto e l'altro ogni retarget.
    private Vector3 approachBias;
    // Percentile fisso (0..1) assegnato in Awake: sceglie quale Raver di guardia (ordinati
    // lungo X) punteggiare da lontano quando nessuno è ancora a portata (vedi FindLaneRaver),
    // così nemici diversi si dirigono verso "corsie" diverse della barriera invece di
    // convergere tutti sul Raver più vicino in assoluto.
    private float lateralPreference;
    // Contatore globale (tra tutti gli Sbirro nati, di qualunque spawner) usato da
    // GetAssignedObjective per rispettare la quota consoleSharePerFive: incrementato una volta
    // per nemico in Awake, mai azzerato in partita.
    private static int objectiveAssignmentCounter;
    // Obiettivo di default assegnato in Awake in base al turno del contatore: true = Console,
    // false = SoundSystem (vedi GetAssignedObjective). Fisso per tutta la vita del nemico,
    // così non cambia idea ad ogni retarget.
    private bool assignedConsole;
    // Obiettivo e numero di guardie a terra su cui si basava l'ultimo tiro a sorte "vado dritto
    // all'obiettivo invece che alla mia corsia" (vedi AcquireNearestTarget/
    // attacksObjectiveDirectly): si ritira solo quando uno dei due cambia, non ad ogni
    // retarget (altrimenti col tempo finirebbe comunque per "vincere" prima o poi, vanificando
    // la probabilità a terra/totali). Così ogni guardia che cade dà una nuova chance, con la
    // probabilità aggiornata, ai nemici che non l'hanno ancora vinta.
    private Transform lastRollObjective;
    private int lastRollDownCount = -1;
    // true se il tiro ha deciso di puntare dritto all'obiettivo invece che alla propria corsia
    // di guardia: in quel caso il nemico non cerca né aggira i Raver vivi, li ingaggia solo se
    // ci collide fisicamente lungo la strada (vedi EnemyAttack, indipendente da questo script).
    // Una volta true resta true per tutta la vita del nemico: la decisione non si ritira più,
    // altrimenti mollerebbe l'obiettivo a metà strada (vedi AcquireNearestTarget).
    private bool attacksObjectiveDirectly;
    // Raggio del collider di questo nemico in world space (scala compresa), misurato una volta
    // in Awake: serve a sapere quanto deve avvicinarsi per toccare davvero un bersaglio fermo.
    // Varia moltissimo da un prefab all'altro (dal ~0.25 dello SbirroSfera al ~3.7 del
    // RoboSbirroNuvola), quindi una soglia fissa non può andare bene per tutti.
    private float ownRadius;
    // true quando il bersaglio è il giocoliere (vedi RaverJuggler.FindNearestAttractor): è
    // l'unico bersaglio che non si difende e non carica mai, quindi l'ultimo tratto deve
    // chiuderlo il nemico (vedi FixedUpdate).
    private bool isChasingAttractor;
    // true quando il bersaglio è Console/SoundSystem e il nemico è arrivato al suo punto di
    // attacco (a objectiveStandoff dalla faccia): letto da EnemyAttack, che da lì fa partire
    // l'attacco all'obiettivo senza bisogno di toccarlo. Ricalcolato ad ogni FixedUpdate.
    private bool isAtObjective;
    // Quanto può scostarsi dal punto di attacco (spinto da altri nemici o dai Raver) restando
    // comunque "arrivato": senza margine la carica si interromperebbe ad ogni urto.
    private const float ObjectiveReachTolerance = 0.75f;

    // Letto per non cambiare bersaglio a metà attacco (vedi il retarget in FixedUpdate).
    private EnemyAttack enemyAttack;

    // Percorso sul NavMesh verso il punto di avvicinamento (vedi GetNextWaypoint).
    private readonly NavPathFollower navPath = new NavPathFollower();

    // Camminata sul posto. Gli Sbirro a contatto (Sbirro*New_Controller) hanno solo camminata e
    // attacco, niente idle: fermi per qualsiasi motivo (a distanza d'attacco in cooldown, senza
    // bersaglio, incastrati) continuavano a camminare. Ora l'Animator si ferma quando il nemico
    // non avanza (vedi Update), e chi spinge senza avanzare per StuckSeconds fa un passo di lato
    // per SidestepSeconds (vedi Move), alternando il lato a ogni tentativo.
    private bool movedThisStep;
    private Vector3 lastStepPosition;
    private float stuckTimer;
    private float sidestepTimer;
    private Vector3 sidestepDirection;
    private float sidestepSign = 1f;
    private const float StuckSeconds = 0.5f;
    private const float SidestepSeconds = 0.5f;
    // Sotto questa frazione del passo previsto il nemico conta come bloccato.
    private const float StuckStepFraction = 0.25f;

    public Transform Target => target;
    public bool IsAtObjective => isAtObjective;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true; // ruotiamo noi manualmente verso il bersaglio, non vogliamo che la fisica ribalti il nemico
        rb.interpolation = RigidbodyInterpolation.Interpolate; // ammorbidisce il movimento tra un FixedUpdate e l'altro
        rb.useGravity = false;
        rb.constraints |= RigidbodyConstraints.FreezePositionY;

        Vector2 randomOffset = Random.insideUnitCircle * objectiveApproachSpread;
        approachBias = new Vector3(randomOffset.x, 0f, randomOffset.y);
        lateralPreference = Random.value;

        assignedConsole = (objectiveAssignmentCounter % 5) < consoleSharePerFive;
        objectiveAssignmentCounter++;

        // bounds è l'AABB in world space, quindi la scala del prefab è già inclusa: per una
        // capsula in piedi le semi-dimensioni su X/Z coincidono col raggio reale.
        Collider ownCollider = GetComponentInChildren<Collider>();
        ownRadius = ownCollider != null
            ? Mathf.Max(ownCollider.bounds.extents.x, ownCollider.bounds.extents.z)
            : stopDistance;

        // L'Animator sta sul modello figlio (es. RiotCop_Unity), non su questo GameObject.
        animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.speed = animationSpeedMultiplier;
        }

        if (snapToFloorOnSpawn && FloorBounds.Instance != null)
        {
            Vector3 position = rb.position;
            position.y = FloorBounds.Instance.Bounds.max.y + floorSnapYOffset;
            rb.position = position;
            transform.position = position;
        }

        spawnPoint = rb.position;
        enemyAttack = GetComponent<EnemyAttack>();
        AcquireNearestTarget();
    }

    // Camminata solo se avanza davvero (o se sta attaccando: lo stato di attacco deve poter
    // partire anche da fermo, vedi EnemyAttack.IsAttacking che conta già la carica).
    void Update()
    {
        if (animator != null)
        {
            bool animate = movedThisStep || (enemyAttack != null && enemyAttack.IsAttacking);
            animator.speed = animate ? animationSpeedMultiplier : 0f;
        }
    }

    // SerotoninBrawl spegne questo componente e fa camminare lo Sbirro per conto suo: l'Animator
    // non deve restare fermo dove l'abbiamo lasciato.
    void OnDisable()
    {
        if (animator != null)
        {
            animator.speed = animationSpeedMultiplier;
        }
    }

    void OnDestroy()
    {
        // Se questo Sbirro muore mentre ha un Raver come bersaglio, libera subito il suo
        // "posto" (vedi RaverHealth.EngagedCount): altrimenti il conteggio resterebbe gonfiato
        // per sempre e altri Sbirro eviterebbero quel Raver credendolo ancora impegnato.
        if (target != null && target.TryGetComponent(out RaverHealth engagedRaver))
        {
            engagedRaver.Disengage();
        }
    }

    void FixedUpdate()
    {
        // Lo scontro col bersaglio (kinematic o dinamico) assegna al nemico una vera
        // velocità fisica per risolvere la sovrapposizione. Il movimento qui sotto è già
        // interamente guidato da MovePosition, quindi quella velocità non serve: se non la
        // azzeriamo resta "appiccicata" al Rigidbody e lo fa scivolare per inerzia dopo il tocco.
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        isAtObjective = false;

        // Il passo chiesto nel FixedUpdate precedente è andato a segno? rb.position qui è già
        // quella risolta dalla fisica, dopo gli urti.
        bool blocked = movedThisStep
            && HorizontalDistance(rb.position, lastStepPosition) < moveSpeed * Time.fixedDeltaTime * StuckStepFraction;
        stuckTimer = blocked ? stuckTimer + Time.fixedDeltaTime : 0f;
        sidestepTimer -= Time.fixedDeltaTime;
        lastStepPosition = rb.position;
        movedThisStep = false;

        // Percorso obbligatorio dallo spawn (vedi EnemyPath, assegnato da EnemySpawner):
        // finché non è concluso il nemico lo segue e ignora bersagli, aggro del Player e
        // ingaggio dei Raver. All'ultimo waypoint riprende l'AI normale.
        if (FollowPath())
        {
            return;
        }

        if (RoutState.IsActive)
        {
            FleeHome();
            return;
        }

        // Finché l'attacco è in corso, animazione compresa (EnemyAttack.IsAttacking), il
        // bersaglio resta quello: senza questo blocco, ad esempio, il Player che entra in aggro
        // strappava il nemico a metà animazione dal Raver o dall'obiettivo che stava colpendo.
        // Il timer continua a scorrere, così finito l'attacco il retarget scatta subito.
        retargetTimer += Time.fixedDeltaTime;
        bool isMidAttack = enemyAttack != null && enemyAttack.IsAttacking;
        if (target == null || (retargetTimer >= retargetInterval && !isMidAttack))
        {
            retargetTimer = 0f;
            AcquireNearestTarget();
        }

        if (target == null)
        {
            return;
        }

        // Punto di avvicinamento reale: il punto più vicino sulla superficie del collider del
        // bersaglio (se ne ha uno), non il centro del Transform. Per Player/Raver, con
        // collider piccoli, la differenza è minima; per la Console (un unico grande
        // BoxCollider) o il SoundSystem (collider sui figli "muro1..muro4"), puntare al
        // centro del Transform significherebbe puntare a un punto sepolto dentro la
        // geometria solida, mai raggiungibile: il nemico ci si incastrerebbe contro, spinto
        // lì ad ogni FixedUpdate e respinto dalla fisica, causando le compenetrazioni.
        // Verso Console/SoundSystem lo scarto laterale assegnato allo spawn (approachBias)
        // sposta il punto mirato sulla superficie dell'obiettivo, così nemici diversi non
        // convergono tutti sullo stesso punto più vicino in assoluto (vedi objectiveApproachSpread).
        // Verso un Raver o il Player si punta invece sempre al vero punto più vicino: lì lo
        // spargimento non serve, anzi confonderebbe l'ingaggio ravvicinato.
        Vector3 approachReference = GetApproachReference(target);
        Vector3 approachPoint = GetApproachPoint(approachReference, out bool isDetour);
        lastApproachPoint = approachPoint; // per il debug via gizmo, vedi OnDrawGizmosSelected/OnDrawGizmos
        Vector3 toTarget = approachPoint - rb.position;
        toTarget.y = 0f; // il movimento resta sul piano orizzontale

        // Il giocoliere è l'unico bersaglio che non si difende e non carica mai nessuno (ha
        // forcedPosition, che in RaverChase ha priorità sul bersaglio): contro tutti gli altri
        // Raver è il Raver stesso a colmare l'ultimo tratto, e il contatto nasce da lì. Qui no,
        // quindi deve chiuderlo il nemico. stopDistance è misurato dal centro del nemico alla
        // SUPERFICIE del bersaglio: un nemico il cui collider è più sottile di quella soglia
        // (Sbirro3 sta a 0.34, lo SbirroSfera a 0.25, contro 0.9) si fermerebbe a mezz'aria
        // senza toccarlo mai, e l'attacco — che scatta solo per collisione fisica, vedi
        // EnemyAttack — non partirebbe. Fermandosi invece poco dentro il proprio raggio il
        // contatto è garantito qualunque sia la corporatura del nemico.
        // Lo spigolo del SoundSystem (isDetour) è una tappa da raggiungere davvero: fermandosi
        // a stopDistance un nemico piccolo resterebbe ancora di fianco alla cassa, per sempre.
        // Il punto di attacco di Console/SoundSystem sta già a objectiveStandoff dalla faccia
        // (vedi GetApproachPoint), quindi va raggiunto quasi esattamente: stopDistance lo
        // farebbe fermare troppo presto, fuori portata.
        // Vale anche per tutti gli altri bersagli, non solo per il giocoliere: uno stopDistance
        // più grande del raggio del nemico lo fa fermare a vuoto davanti al Player o al Raver,
        // che lo aspettano fermi, e il nemico resta lì a camminare sul posto senza mai attaccare.
        // Succedeva allo Sbirro1 dopo averlo rimpicciolito (raggio 0.9, stopDistance 1.5), e
        // il tetto qui sotto evita di dover ritoccare il valore a ogni cambio di scala.
        bool isObjectiveTarget = IsObjective(target);
        float stopThreshold = isDetour ? 0f
            : isObjectiveTarget ? 0.2f
            : isChasingAttractor ? ownRadius * 0.9f
            : Mathf.Min(stopDistance, ownRadius * 0.9f);
        // Finché l'animazione di attacco è in corso resta fermo e girato com'è: continuando a
        // camminare verso il proprio bersaglio si staccherebbe da quello che sta colpendo (spesso
        // un Raver urtato lungo la strada, non il bersaglio ufficiale) e l'attacco si annullerebbe
        // a metà, o scivolerebbe via col manganello ancora alzato.
        if (!isMidAttack)
        {
            // Se un ostacolo (es. il Camper parcheggiato) sbarra la linea retta, prima si passa
            // per gli angoli del percorso sul NavMesh: sono tappe da raggiungere davvero, come lo
            // spigolo del SoundSystem, quindi senza soglia d'arresto.
            Vector3 waypoint = GetNextWaypoint(approachPoint, out bool isPathCorner);
            Vector3 toWaypoint = waypoint - rb.position;
            toWaypoint.y = 0f;
            Move(toWaypoint, isPathCorner ? 0f : stopThreshold);

            // Fermo al punto di attacco, toTarget si riduce a pochi centimetri in una direzione
            // qualsiasi: si guarda invece la parete dell'obiettivo che si sta colpendo.
            Vector3 facing = toWaypoint;
            if (isObjectiveTarget && !isDetour && !isPathCorner && targetCollider != null)
            {
                facing = targetCollider.ClosestPoint(rb.position) - rb.position;
                facing.y = 0f;
            }
            Rotate(facing);
        }

        isAtObjective = isObjectiveTarget && !isDetour && toTarget.magnitude <= ObjectiveReachTolerance;
    }

    // Dove andare adesso per raggiungere approachPoint (vedi NavPathFollower): la meta stessa a
    // strada libera, altrimenti il prossimo angolo del percorso che aggira l'ostacolo
    // (isPathCorner). Ancora fuori dall'arena si va dritti: lì il NavMesh non c'è.
    private Vector3 GetNextWaypoint(Vector3 approachPoint, out bool isPathCorner)
    {
        if (!hasEnteredArena)
        {
            isPathCorner = false;
            return approachPoint;
        }

        return navPath.GetNextWaypoint(rb.position, approachPoint, out isPathCorner);
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // Debug visivo del targeting: un pallino colorato sempre visibile sopra ogni nemico (che
    // bersaglio ha in questo momento: rosso Raver, verde Console/SoundSystem, blu Player),
    // più dettagli (linea al bersaglio, punto di avvicinamento reale) quando lo selezioni in
    // Hierarchy/Scene. Utile per capire a colpo d'occhio, in Play Mode, perché un nemico non
    // sta attaccando quello che ti aspetti.
    void OnDrawGizmos()
    {
        if (target == null)
        {
            return;
        }

        Gizmos.color = GetTargetGizmoColor();
        Gizmos.DrawSphere(transform.position + Vector3.up * 2.5f, 0.2f);
    }

    void OnDrawGizmosSelected()
    {
        if (target == null)
        {
            return;
        }

        Gizmos.color = GetTargetGizmoColor();
        Gizmos.DrawLine(transform.position, target.position);
        Gizmos.DrawWireSphere(target.position, 0.3f);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(lastApproachPoint, 0.25f);
    }

    private Color GetTargetGizmoColor()
    {
        if (target.GetComponent<RaverHealth>() != null)
        {
            return Color.red; // Raver vivo ingaggiato (o giocoliere)
        }

        if (IsObjective(target))
        {
            return Color.green; // Obiettivo vero (Console/SoundSystem)
        }

        if (target.CompareTag("Player"))
        {
            return Color.blue;
        }

        return new Color(1f, 0.5f, 0f); // Fallback difensivo: non dovrebbe verificarsi in condizioni normali
    }

    // Chiamato da EnemySpawner subito dopo l'Instantiate, se lo spawner ha un percorso.
    public void SetPath(EnemyPath path)
    {
        if (path == null || path.Count == 0)
        {
            return;
        }

        pathPoints = path.GetWorldPoints();
        arrivalPoints = pathPoints;
        pathIndex = 0;
        pathReachRadius = path.WaypointReachRadius;
    }

    // Avanza lungo i waypoint del percorso. Ritorna true finché il percorso è in corso
    // (in quel caso ha già gestito movimento/rotazione di questo frame), false quando non
    // c'è percorso o si è appena concluso: da lì FixedUpdate prosegue con l'AI normale.
    private bool FollowPath()
    {
        if (pathPoints == null)
        {
            return false;
        }

        // Partita finita: si abbandona il percorso e si torna indietro come gli altri (vedi
        // FleeHome, che riparte da pathIndex, i waypoint già superati).
        if (RoutState.IsActive)
        {
            pathPoints = null;
            return false;
        }

        Vector3 toWaypoint = pathPoints[pathIndex] - rb.position;
        toWaypoint.y = 0f;

        if (toWaypoint.magnitude <= pathReachRadius)
        {
            pathIndex++;
            if (pathIndex >= pathPoints.Length)
            {
                // Ultimo waypoint raggiunto: da qui bersaglia normalmente, e il clamp
                // dentro l'arena torna attivo (vedi ClampToFloor / hasEnteredArena).
                pathPoints = null;
                hasEnteredArena = true;
                SetTarget(null);
                return false;
            }

            return true; // waypoint raggiunto: il prossimo frame punta al successivo
        }

        Move(toWaypoint);
        Rotate(toWaypoint);
        return true;
    }

    private void AcquireNearestTarget()
    {
        // Ridefinito più sotto solo se finisce davvero sul giocoliere: uscendo prima per il
        // Player resterebbe altrimenti il valore del retarget precedente, e il nemico userebbe
        // la soglia di avvicinamento sbagliata (vedi isChasingAttractor in FixedUpdate).
        isChasingAttractor = false;

        // Obiettivo distrutto, partita persa (DefeatRout): caccia ai Raver ancora in piedi.
        if (DefeatRout.IsActive)
        {
            Transform prey = DefeatRout.FindNearestRaver(transform.position);
            if (prey != null)
            {
                SetTarget(prey);
                return;
            }
        }

        // Il Player ha la precedenza su tutto: se è entro playerAggroRadius diventa lui il
        // bersaglio anche quando c'è un giocoliere che sta attirando (vedi RaverJuggler).
        // L'esca funziona quindi solo su chi non ha già il Player a tiro.
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null
            && (player.transform.position - transform.position).sqrMagnitude <= playerAggroRadius * playerAggroRadius)
        {
            SetTarget(player.transform);
            return;
        }

        // Un Raver che sta facendo il giocoliere (vedi RaverJuggler) viene prima di
        // obiettivi e corsie, se questo Sbirro gli è entrato entro il raggio di attrazione.
        Transform attractor = RaverJuggler.FindNearestAttractor(transform.position);
        if (attractor != null)
        {
            isChasingAttractor = true;
            SetTarget(attractor);
            return;
        }

        Transform objective = GetAssignedObjective();
        Transform destination = null;

        if (objective != null)
        {
            // Probabilità (guardie a terra / guardie totali) di puntare dritto all'obiettivo
            // invece che alla propria corsia di guardia: con la barriera intatta nessuno sfonda,
            // con metà guardie a terra lo fa un nemico su due, senza guardie tutti. Si ritira ad
            // ogni cambio del numero di guardie a terra, ma SOLO finché non è stata vinta (vedi il
            // !attacksObjectiveDirectly qui sotto). Il blocco è essenziale: il numero cambia in
            // continuazione durante uno scontro (ogni Raver che cade lo alza, ogni rianimazione lo
            // abbassa), quindi senza di esso un nemico che ha già deciso di puntare all'obiettivo
            // ritirerebbe pochi decimi di secondo dopo e potrebbe mollare a metà strada per
            // dirottarsi su una guardia.
            List<RaverHealth> guards = GetGuardsFor(objective);
            int downGuards = 0;
            foreach (RaverHealth guard in guards)
            {
                if (guard.IsDown)
                {
                    downGuards++;
                }
            }

            if (!attacksObjectiveDirectly && (objective != lastRollObjective || downGuards != lastRollDownCount))
            {
                lastRollObjective = objective;
                lastRollDownCount = downGuards;
                attacksObjectiveDirectly = Random.value < DirectAttackChance(guards.Count, downGuards);
            }

            // Chi punta all'obiettivo ci va dritto: non cerca né aggira i Raver vivi, li
            // ingaggia solo se ci collide fisicamente lungo la strada (vedi EnemyAttack,
            // indipendente da questo script). Altrimenti punta al Raver di guardia della
            // propria "corsia" come sempre (vedi FindLaneRaver).
            destination = attacksObjectiveDirectly ? objective : FindLaneRaver(objective);
            if (destination == null)
            {
                destination = objective;
            }
        }

        // Il Player è già stato gestito in cima (ha la precedenza): qui resta solo come ultima
        // spiaggia se non esiste nessun obiettivo da attaccare, pur essendo fuori aggro.
        SetTarget(destination != null ? destination : player?.transform);
    }

    // Console o SoundSystem in base al turno assegnato in Awake (vedi assignedConsole/
    // consoleSharePerFive), non al più vicino in assoluto: dai punti di spawn (a metà dei lati
    // sx/dx) il SoundSystem è quasi sempre geometricamente più vicino della Console, quindi
    // usare la vera distanza qui vanificherebbe la quota e farebbe convergere comunque quasi
    // tutti i nemici sul SoundSystem. Ripiega sull'altro obiettivo se quello assegnato non
    // esiste più (es. Console già distrutta: a quel punto la partita è comunque persa, ma
    // resta un fallback difensivo).
    private Transform GetAssignedObjective()
    {
        DJConsoleHealth console = FindAnyObjectByType<DJConsoleHealth>();
        SoundSystem soundSystem = FindAnyObjectByType<SoundSystem>();

        Transform preferred = assignedConsole ? console?.transform : soundSystem?.transform;
        if (preferred != null)
        {
            return preferred;
        }

        return assignedConsole ? soundSystem?.transform : console?.transform;
    }

    // Tra i Raver vivi che presidiano lo stesso objective (vedi GetGuardsFor), sceglie quello
    // alla posizione lateralPreference (0..1) una volta ordinati lungo X: i due obiettivi sono a
    // metà dei lati superiore/inferiore dell'arena, con le guardie spalmate in orizzontale,
    // quindi X è l'asse lungo cui ha senso distribuire i nemici. Ritorna null se nessuna
    // guardia viva presidia quell'obiettivo (barriera sfondata o Raver tutti a terra).
    private Transform FindLaneRaver(Transform objective)
    {
        List<RaverHealth> guards = GetGuardsFor(objective);
        guards.RemoveAll(guard => guard.IsDown);
        if (guards.Count == 0)
        {
            return null;
        }

        guards.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
        int index = Mathf.Clamp(Mathf.RoundToInt(lateralPreference * (guards.Count - 1)), 0, guards.Count - 1);
        return guards[index].transform;
    }

    // Probabilità che uno Sbirro punti dritto all'obiettivo invece che alla sua corsia di guardia:
    // guardie a terra / guardie totali, 1 se l'obiettivo non ha guardie. In modalità facile è il
    // quadrato (vedi GameDifficulty): cresce più lentamente ma arriva sempre a 1.
    private static float DirectAttackChance(int guardCount, int downGuards)
    {
        float chance = guardCount > 0 ? (float)downGuards / guardCount : 1f;
        return GameDifficulty.IsEasy ? chance * chance : chance;
    }

    // Stessa probabilità, calcolata per un obiettivo: la mostra MinimapUI accanto alla vita di
    // Console e SoundSystem. Usa lo stesso conteggio del tiro a sorte, così il numero a schermo è quello vero.
    public static float GetDirectAttackChance(Transform objective, out int guardCount, out int downGuards)
    {
        List<RaverHealth> guards = GetGuardsFor(objective);
        guardCount = guards.Count;
        downGuards = 0;
        foreach (RaverHealth guard in guards)
        {
            if (guard.IsDown)
            {
                downGuards++;
            }
        }

        return DirectAttackChance(guardCount, downGuards);
    }

    // Per i log di controllo: l'obiettivo a cui viene contato questo Raver come guardia (null se
    // non conta, es. il giocoliere).
    public static Transform GetGuardedObjective(RaverHealth raver)
    {
        if (raver.TryGetComponent(out RaverJuggler juggler) && juggler.IsJuggler)
        {
            return null;
        }

        Vector3 guardPosition = raver.TryGetComponent(out RaverChase raverChase) ? raverChase.GuardPosition : raver.transform.position;
        return FindNearestObjective(guardPosition);
    }

    // Tutti i Raver (vivi e a terra, giocoliere escluso) che presidiano l'objective indicato: base condivisa da
    // FindLaneRaver e dal tiro a sorte in AcquireNearestTarget. Il raggruppamento usa il punto
    // di guardia (RaverChase.GuardPosition), non la posizione attuale: un Raver a terra
    // cammina verso la ChilloutZone e, misurato da dove si trova, finirebbe contato
    // sull'obiettivo sbagliato, falsando il rapporto a terra/totali.
    private static List<RaverHealth> GetGuardsFor(Transform objective)
    {
        List<RaverHealth> guards = new List<RaverHealth>();
        if (objective == null)
        {
            return guards;
        }

        foreach (RaverHealth raver in FindObjectsByType<RaverHealth>(FindObjectsSortMode.None))
        {
            // Il giocoliere non è una guardia: attira gli Sbirro e non si difende, quindi va a
            // terra spesso, e contarlo alzava la probabilità di sfondare dell'obiettivo più
            // vicino anche con tutte le guardie vere in piedi.
            if (raver.TryGetComponent(out RaverJuggler juggler) && juggler.IsJuggler)
            {
                continue;
            }

            Vector3 guardPosition = raver.TryGetComponent(out RaverChase raverChase)
                ? raverChase.GuardPosition
                : raver.transform.position;

            if (FindNearestObjective(guardPosition) != objective)
            {
                continue;
            }

            guards.Add(raver);
        }

        return guards;
    }

    // Aggiorna insieme target e targetCollider, così FixedUpdate ha sempre il collider giusto
    // con cui calcolare il punto di avvicinamento reale (vedi FindNearestCollider). Per un
    // obiettivo con più collider separati (es. i "muro1..muro4" del SoundSystem, sparsi su
    // quasi 20 unità) sceglie il collider già con la posizione scartata (vedi
    // GetApproachReference/approachBias): la selezione tra collider diversi è "a scatti" (o è
    // il più vicino o non lo è), quindi il bias deve intervenire qui, non solo dopo su
    // ClosestPoint, altrimenti nemici diversi bloccherebbero comunque sempre lo stesso muro
    // (quello vicino alla loro direzione di spawn) e il bias sposterebbe solo di poco il punto
    // mirato sulla sua superficie, senza mai fargli scegliere un muro diverso. Se il bersaglio
    // cambia e quello vecchio o quello nuovo è un Raver, aggiorna anche il suo EngagedCount
    // (vedi RaverHealth.Engage/Disengage): non serve a questo script (che non sceglie più tra
    // più Raver vicini in base all'occupazione), ma lo leggono EnemyRangedAttack e
    // IdroSbirro per non far convergere i loro nemici sullo stesso Raver già
    // impegnato da uno Sbirro melee.
    private void SetTarget(Transform newTarget)
    {
        if (newTarget != target)
        {
            if (target != null && target.TryGetComponent(out RaverHealth previousRaver))
            {
                previousRaver.Disengage();
            }
            if (newTarget != null && newTarget.TryGetComponent(out RaverHealth nextRaver))
            {
                nextRaver.Engage();
            }
        }

        target = newTarget;
        targetCollider = newTarget != null ? FindNearestCollider(newTarget, GetApproachReference(newTarget)) : null;
    }

    // Il SoundSystem si attacca solo da davanti (vedi SoundSystem.GetFrontPoint): il punto
    // mirato sta sempre sulla faccia frontale della cassa scelta, e chi si trova di fianco o
    // dietro passa prima dallo spigolo anteriore, così aggira la cassa invece di infilarsi
    // tra una cassa e l'altra o restare incastrato contro il retro. Gli altri bersagli usano
    // il semplice punto più vicino sul collider.
    private Vector3 GetApproachPoint(Vector3 approachReference, out bool isDetour)
    {
        isDetour = false;
        if (targetCollider == null)
        {
            return target.position;
        }

        // Distanza dal centro del nemico alla faccia dell'obiettivo quando è in posizione di
        // attacco: il suo raggio più il distacco voluto, così il bordo resta a objectiveStandoff.
        float objectiveClearance = ownRadius + objectiveStandoff;

        if (target.TryGetComponent(out SoundSystem soundSystem))
        {
            if (soundSystem.IsInFront(targetCollider, rb.position))
            {
                return soundSystem.GetFrontPoint(targetCollider, approachReference, objectiveClearance);
            }

            isDetour = true;
            return soundSystem.GetFrontCornerPoint(targetCollider, rb.position, objectiveClearance);
        }

        Vector3 closestPoint = targetCollider.ClosestPoint(approachReference);
        if (!IsObjective(target))
        {
            return closestPoint;
        }

        // Console: stesso distacco, allontanandosi dalla superficie verso il riferimento
        // d'avvicinamento (o, se questo cade dentro il collider, verso il nemico).
        Vector3 outward = approachReference - closestPoint;
        outward.y = 0f;
        if (outward.sqrMagnitude < 0.0001f)
        {
            outward = rb.position - targetCollider.bounds.center;
            outward.y = 0f;
        }
        return closestPoint + outward.normalized * objectiveClearance;
    }

    // Console/SoundSystem sono gli unici bersagli a cui si applica lo scarto laterale
    // dell'approccio (vedi objectiveApproachSpread/approachBias): un Raver o il Player restano
    // puntati dalla vera posizione, lo spargimento serve solo a non far convergere tutti i
    // nemici sullo stesso punto (o sullo stesso "muro" del SoundSystem) dell'obiettivo.
    private Vector3 GetApproachReference(Transform candidate)
    {
        return candidate != null && IsObjective(candidate) ? rb.position + approachBias : rb.position;
    }

    private static bool IsObjective(Transform candidate)
    {
        return candidate.GetComponent<DJConsoleHealth>() != null || candidate.GetComponent<SoundSystem>() != null;
    }

    // Il collider più vicino al nemico tra quelli del bersaglio: sul suo stesso GameObject,
    // o nei figli (es. i "muro1..muro4" del SoundSystem, che non ha un collider proprio sulla
    // radice). Null se il bersaglio non ha alcun collider: in quel caso FixedUpdate ricade su
    // target.position.
    private static Collider FindNearestCollider(Transform target, Vector3 fromPosition)
    {
        Collider[] colliders = target.GetComponentsInChildren<Collider>();
        Collider nearest = null;
        float nearestSqrDistance = float.MaxValue;

        foreach (Collider collider in colliders)
        {
            float sqrDistance = (collider.ClosestPoint(fromPosition) - fromPosition).sqrMagnitude;
            if (sqrDistance < nearestSqrDistance)
            {
                nearest = collider;
                nearestSqrDistance = sqrDistance;
            }
        }

        return nearest;
    }

    // Punto dell'obiettivo più vicino a fromPosition: usa il collider reale se ce n'è uno
    // (vedi FindNearestCollider), altrimenti il centro del Transform. Necessario per il
    // SoundSystem, la cui radice (muro4) sta a un estremo dell'installazione: i suoi 4
    // collider "muro1..muro4" sono sparsi su quasi 20 unità, quindi misurare sempre dal
    // Transform darebbe una distanza sballata per chi/cosa sta vicino a muro1.
    private static Vector3 GetNearestObjectivePoint(Transform objective, Vector3 fromPosition)
    {
        Collider collider = FindNearestCollider(objective, fromPosition);
        return collider != null ? collider.ClosestPoint(fromPosition) : objective.position;
    }

    // Console/DJ e SoundSystem sono i due obiettivi che la polizia cerca di abbattere: stesso
    // approccio component-based (non layer-based) già usato per i Raver altrove. Distanza
    // misurata contro il collider reale più vicino (vedi GetNearestObjectivePoint), non contro
    // il Transform: per la Console non cambia nulla (un solo collider centrato), ma per il
    // SoundSystem, la cui radice sta a un estremo dell'installazione (muro4), la distanza dal
    // Transform sarebbe gonfiata di parecchio per chi è vicino a muro1.
    //
    // Usato da GetGuardsFor per capire quale obiettivo presidia ciascun Raver dal SUO punto
    // di guardia (un fatto geometrico), non da GetAssignedObjective, che invece decide dove VA
    // il nemico che sta chiamando: i due possono differire (un nemico assegnato alla Console
    // deve comunque contare le guardie della Console dal loro punto di guardia, non dalla
    // propria posizione).
    private static Transform FindNearestObjective(Vector3 fromPosition)
    {
        Transform nearest = null;
        float nearestSqrDistance = float.MaxValue;

        foreach (DJConsoleHealth console in FindObjectsByType<DJConsoleHealth>(FindObjectsSortMode.None))
        {
            float sqrDistance = (GetNearestObjectivePoint(console.transform, fromPosition) - fromPosition).sqrMagnitude;
            if (sqrDistance < nearestSqrDistance)
            {
                nearest = console.transform;
                nearestSqrDistance = sqrDistance;
            }
        }

        foreach (SoundSystem soundSystem in FindObjectsByType<SoundSystem>(FindObjectsSortMode.None))
        {
            float sqrDistance = (GetNearestObjectivePoint(soundSystem.transform, fromPosition) - fromPosition).sqrMagnitude;
            if (sqrDistance < nearestSqrDistance)
            {
                nearest = soundSystem.transform;
                nearestSqrDistance = sqrDistance;
            }
        }

        return nearest;
    }

    // Partita finita (boss e IdroSbirro abbattuti, vedi RoutState): si torna da dove si è
    // venuti, a ritroso lungo il percorso d'arrivo fino allo spawner, e lì si sparisce. Dentro
    // l'arena gli ostacoli si aggirano col NavMesh come nell'inseguimento (GetNextWaypoint).
    private void FleeHome()
    {
        if (routHome == null)
        {
            routHome = new RoutHome(spawnPoint, arrivalPoints, pathIndex, pathReachRadius);
        }

        if (!routHome.TryGetNextStop(rb.position, out Vector3 stop))
        {
            Destroy(gameObject);
            return;
        }

        Vector3 toWaypoint = GetNextWaypoint(stop, out _) - rb.position;
        toWaypoint.y = 0f;
        Move(toWaypoint);
        Rotate(toWaypoint);
    }

    // stopDistance: sotto questa distanza non avanza oltre (vedi il campo stopDistance);
    // lasciata a 0 (default) per chi insegue un punto esatto da raggiungere davvero, come i
    // waypoint di un percorso o la direzione di fuga, che non devono fermarsi prima.
    private void Move(Vector3 direction, float stopDistance = 0f)
    {
        if (direction.magnitude <= stopDistance)
        {
            return;
        }

        if (stuckTimer >= StuckSeconds)
        {
            stuckTimer = 0f;
            sidestepTimer = SidestepSeconds;
            sidestepSign = -sidestepSign;
            sidestepDirection = Vector3.Cross(Vector3.up, direction).normalized * sidestepSign;
        }

        Vector3 heading = sidestepTimer > 0f ? sidestepDirection : direction.normalized;
        Vector3 nextPosition = rb.position + heading * moveSpeed * Time.fixedDeltaTime;
        movedThisStep = true;
        rb.MovePosition(ClampToFloor(nextPosition));
    }

    // Tiene il nemico dentro l'arena, stesso motivo/fix già applicato in EnemyRangedAttack.
    // In fuga (RoutState) no: deve uscirne per tornare allo spawner (vedi FleeHome).
    private Vector3 ClampToFloor(Vector3 position)
    {
        if (FloorBounds.Instance == null || RoutState.IsActive)
        {
            return position;
        }

        Bounds bounds = FloorBounds.Instance.Bounds;

        // I nemici spawnano fuori dal perimetro (spawner oltre i muri, che i nemici
        // ignorano - vedi ArenaBounds.IgnoreCollisionsForEnemy) e devono poter camminare
        // "da fuori a dentro": finché non hanno raggiunto una volta l'interno dell'arena
        // non li si clampa, altrimenti verrebbero risucchiati al bordo al primo passo. Una
        // volta entrati il clamp torna attivo e li tiene dentro come prima.
        if (!hasEnteredArena)
        {
            bool insideArena = position.x > bounds.min.x && position.x < bounds.max.x
                && position.z > bounds.min.z && position.z < bounds.max.z;
            if (insideArena)
            {
                hasEnteredArena = true;
            }
            else
            {
                return position;
            }
        }

        position.x = Mathf.Clamp(position.x, bounds.min.x, bounds.max.x);
        position.z = Mathf.Clamp(position.z, bounds.min.z, bounds.max.z);
        return position;
    }

    private void Rotate(Vector3 toTarget)
    {
        // Se il nemico è già praticamente addosso al bersaglio, non ruotare (direzione instabile).
        if (toTarget.sqrMagnitude < 0.0001f)
        {
            return;
        }

        // Niente Slerp: il nemico deve essere sempre orientato esattamente verso il bersaglio, non con un turning graduale.
        Quaternion targetRotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        rb.MoveRotation(targetRotation);
    }
}
