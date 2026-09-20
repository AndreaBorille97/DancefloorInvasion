using UnityEngine;

// Da mettere sul prefab dello Sbirro a distanza (es. Sbirro3), al posto della coppia
// EnemyChase + EnemyAttack: questo nemico non carica il bersaglio a contatto, insegue solo
// il Player o un Raver vivo entro raverEngageDistance (con priorità assoluta su tutto, vedi
// AcquireNearestTarget) — non punta mai Console/DJ o SoundSystem, a differenza degli Sbirro
// melee/EnemyChase. Insegue finché non arriva a stopDistance, poi si ferma e lancia
// fumogeni parabolici (vedi ParabolicProjectile) che infliggono un colpo a Player e Raver
// come un attacco corpo a corpo di Sbirro, ma ignorano del tutto Console/DJ e SoundSystem
// anche se li tocca per caso lungo la traiettoria.
// Se il bersaglio si avvicina troppo (sotto retreatDistance) mentre sta sparando, dopo
// retreatDelay secondi smette di attaccare e si allontana per riguadagnare stopDistance:
// il ritardo evita che basti avvicinarsi di un passo per farlo scappare all'istante.
// Con raverFleeCountThreshold > 0 (usato dal Robosbirro) fugge anche quando troppi Raver
// lo circondano entro raverFleeRadius, a prescindere dallo stato corrente: sbirro3 lascia
// questa soglia a 0 e non cambia comportamento.
[RequireComponent(typeof(Rigidbody))]
public class EnemyRangedAttack : MonoBehaviour
{
    private enum State { Chasing, Attacking, Fleeing }

    // Usato da RobosbirroUnstuck: mentre attacca è normale che il nemico stia fermo,
    // non va confuso con un blocco fisico (angolo, collider incastrati).
    public bool IsAttacking => state == State.Attacking;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 4.5f; // velocità sia di avvicinamento sia di fuga

    [Header("Animazione")]
    [Tooltip("Moltiplicatore di velocità dell'Animator (1 = normale): il movimento è gestito interamente da script (niente root motion), quindi il ciclo di camminata va accelerato a parte per restare a passo con moveSpeed.")]
    [SerializeField] private float animationSpeedMultiplier = 1.5f;

    [Header("Target")]
    [Tooltip("Ogni quanti secondi ricalcola il bersaglio più vicino tra Player e Raver.")]
    [SerializeField] private float retargetInterval = 0.5f;
    [Tooltip("Se un Raver (vivo, non a terra) è entro questa distanza, ha sempre priorità assoluta sul Player: prima libera la strada, poi torna a puntare lui. Ignorato se alwaysTargetPlayer è attivo.")]
    [SerializeField] private float raverEngageDistance = 2f;
    [Tooltip("Quanti metri \"pesa\" ogni altro Sbirro già impegnato con un Raver, quando ce n'è più di uno a portata di raverEngageDistance: a parità di distanza fisica si preferisce quello libero, anche se leggermente più lontano, invece di accalcarsi tutti sullo stesso (stesso meccanismo di EnemyChase).")]
    [SerializeField] private float raverOccupancyPenalty = 2.5f;
    [Tooltip("Se attivo, il bersaglio è sempre il Player, ignorando i Raver: usato dal Robosbirro. Sbirro3 lo lascia disattivato e mantiene il comportamento standard.")]
    [SerializeField] private bool alwaysTargetPlayer = false;

    [Header("Distanza di tiro")]
    [Tooltip("Distanza a cui il nemico si ferma e inizia a sparare. Deve restare maggiore del raggio massimo della SoundWave del Player (vedi SoundWave.prefab), altrimenti il Player lo colpisce senza doversi avvicinare.")]
    [SerializeField] private float stopDistance = 14f;
    [Tooltip("Se il bersaglio invade questa distanza (minore di stopDistance) mentre il nemico sta sparando, parte il timer di fuga.")]
    [SerializeField] private float retreatDistance = 8f;
    [Tooltip("Quanti secondi il bersaglio deve restare entro retreatDistance prima che il nemico smetta di sparare e inizi a fuggire.")]
    [SerializeField] private float retreatDelay = 2f;

