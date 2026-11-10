using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// L'IdroSbirro: un camion idrante. Sul prefab (IdroSbirro.prefab) insieme a EnemyHealth, che lo
// rende danneggiabile come gli altri Sbirro, e IdroSbirroRig, che ne anima ruote, sterzo e
// idrante.
//
// Lo genera EnemySpawner, uno per spawner e una volta sola, dopo idroSbirroDelay (2 minuti). Poi:
//  - arrivo: segue il percorso dello spawner (EnemyPath, quello degli Sbirro, vedi SetRoute) e
//    va alla sua postazione, da veicolo: avanza lungo il muso e sterza per gradi
//    (VehicleTuning). Niente lo ferma: il
//    Rigidbody è cinematico, e quello che gli sta davanti lo sposta via (vedi ShoveObstacles).
//    Passa attraverso solo i muri dell'arena, il pavimento, sound system e Console.
//    La postazione è il punto di stationPoints più vicino al suo spawner fra quelli non già
//    presi dall'altro camion (vedi PickStation). Lì fa una manovra di parcheggio e si mette col
//    muso a metà fra sound system e Console (vedi Park);
//  - postazione: da lì non si muove più e bagna i Raver con getti d'acqua (WaterJet, vedi Fire).
//    Colpisce solo quelli dalla sua parte rispetto alla linea sound system - Console, alternando:
//    uno del sound system, uno della Console, e così via, cambiando ogni volta che ne manda a
//    terra uno. Gira solo l'idrante, di ±turretMaxAngle gradi rispetto al muso: i Raver più in
//    là non li può bagnare (vedi IsInTurretArc).
//
// La partita è vinta quando sono morti tutti gli IdroSbirro e anche il boss, CyberGiorgia, in
// qualunque ordine (vedi FinalVictory): da lì non arrivano più Sbirro, quelli rimasti tornano
// agli spawner da cui sono venuti (RoutState), e appena se ne sono andati è la vittoria
// (BossDefeatedSequence, poi WinState).
[RequireComponent(typeof(Rigidbody))]
public class IdroSbirro : MonoBehaviour
{
    private enum State { Arriving, Parking, Stationed }

    private static readonly List<IdroSbirro> alive = new List<IdroSbirro>();

    [Header("Guida")]
    [SerializeField] private VehicleTuning driving = new VehicleTuning
    {
        maxSpeed = 7f,
        acceleration = 5f,
        minTurnRadius = 6f,
        steerRate = 1.8f,
        fullLockAngle = 35f,
        steerDeadZone = 3f,
        minThrottle = 0.6f,
    };
    [Tooltip("A quanti metri dal punto di arrivo si considera arrivato.")]
    [SerializeField] private float arriveDistance = 1.5f;
    [Tooltip("Dove sta la postazione lungo la linea dal centro del sound system (0) a quello della Console (1).")]
    [Range(0f, 1f)]
    [SerializeField] private float stationAlongLine = 0.5f;
    [Tooltip("A quanti metri di fianco a quella linea sta la postazione, dalla parte da cui arriva il camion. Più è al largo, più Raver dei due obiettivi stanno nei ±45° dell'idrante (a 24 quasi tutti), ma oltre i 20 il camion sta sull'ingresso dell'arena e blocca gli Sbirro che entrano.")]
    [SerializeField] private float stationSideDistance = 17f;
    [Tooltip("Le postazioni dei camion: ognuno va nella più vicina al suo spawner fra quelle non già prese. Vuoto = postazione calcolata con stationAlongLine/stationSideDistance.")]
    [SerializeField] private Vector3[] stationPoints =
    {
        new Vector3(14.69f, 2.04f, 5.07f),
        new Vector3(-8.01f, 2.04f, 6.13f),
    };
    [Tooltip("Gradi al secondo della manovra di parcheggio, all'arrivo: il camion si mette col muso a metà fra sound system e Console, così l'idrante arriva a tutti e due. Dopo non si gira più.")]
    [SerializeField] private float parkTurnSpeed = 35f;
    [Tooltip("A quanti metri da un waypoint del percorso si passa al successivo: largo, perché un camion non gira sul posto.")]
    [SerializeField] private float waypointReachDistance = 3f;