    [Header("Fuga dai Raver")]
    [Tooltip("Raggio entro cui vengono contati i Raver per la fuga di gruppo. 0 disattiva questo tipo di fuga (comportamento standard, es. Sbirro3).")]
    [SerializeField] private float raverFleeRadius = 6f;
    [Tooltip("Numero di Raver entro raverFleeRadius che fa scattare la fuga, indipendentemente dallo stato corrente. 0 = disattivato.")]
    [SerializeField] private int raverFleeCountThreshold = 0;

    [Header("Attacco")]
    [SerializeField] private GameObject projectilePrefab; // prefab della sfera (vedi ParabolicProjectile)
    [Tooltip("Colpi al secondo mentre è fermo ad attaccare.")]
    [SerializeField] private float fireRate = 0.7f;
    [SerializeField] private Transform firePoint; // punto di spawn del proiettile; se vuoto usa la posizione del nemico

    [Header("Fuga (boss morto)")]
    [Tooltip("Ogni quanti secondi cambia la direzione di fuga casuale mentre RoutState è attivo: valori bassi la fanno sembrare più erratica.")]
    [SerializeField] private float routDirectionChangeInterval = 1.5f;

    private Rigidbody rb;
    private Animator animator;
    private Transform target;
    private State state = State.Chasing;
    private float retargetTimer;
    private float fireTimer;
    private float tooCloseTimer;
    private Vector3 routDirection;
    private float routDirectionTimer;
    private bool hasEnteredArena;
    private Vector3[] pathPoints;
    private int pathIndex;
    private float pathReachRadius = 1.5f;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true; // ruotiamo noi manualmente verso il bersaglio, non vogliamo che la fisica ribalti il nemico
        rb.interpolation = RigidbodyInterpolation.Interpolate; // ammorbidisce il movimento tra un FixedUpdate e l'altro
        rb.useGravity = false;
        rb.constraints |= RigidbodyConstraints.FreezePositionY;