    [Header("Idrante")]
    [Tooltip("Di quanti gradi gira l'idrante a destra e a sinistra rispetto al muso.")]
    [SerializeField] private float turretMaxAngle = 45f;
    [Tooltip("Gradi al secondo con cui gira l'idrante.")]
    [SerializeField] private float turretTurnSpeed = 60f;
    [Tooltip("Senza un Raver da bagnare l'idrante va avanti e indietro fra i due finecorsa: secondi per un giro completo.")]
    [SerializeField] private float turretSweepPeriod = 4f;
    [SerializeField] private float fireInterval = 2.2f;
    [Tooltip("Danno di un getto a un Raver.")]
    [SerializeField] private float damagePerHit = 50f;

    [Header("Getto (WaterJet)")]
    [Tooltip("Secondi perché l'acqua arrivi al Raver: lì arriva il danno.")]
    [SerializeField] private float jetStartTime = 0.45f;
    [Tooltip("Secondi in cui il getto resta sul Raver.")]
    [SerializeField] private float jetSustainTime = 1.6f;
    [Tooltip("Secondi in cui il getto si ritira, perdendo pressione.")]
    [SerializeField] private float jetEndTime = 0.55f;
    [SerializeField] private float jetWidth = 0.3f;
    [Tooltip("Gocce al secondo, per metro di getto, che si staccano lungo il raggio.")]
    [SerializeField] private float beamSprayRate = 120f;
    [Tooltip("Gocce al secondo degli schizzi sul Raver mentre il getto lo colpisce.")]
    [SerializeField] private float impactSplashRate = 600f;

    [Header("Morte")]
    [Tooltip("Grandezza dell'esplosione (TruckExplosion) quando salta in aria: a 1 la palla di fuoco è larga circa 5 m.")]
    [SerializeField] private float explosionScale = 1f;
    [Tooltip("MiniNukePuff.shader (particles/MiniNuke3D), usato anche da TruckExplosion: assegnato qui finisce anche nelle build, dove Shader.Find non lo troverebbe.")]
    [SerializeField] private Shader explosionShader;
    [Tooltip("Tremolio dello schermo all'esplosione, da 0 a 1 (CameraShake).")]
    [Range(0f, 1f)]
    [SerializeField] private float explosionShake = 0.8f;
    [SerializeField] private Color jetColor = new Color(0.45f, 0.8f, 1f, 0.9f);

    private Rigidbody rb;
    private EnemyHealth health;
    private IdroSbirroRig rig;
    private State state = State.Arriving;
    private Vector3 station;
    private Vector3[] route = new Vector3[0];
    private int routeIndex;
    private bool hasEnteredArena;
    private bool hasStation;

    private WaterJet jet;
    private RaverHealth jetRaver; // il Raver su cui è aperto il getto

    // I collider del camion, con la loro posa rispetto al Rigidbody (per ShoveObstacles).
    private Collider[] bodyShapes = new Collider[0];
    private Vector3[] bodyShapeOffsets = new Vector3[0];
    private Quaternion[] bodyShapeRotations = new Quaternion[0];
    private readonly Collider[] overlaps = new Collider[32];

    // Lato dell'arena (rispetto alla linea sound system - Console) e obiettivo di turno.
    private Transform soundSystem;
    private Transform console;
    private Vector3 soundCenter;
    private Vector3 consoleCenter;
    private float side;
    private bool aimingAtSoundSystem = true;

    private Transform target;
    private float vehicleSpeed;
    private float steerInput;
    private float turretAngle; // rispetto al muso, in gradi, positivo a destra
    private float fireTimer;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        // Cinematico: MovePosition non lo ferma contro muri, sound system o altri corpi, e
        // nessuno lo può spingere.
        rb.isKinematic = true;
        health = GetComponent<EnemyHealth>();
        rig = GetComponent<IdroSbirroRig>();
        gameObject.AddComponent<IdroSbirroHealthBar>();

        var shapes = new List<Collider>();
        foreach (Collider shape in GetComponentsInChildren<Collider>())
        {
            if (!shape.isTrigger)
            {
                shapes.Add(shape);
            }
        }
        bodyShapes = shapes.ToArray();
        bodyShapeOffsets = new Vector3[bodyShapes.Length];
        bodyShapeRotations = new Quaternion[bodyShapes.Length];
        Quaternion inverse = Quaternion.Inverse(transform.rotation);
        for (int i = 0; i < bodyShapes.Length; i++)
        {
            bodyShapeOffsets[i] = inverse * (bodyShapes[i].transform.position - transform.position);
            bodyShapeRotations[i] = inverse * bodyShapes[i].transform.rotation;
        }
        fireTimer = fireInterval * 0.5f; // il primo getto non si fa aspettare un intervallo intero