        // L'Animator sta sul modello figlio (es. RiotCop_Unity), non su questo GameObject.
        animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.speed = animationSpeedMultiplier;
        }

        AcquireNearestTarget();
    }

    void OnDestroy()
    {
        // Se questo Sbirro muore mentre ha un Raver come bersaglio, libera subito il suo
        // "posto" (vedi RaverHealth.EngagedCount), stesso motivo di EnemyChase.OnDestroy.
        if (target != null && target.TryGetComponent(out RaverHealth engagedRaver))
        {
            engagedRaver.Disengage();
        }
    }

    void FixedUpdate()
    {
        // Stesso motivo di EnemyChase: il movimento è interamente guidato da MovePosition/MoveRotation,
        // una velocità fisica residua dopo un urto resterebbe "appiccicata" al Rigidbody.
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // Percorso obbligatorio dallo spawn (vedi EnemyPath, assegnato da EnemySpawner):
        // finché non è concluso il nemico lo segue e ignora bersagli, aggro e Raver.
        // All'ultimo waypoint riprende l'AI normale.
        if (FollowPath())
        {
            return;
        }

        retargetTimer += Time.fixedDeltaTime;
        if (retargetTimer >= retargetInterval || target == null)
        {
            retargetTimer = 0f;
            AcquireNearestTarget();

            // Stessa cadenza del retarget, per non contare i Raver vicini ad ogni FixedUpdate:
            // se troppi lo circondano, fugge subito, qualunque sia lo stato corrente.
            if (raverFleeCountThreshold > 0 && state != State.Fleeing
                && CountNearbyRavers() >= raverFleeCountThreshold)
            {
                EnterFleeing();
            }
        }

        if (target == null)
        {
            return;
        }

        if (RoutState.IsActive)
        {
            // Boss morto: si scappa vagando a caso invece di allontanarsi in linea retta dal
            // bersaglio, niente più spari (vedi RoutState) — così gli Sbirro non fuggono tutti
            // sincronizzati nella stessa direzione.
            routDirectionTimer += Time.fixedDeltaTime;
            if (routDirectionTimer >= routDirectionChangeInterval || routDirection == Vector3.zero)
            {
                routDirectionTimer = 0f;
                routDirection = GetRandomDirection();
            }

            Move(routDirection);
            Rotate(routDirection);
            return;
        }

        Vector3 toTarget = target.position - rb.position;
        toTarget.y = 0f; // il movimento resta sul piano orizzontale

        float distance = toTarget.magnitude;

        switch (state)
        {
            case State.Chasing:
                UpdateChasing(toTarget, distance);
                break;
            case State.Attacking:
                UpdateAttacking(toTarget, distance);
                break;
            case State.Fleeing:
                UpdateFleeing(toTarget, distance);
                break;
        }
    }

    private void UpdateChasing(Vector3 toTarget, float distance)
    {
        Move(toTarget);
        Rotate(toTarget);

        if (distance <= stopDistance)
        {
            EnterAttacking();
        }
    }

    private void UpdateAttacking(Vector3 toTarget, float distance)
    {
        Rotate(toTarget); // fermo, ma continua a mirare il bersaglio

        if (distance > stopDistance)
        {
            // Il bersaglio si è allontanato oltre la portata: torna a inseguirlo.
            state = State.Chasing;
            tooCloseTimer = 0f;
            return;
        }

        if (distance < retreatDistance)
        {
            tooCloseTimer += Time.fixedDeltaTime;
            if (tooCloseTimer >= retreatDelay)
            {
                EnterFleeing();
            }
            return;
        }

        tooCloseTimer = 0f;

        fireTimer += Time.fixedDeltaTime;
        float fireInterval = 1f / Mathf.Max(fireRate, 0.01f);
        if (fireTimer >= fireInterval)
        {
            fireTimer = 0f;
            Fire();
        }
    }

    private void UpdateFleeing(Vector3 toTarget, float distance)
    {
        // Si allontana in linea retta dal bersaglio, senza sparare, finché non riguadagna stopDistance.
        Move(-toTarget);
        Rotate(-toTarget);

        if (distance >= stopDistance)
        {
            EnterAttacking();
        }
    }

    private void EnterAttacking()
    {
        state = State.Attacking;
        fireTimer = 0f; // il primo colpo parte dopo un intervallo pieno, non appena si ferma
        tooCloseTimer = 0f;
    }

    private void EnterFleeing()
    {
        state = State.Fleeing;
        tooCloseTimer = 0f;
    }

    // Chiamato da EnemySpawner subito dopo l'Instantiate, se lo spawner ha un percorso.
    public void SetPath(EnemyPath path)
    {
        if (path == null || path.Count == 0)
        {
            return;
        }

        pathPoints = path.GetWorldPoints();
        pathIndex = 0;
        pathReachRadius = path.WaypointReachRadius;
    }

    // Avanza lungo i waypoint del percorso. Ritorna true finché il percorso è in corso
    // (movimento/rotazione già gestiti qui), false quando non c'è percorso o si è appena
    // concluso: da lì FixedUpdate prosegue con l'AI normale.
    private bool FollowPath()
    {
        if (pathPoints == null)
        {
            return false;
        }

        // Boss morto: si abbandona il percorso e si scappa come gli altri (vedi RoutState).
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
                // Ultimo waypoint raggiunto: da qui insegue/attacca normalmente, e il clamp
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
        GameObject player = GameObject.FindGameObjectWithTag("Player");

        if (alwaysTargetPlayer)
        {
            // Il Robosbirro ignora la finta: resta concentrato sul Player a prescindere da
            // qualunque giocoliere nei paraggi.
            SetTarget(player?.transform);
            return;
        }

        // Un Raver che sta facendo il giocoliere (vedi RaverJuggler) ha priorità assoluta su
        // tutto il resto se questo Sbirro gli è entrato entro il suo raggio di attrazione:
        // è pensato apposta per attirarlo, con precedenza persino sul Player.
        Transform attractor = RaverJuggler.FindNearestAttractor(transform.position);
        if (attractor != null)
        {
            SetTarget(attractor);
            return;
        }

        // Non punta mai Console/DJ o SoundSystem: bersaglia solo Player e Raver.
        Transform nearestRaver = FindNearestEngageableRaver();
        SetTarget(nearestRaver != null ? nearestRaver : player?.transform);
    }

    // Se il bersaglio cambia e quello vecchio o quello nuovo è un Raver, aggiorna il suo
    // EngagedCount (vedi RaverHealth.Engage/Disengage): stesso meccanismo di
    // EnemyChase.SetTarget, per non far accalcare più Sbirro (melee e a distanza insieme)
    // sullo stesso Raver quando ce n'è un altro libero a portata.
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
    }

    // Un Raver a terra (RaverHealth.IsDown, vedi rianimazione dal cono del Player) è inerte:
    // non viene considerato, il nemico lo ignora come bersaglio (anche se il suo collider
    // può comunque bloccarlo fisicamente per puro ingombro).
    private Transform FindNearestEngageableRaver()
    {
        Transform nearest = null;
        float bestScore = float.MaxValue;
        float sqrRaverEngageDistance = raverEngageDistance * raverEngageDistance;

        foreach (RaverHealth raver in FindObjectsByType<RaverHealth>(FindObjectsSortMode.None))
        {
            if (raver.IsDown)
            {
                continue;
            }

            float sqrDistance = (raver.transform.position - transform.position).sqrMagnitude;
            if (sqrDistance > sqrRaverEngageDistance)
            {
                continue;
            }

            // Stesso meccanismo di EnemyChase.FindNearestEngageableRaver: a parità di distanza
            // fisica un Raver già impegnato pesa come se fosse più lontano, per preferire quello libero.
            float score = Mathf.Sqrt(sqrDistance) + raver.EngagedCount * raverOccupancyPenalty;
            if (score < bestScore)
            {
                nearest = raver.transform;
                bestScore = score;
            }
        }

        return nearest;
    }

    private int CountNearbyRavers()
    {
        int count = 0;
        float sqrRadius = raverFleeRadius * raverFleeRadius;

        foreach (RaverHealth raver in FindObjectsByType<RaverHealth>(FindObjectsSortMode.None))
        {
            float sqrDistance = (raver.transform.position - rb.position).sqrMagnitude;
            if (sqrDistance <= sqrRadius)
            {
                count++;
            }
        }

        return count;
    }

    private static Vector3 GetRandomDirection()
    {
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
    }

    private void Move(Vector3 direction)
    {
        Vector3 nextPosition = rb.position + direction.normalized * moveSpeed * Time.fixedDeltaTime;
        rb.MovePosition(ClampToFloor(nextPosition));
    }

    // In Fleeing il nemico si allontana in linea retta dal bersaglio senza sapere dove
    // sia il bordo della mappa: senza questo clamp, circondato dai Raver, finisce per
    // scappare sempre verso il perimetro e restarci incollato (spingendo contro il muro
    // invisibile). Bloccando qui la posizione risultante entro i bounds del floor, la fuga
    // scivola lungo il bordo invece di restarci piantata contro.
    private Vector3 ClampToFloor(Vector3 position)
    {
        if (FloorBounds.Instance == null)
        {
            return position;
        }

        Bounds bounds = FloorBounds.Instance.Bounds;

        // Spawnato fuori dal perimetro (spawner oltre i muri, che il nemico ignora - vedi
        // ArenaBounds.IgnoreCollisionsForEnemy), deve poter camminare "da fuori a dentro":
        // finché non ha raggiunto una volta l'interno dell'arena non lo si clampa,
        // altrimenti verrebbe risucchiato al bordo al primo passo. Una volta entrato il
        // clamp torna attivo (fuga lungo il bordo, RoutState) come prima.
        if (!hasEnteredArena)
        {
            bool insideArena = position.x > bounds.min.x && position.x < bounds.max.x
                && position.z > bounds.min.z && position.z < bounds.max.z;
            if (insideArena || RoutState.IsActive)
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

    private void Rotate(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        rb.MoveRotation(targetRotation);
    }

    private void Fire()
    {
        if (projectilePrefab == null || target == null)
        {
            return;
        }

        Transform spawnPoint = firePoint != null ? firePoint : transform;
        GameObject projectileObject = Instantiate(projectilePrefab, spawnPoint.position, Quaternion.identity);

        ParabolicProjectile projectile = projectileObject.GetComponent<ParabolicProjectile>();
        if (projectile != null)
        {
            projectile.Init(target.position);
        }
    }
}