        CreateNavObstacle();
    }

    // Parcheggiato: un NavMeshObstacle che si ritaglia il suo buco nel NavMesh di
    // EnemyNavMesh, così Sbirro e Raver (NavPathFollower) lo aggirano invece di tirargli dritto
    // contro. carveOnlyStationary: il buco si ritaglia a camion fermo (in postazione), niente
    // ricalcoli mentre guida. La sagoma è quella del BoxCollider del camion.
    private void CreateNavObstacle()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null || box.isTrigger)
        {
            return;
        }

        NavMeshObstacle obstacle = gameObject.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = box.center;
        obstacle.size = box.size;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = true;
    }

    void OnEnable()
    {
        alive.Add(this);
    }

    void OnDisable()
    {
        alive.Remove(this);
    }

    // Chiamato da EnemySpawner subito dopo averlo generato: i waypoint da seguire prima della
    // postazione.
    public void SetRoute(Vector3[] waypoints)
    {
        route = waypoints ?? new Vector3[0];
        routeIndex = 0;
    }

    void Start()
    {
        float floorY = FloorBounds.Instance != null ? FloorBounds.Instance.Bounds.max.y : transform.position.y;
        Vector3 start = transform.position;
        start.y = floorY;

        foreach (IObjective objective in ObjectiveRegistry.All)
        {
            if (objective is SoundSystem)
            {
                soundSystem = objective.transform;
                soundCenter = ShapeCenter(soundSystem);
            }
            else if (objective is DJConsoleHealth)
            {
                console = objective.transform;
                consoleCenter = ShapeCenter(console);
            }
        }

        station = PickStation(start);
        station.y = floorY;
        side = SideOf(station);

        // Col muso verso la prima tappa.
        Vector3 firstStop = route.Length > 0 ? route[0] : station;
        Vector3 toFirstStop = Flat(firstStop - start);
        Quaternion facing = toFirstStop.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toFirstStop) : transform.rotation;
        rb.position = start;
        rb.rotation = facing;
        transform.SetPositionAndRotation(start, facing);
    }

    // Il punto di stationPoints più vicino allo spawner, saltando quelli già presi dagli altri
    // IdroSbirro (fanno Start uno dopo l'altro, quindi il secondo trova occupato il primo).
    private Vector3 PickStation(Vector3 spawnPoint)
    {
        Vector3? best = null;
        float bestDistance = float.MaxValue;
        foreach (Vector3 point in stationPoints)
        {
            bool taken = false;
            foreach (IdroSbirro other in alive)
            {
                if (other != this && other.hasStation && Flat(other.station - point).sqrMagnitude < 1f)
                {
                    taken = true;
                    break;
                }
            }

            float distance = Flat(point - spawnPoint).sqrMagnitude;
            if (!taken && distance < bestDistance)
            {
                bestDistance = distance;
                best = point;
            }
        }

        hasStation = true;
        return best ?? ComputeStation(spawnPoint);
    }

    // Sulla linea sound system - Console a stationAlongLine del tragitto, poi stationSideDistance
    // di fianco, dalla parte da cui arriva il camion. Non si parte dal giocoliere: RaverJuggler
    // sta sul prefab di tutti i Raver, e cercarlo dava un Raver qualsiasi (finivano tutti e due
    // vicino al sound system).
    private Vector3 ComputeStation(Vector3 spawnPoint)
    {
        Vector3 center = soundSystem != null && console != null
            ? Vector3.Lerp(soundCenter, consoleCenter, stationAlongLine)
            : spawnPoint;

        Vector3 lateral = soundSystem != null && console != null
            ? Vector3.Cross(Vector3.up, Flat(consoleCenter - soundCenter)).normalized
            : Flat(spawnPoint - center).normalized;
        if (Vector3.Dot(lateral, spawnPoint - center) < 0f)
        {
            lateral = -lateral;
        }
        return center + lateral * stationSideDistance;
    }

    // Il centro della sagoma (i collider), non il pivot: quello del sound system sta su un
    // bordo del palco, e prenderlo come centro spostava postazioni e lati verso il sound.
    private static Vector3 ShapeCenter(Transform objective)
    {
        Collider[] shapes = objective.GetComponentsInChildren<Collider>();
        if (shapes.Length == 0)
        {
            return objective.position;
        }

        Bounds bounds = shapes[0].bounds;
        foreach (Collider shape in shapes)
        {
            bounds.Encapsulate(shape.bounds);
        }
        return bounds.center;
    }

    void OnDestroy()
    {
        alive.Remove(this);
        SetTarget(null);

        // Morto ammazzato (non scaricato col cambio di scena): salta in aria, e se anche il boss e
        // l'altro camion sono già abbattuti la partita è vinta (vedi FinalVictory).
        if (health != null && health.CurrentHealth <= 0f)
        {
            SpawnDeathExplosion();
            FinalVictory.IdroSbirroKilled();
        }
    }

    // L'esplosione (TruckExplosion) a terra in mezzo al camion. Il tremolio dello schermo è
    // quello del gioco (CameraShake, applicato da CameraFollow).
    private void SpawnDeathExplosion()
    {
        Vector3 center = transform.position;
        bool found = false;
        Bounds bounds = default;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r is MeshRenderer || r is SkinnedMeshRenderer)
            {
                if (!found)
                {
                    bounds = r.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }
        }
        if (found)
        {
            center = bounds.center;
        }
        center.y = FloorBounds.Instance != null ? FloorBounds.Instance.Bounds.max.y : transform.position.y;

        TruckExplosion.Spawn(center, explosionScale, explosionShader);
        CameraShake.Add(explosionShake);
    }

    // Il camion è cinematico, quindi la fisica non lo ferma e gli oggetti senza Rigidbody non
    // li sposterebbe: qui ogni collider che il camion sta compenetrando viene spinto fuori, in
    // orizzontale. Chi ha un Rigidbody (Player, Raver, Sbirro) si sposta col suo Rigidbody, il
    // resto (palme, amache, fuochi...) spostandone il Transform.
    private void ShoveObstacles()
    {
        for (int i = 0; i < bodyShapes.Length; i++)
        {
            Collider shape = bodyShapes[i];
            if (!shape.enabled)
            {
                continue;
            }

            Vector3 shapePosition = rb.position + rb.rotation * bodyShapeOffsets[i];
            Quaternion shapeRotation = rb.rotation * bodyShapeRotations[i];
            Bounds area = shape.bounds;
            area.center += shapePosition - shape.transform.position;
            int count = Physics.OverlapBoxNonAlloc(area.center, area.extents + Vector3.one * 0.5f, overlaps, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);

            for (int j = 0; j < count; j++)
            {
                Collider other = overlaps[j];
                if (!CanBeShoved(other))
                {
                    continue;
                }

                if (!Physics.ComputePenetration(shape, shapePosition, shapeRotation,
                        other, other.transform.position, other.transform.rotation,
                        out Vector3 direction, out float depth))
                {
                    continue;
                }

                // Sempre in orizzontale. Se la spinta sarebbe quasi solo verticale il camion ci
                // sta sopra (un terreno fuori dall'arena, una pedana): quello non si sposta.
                Vector3 push = Flat(-direction);
                if (push.sqrMagnitude < 0.25f)
                {
                    continue;
                }
                push = push.normalized * (depth + 0.05f);

                Rigidbody body = other.attachedRigidbody;
                if (body != null && !body.isKinematic)
                {
                    body.position += push;
                }
                else
                {
                    Transform moved = body != null ? body.transform : other.transform;
                    moved.position += push;
                }
            }
        }
    }

    private bool CanBeShoved(Collider other)
    {
        if (other.attachedRigidbody == rb || other.transform.IsChildOf(transform))
        {
            return false;
        }

        return other.GetComponentInParent<ArenaBounds>() == null
            && other.GetComponentInParent<FloorBounds>() == null
            && other.GetComponentInParent<SoundSystem>() == null
            && other.GetComponentInParent<DJConsoleHealth>() == null
            && other.GetComponentInParent<IdroSbirro>() == null;
    }

    void FixedUpdate()
    {
        ShoveObstacles();

        if (RoutState.IsActive)
        {
            Drive(rb.position);
            return;
        }

        if (state == State.Arriving && routeIndex < route.Length)
        {
            Drive(route[routeIndex], false);
            AimTurret(null);
            if (Flat(route[routeIndex] - rb.position).magnitude <= waypointReachDistance)
            {
                routeIndex++;
            }
            return;
        }

        if (state == State.Arriving)
        {
            Drive(station);
            AimTurret(null);
            if (Flat(station - rb.position).magnitude <= arriveDistance)
            {
                state = State.Parking;
            }
            return;
        }

        if (state == State.Parking)
        {
            AimTurret(null);
            if (Park())
            {
                state = State.Stationed;
                LogCoverage();
            }
            return;
        }

        // In postazione il camion resta fermo e gira solo l'idrante.
        Drive(rb.position);
        UpdateTarget();
        AimTurret(target);
        UpdateJet();
        if (target == null)
        {
            return;
        }

        float bearing = Bearing(FromTurret(target));
        fireTimer += Time.fixedDeltaTime;
        bool jetIdle = jet == null || jet.CurrentPhase == WaterJet.Phase.Idle;
        if (jetIdle && fireTimer >= fireInterval && Mathf.Abs(Mathf.DeltaAngle(turretAngle, bearing)) <= 4f)
        {
            fireTimer = 0f;
            Fire();
        }
    }

    // Il bersaglio resta quello finché non va a terra; poi tocca all'altro obiettivo. Se
    // all'obiettivo di turno non resta nessuno dalla sua parte, si passa direttamente all'altro.
    private void UpdateTarget()
    {
        if (target != null)
        {
            if (target.TryGetComponent(out RaverHealth current) && !current.IsDown)
            {
                if (IsInTurretArc(target) && !IsReturningToGuard(current))
                {
                    return;
                }
                // Uscito dal raggio dell'idrante, o sta tornando alla guardia: si cerca un altro
                // Raver dello stesso obiettivo.
            }
            else
            {
                aimingAtSoundSystem = !aimingAtSoundSystem; // abbattuto (o sparito): si cambia obiettivo
            }
        }

        Transform next = FindRaver(aimingAtSoundSystem);
        if (next == null)
        {
            aimingAtSoundSystem = !aimingAtSoundSystem;
            next = FindRaver(aimingAtSoundSystem);
        }
        SetTarget(next);
    }

    // Manovra di parcheggio: si gira (ruote sterzate, un filo di rotolamento) finché il muso non
    // punta a metà fra sound system e Console. true quando ha finito.
    private bool Park()
    {
        Vector3 toSound = Flat(soundCenter - rb.position).normalized;
        Vector3 toConsole = Flat(consoleCenter - rb.position).normalized;
        Vector3 wanted = toSound + toConsole;
        if (soundSystem == null || console == null || wanted.sqrMagnitude < 0.0001f)
        {
            return true;
        }

        float error = Vector3.SignedAngle(Flat(rb.rotation * Vector3.forward), wanted, Vector3.up);
        float step = Mathf.Clamp(error, -parkTurnSpeed * Time.fixedDeltaTime, parkTurnSpeed * Time.fixedDeltaTime);
        rb.MoveRotation(rb.rotation * Quaternion.AngleAxis(step, Vector3.up));

        bool done = Mathf.Abs(error) <= 1f;
        vehicleSpeed = 0f;
        steerInput = Mathf.MoveTowards(steerInput, done ? 0f : Mathf.Sign(error), driving.steerRate * Time.fixedDeltaTime);
        if (rig != null)
        {
            rig.SetMotion(done ? 0f : parkTurnSpeed * Mathf.Deg2Rad * 2f, steerInput);
        }
        return done;
    }

    // Una riga nel log all'arrivo in postazione: quanti Raver del suo lato l'idrante può
    // raggiungere, per obiettivo. Serve a tarare stationAlongLine/stationSideDistance.
    private void LogCoverage()
    {
        int soundIn = 0, soundAll = 0, consoleIn = 0, consoleAll = 0;
        foreach (RaverHealth raver in RaverHealth.All)
        {
            if (!IsOnMySide(raver))
            {
                continue;
            }

            bool inArc = IsInTurretArc(raver.transform);
            if (DefendsSoundSystem(raver))
            {
                soundAll++;
                if (inArc) soundIn++;
            }
            else
            {
                consoleAll++;
                if (inArc) consoleIn++;
            }
        }
        Debug.Log($"[IdroSbirro] {name} in postazione a {rb.position}: a tiro {soundIn}/{soundAll} Raver del sound system, {consoleIn}/{consoleAll} della Console.", this);
    }

    // Il Raver in piedi più vicino fra quelli dell'obiettivo indicato, dalla stessa parte di
    // questo IdroSbirro e a tiro d'idrante.
    private Transform FindRaver(bool ofSoundSystem)
    {
        Transform nearest = null;
        float best = float.MaxValue;
        foreach (RaverHealth raver in RaverHealth.All)
        {
            if (raver.IsDown || IsReturningToGuard(raver))
            {
                continue;
            }

            if (DefendsSoundSystem(raver) != ofSoundSystem || !IsOnMySide(raver) || !IsInTurretArc(raver.transform))
            {
                continue;
            }

            float distance = Flat(raver.transform.position - rb.position).sqrMagnitude;
            if (distance < best)
            {
                best = distance;
                nearest = raver.transform;
            }
        }
        return nearest;
    }

    // Il camion in postazione non si gira: si possono bagnare solo i Raver che l'idrante
    // raggiunge coi suoi ±turretMaxAngle gradi.
    private bool IsInTurretArc(Transform raver)
    {
        return Mathf.Abs(Bearing(FromTurret(raver))) <= turretMaxAngle;
    }

    // Di chi è il Raver: quello che difende (RaverChase), e se non lo dice l'obiettivo più vicino
    // al suo punto di guardia. Il punto di guardia e non la posizione attuale, perché un Raver
    // che insegue uno Sbirro o torna dalla chillout non cambia squadra.
    private bool DefendsSoundSystem(RaverHealth raver)
    {
        if (raver.TryGetComponent(out RaverChase chase) && chase.DefendedObjective != null)
        {
            return chase.DefendedObjective == soundSystem;
        }
        Vector3 home = GuardPositionOf(raver);
        return Flat(home - soundCenter).sqrMagnitude <= Flat(home - consoleCenter).sqrMagnitude;
    }

    // Chi sta rientrando al punto di guardia (RaverChase) non si bagna.
    private static bool IsReturningToGuard(RaverHealth raver)
    {
        return raver.TryGetComponent(out RaverChase chase) && chase.IsReturningToGuard;
    }

    private bool IsOnMySide(RaverHealth raver)
    {
        return SideOf(GuardPositionOf(raver)) * side >= 0f;
    }

    private static Vector3 GuardPositionOf(RaverHealth raver)
    {
        return raver.TryGetComponent(out RaverChase chase) ? chase.GuardPosition : raver.transform.position;
    }

    // Da che parte della linea sound system - Console sta il punto: il segno è quello che conta.
    private float SideOf(Vector3 position)
    {
        if (soundSystem == null || console == null)
        {
            return 1f;
        }
        Vector3 line = Flat(consoleCenter - soundCenter);
        return Vector3.Cross(line, Flat(position - soundCenter)).y;
    }

    // Stessa guida di RaverChase.DriveVehicle: volante che si muove per gradi, gas che cala in
    // curva, gradi girati in proporzione alla strada fatta. destination uguale alla posizione
    // attuale = frena e resta fermo.
    // brakeAtDestination falso per i waypoint di passaggio: ci si passa senza fermarsi.
    private void Drive(Vector3 destination, bool brakeAtDestination = true)
    {
        Vector3 heading = Flat(rb.rotation * Vector3.forward).normalized;
        Vector3 toDestination = Flat(destination - rb.position);
        bool arrived = toDestination.magnitude <= 0.05f;

        float headingError = arrived ? 0f : Vector3.SignedAngle(heading, toDestination, Vector3.up);
        float wantedSteer = Mathf.Abs(headingError) <= driving.steerDeadZone
            ? 0f
            : Mathf.Clamp(headingError / Mathf.Max(1f, driving.fullLockAngle), -1f, 1f);
        steerInput = Mathf.MoveTowards(steerInput, wantedSteer, driving.steerRate * Time.fixedDeltaTime);

        // Freno tirato in tempo per fermarsi sul punto, non qualche metro dopo.
        float brakingDistance = vehicleSpeed * vehicleSpeed / (2f * Mathf.Max(0.01f, driving.acceleration));
        bool braking = arrived || (brakeAtDestination && destination != rb.position && toDestination.magnitude <= brakingDistance && state == State.Arriving);
        float throttle = Mathf.Lerp(1f, driving.minThrottle, Mathf.Abs(steerInput));
        float targetSpeed = braking ? 0f : driving.maxSpeed * throttle;
        vehicleSpeed = Mathf.MoveTowards(vehicleSpeed, targetSpeed, driving.acceleration * Time.fixedDeltaTime);

        float distance = vehicleSpeed * Time.fixedDeltaTime;
        if (distance > 0f)
        {
            // Vicino alla meta il raggio di sterzata si stringe (manovra a passo d'uomo): con
            // quello pieno un punto che cade dentro la curva non si raggiunge mai, e il camion ci
            // girerebbe attorno all'infinito.
            float turnRadius = brakeAtDestination
                ? Mathf.Min(driving.minTurnRadius, Mathf.Max(0.5f, toDestination.magnitude * 0.5f))
                : driving.minTurnRadius;
            float degrees = distance / Mathf.Max(0.01f, turnRadius) * Mathf.Rad2Deg * steerInput;
            if (!arrived)
            {
                degrees = Mathf.Clamp(degrees, -Mathf.Abs(headingError), Mathf.Abs(headingError));
            }
            rb.MovePosition(ClampToFloor(rb.position + heading * distance));
            rb.MoveRotation(rb.rotation * Quaternion.AngleAxis(degrees, Vector3.up));
        }

        if (rig != null)
        {
            rig.SetMotion(vehicleSpeed, steerInput);
        }
    }

    // Come gli altri Sbirro ignora i muri dell'arena (EnemyHealth, ArenaBounds): nasce fuori,
    // sullo spawner, e una volta entrato lo si tiene sul pavimento.
    private Vector3 ClampToFloor(Vector3 position)
    {
        if (FloorBounds.Instance == null)
        {
            return position;
        }

        Bounds floor = FloorBounds.Instance.Bounds;
        bool inside = position.x > floor.min.x && position.x < floor.max.x
            && position.z > floor.min.z && position.z < floor.max.z;
        if (!hasEnteredArena)
        {
            hasEnteredArena = inside;
            return position;
        }

        position.x = Mathf.Clamp(position.x, floor.min.x, floor.max.x);
        position.z = Mathf.Clamp(position.z, floor.min.z, floor.max.z);
        return position;
    }

    // Idrante sul bersaglio (entro i finecorsa), o avanti e indietro fra i finecorsa se non ce n'è.
    private void AimTurret(Transform aimAt)
    {
        float wanted = aimAt != null
            ? Mathf.Clamp(Bearing(FromTurret(aimAt)), -turretMaxAngle, turretMaxAngle)
            : turretMaxAngle * Mathf.Sin(Time.time * 2f * Mathf.PI / Mathf.Max(0.1f, turretSweepPeriod));
        turretAngle = Mathf.MoveTowardsAngle(turretAngle, wanted, turretTurnSpeed * Time.fixedDeltaTime);

        if (rig != null)
        {
            rig.SetTurretYaw(turretAngle);
        }
    }

    // Direzione in pianta dal perno dell'idrante al punto in cui arriva il getto (il centro del
    // Raver): quella in cui l'idrante deve puntare davvero.
    private Vector3 FromTurret(Transform aimAt)
    {
        Vector3 pivot = rig != null && rig.TurretPivot != null ? rig.TurretPivot.position : rb.position;
        return Flat(AimPoint(aimAt) - pivot);
    }

    private static Vector3 AimPoint(Transform aimAt)
    {
        Collider shape = aimAt.GetComponentInChildren<Collider>();
        return shape != null ? shape.bounds.center : aimAt.position;
    }

    // Angolo della direzione rispetto al muso (o a dove guarda l'ugello nella posa di partenza,
    // vedi IdroSbirroRig.NozzleBaseYaw): positivo a destra.
    private float Bearing(Vector3 flatDirection)
    {
        float nozzleYaw = rig != null ? rig.NozzleBaseYaw : 0f;
        return Mathf.DeltaAngle(nozzleYaw, Vector3.SignedAngle(Flat(rb.rotation * Vector3.forward), flatDirection, Vector3.up));
    }

    // Il getto è il WaterJet (models/idrosbirro/Idrante), su un figlio del camion: parte
    // dall'ugello e segue il Raver mentre si sposta. Il danno arriva una volta sola, quando
    // l'acqua lo raggiunge (OnHitStart). Va dritto: niente arco e niente oscillazione del punto
    // di controllo, che col getto teso lo farebbe serpeggiare.
    private void Fire()
    {
        if (!target.TryGetComponent(out RaverHealth raver) || raver.IsDown)
        {
            return;
        }

        if (jet == null)
        {
            jet = CreateJet();
        }
        jet.arcHeight = 0f;
        jet.wobble = 0f;
        jet.beamSprayRate = beamSprayRate;
        jet.thickness = jetWidth;
        jet.waterColor = jetColor;
        jet.startTime = jetStartTime;
        jet.sustainTime = Mathf.Max(0.01f, jetSustainTime);
        jet.endTime = jetEndTime;

        jetRaver = raver;
        jet.Fire(rig != null && rig.Muzzle != null ? rig.Muzzle : transform, AimPoint(target));
    }

    private WaterJet CreateJet()
    {
        var holder = new GameObject("WaterJet");
        holder.transform.SetParent(transform, false);
        WaterJet created = holder.AddComponent<WaterJet>();
        created.impactSplash = CreateSplash(holder.transform, "SchizziImpatto", SplashKind.Impact);
        created.finalSplash = CreateSplash(holder.transform, "SchizzoFinale", SplashKind.Final);
        created.finalSplashCount = 250;
        created.beamSpray = CreateSplash(holder.transform, "SchizziGetto", SplashKind.Beam);
        created.OnHitStart += () =>
        {
            if (jetRaver != null && !jetRaver.IsDown && !IsReturningToGuard(jetRaver))
            {
                jetRaver.TakeDamage(damagePerHit);
            }
        };
        return created;
    }

    private enum SplashKind { Impact, Final, Beam }

    // Gli schizzi dove l'acqua colpisce, con le impostazioni del README del WaterJet: un cono
    // di gocce verso l'alto che ricadono. Impact = quelli che durano finché il getto è sul
    // Raver (WaterJet li mette in Play/Stop); Final = lo schizzo finale, che WaterJet emette
    // in un colpo solo (Emit) quando il getto si chiude; Beam = le gocce lungo il raggio, che
    // WaterJet emette una per una con posizione e velocità sue (rate 0 qui).
    private ParticleSystem CreateSplash(Transform parent, string splashName, SplashKind kind)
    {
        bool continuous = kind == SplashKind.Impact;
        var splashObject = new GameObject(splashName);
        splashObject.transform.SetParent(parent, false);
        splashObject.transform.rotation = Quaternion.Euler(-90f, 0f, 0f); // il cono guarda in su

        ParticleSystem splash = splashObject.AddComponent<ParticleSystem>();
        splash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = splash.main;
        main.loop = continuous;
        main.playOnAwake = false;
        main.duration = 1f;
        main.startLifetime = kind == SplashKind.Beam ? new ParticleSystem.MinMaxCurve(0.25f, 0.55f) : new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
        main.startSpeed = continuous ? new ParticleSystem.MinMaxCurve(2.5f, 6.5f) : new ParticleSystem.MinMaxCurve(2.5f, 7f);
        main.startSize = kind == SplashKind.Beam ? new ParticleSystem.MinMaxCurve(0.04f, 0.12f) : new ParticleSystem.MinMaxCurve(0.06f, 0.24f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(jetColor.r, jetColor.g, jetColor.b, 0.9f),
            new Color(0.9f, 0.97f, 1f, 0.9f));
        main.gravityModifier = 1.5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 4000;

        ParticleSystem.EmissionModule emission = splash.emission;
        emission.rateOverTime = continuous ? impactSplashRate : 0f;

        ParticleSystem.ShapeModule shape = splash.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 70f;
        shape.radius = 0.4f;

        ParticleSystem.SizeOverLifetimeModule size = splash.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

        ParticleSystemRenderer splashRenderer = splashObject.GetComponent<ParticleSystemRenderer>();
        splashRenderer.material = new Material(MaterialLibrary.SpriteDefault);
        splashRenderer.material.mainTexture = RoundParticleTexture.Get();
        splashRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        splashRenderer.receiveShadows = false;
        return splash;
    }

    // Mentre il getto è aperto la punta resta sul Raver che si muove; se va a terra, o il
    // bersaglio cambia, l'idrante chiude.
    private void UpdateJet()
    {
        if (jet == null || jet.CurrentPhase == WaterJet.Phase.Idle)
        {
            return;
        }

        if (jetRaver == null || jetRaver.IsDown || target != jetRaver.transform)
        {
            jet.Stop();
            return;
        }
        jet.targetPoint = AimPoint(jetRaver.transform);
    }

    // Tiene aggiornato RaverHealth.EngagedCount come fanno gli altri Sbirro (EnemyChase.SetTarget).
    private void SetTarget(Transform newTarget)
    {
        if (newTarget == target)
        {
            return;
        }

        if (target != null && target.TryGetComponent(out RaverHealth previous))
        {
            previous.Disengage();
        }
        if (newTarget != null && newTarget.TryGetComponent(out RaverHealth next))
        {
            next.Engage();
        }
        target = newTarget;
    }

    private static Vector3 Flat(Vector3 direction)
    {
        direction.y = 0f;
        return direction;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(jetColor.r, jetColor.g, jetColor.b, 0.6f);
        Gizmos.DrawWireSphere(Application.isPlaying ? station : transform.position, arriveDistance);
        if (target != null)
        {
            Gizmos.DrawLine(transform.position, target.position);
        }
    }
}
