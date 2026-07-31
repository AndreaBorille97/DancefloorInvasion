using UnityEngine;

// Da mettere sul prefab del nemico (lo stesso che spawna EnemySpawner).
// Il nemico insegue sul piano orizzontale (X/Z) il bersaglio più vicino tra il Player
// e ogni Raver in scena (vedi RaverHealth), ricalcolandolo periodicamente: se un Raver
// gli si avvicina più del Player, il nemico cambia bersaglio e carica lui.
[RequireComponent(typeof(Rigidbody))]
public class EnemyChase : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f; // velocità di inseguimento (unità al secondo)

    [Header("Target")]
    [Tooltip("Ogni quanti secondi ricalcola il bersaglio più vicino tra Player e Raver in scena.")]
    [SerializeField] private float retargetInterval = 0.5f;

    [Header("Fuga (boss morto)")]
    [Tooltip("Ogni quanti secondi cambia la direzione di fuga casuale mentre RoutState è attivo: valori bassi la fanno sembrare più erratica.")]
    [SerializeField] private float routDirectionChangeInterval = 1.5f;

    private Rigidbody rb;
    private Transform target;
    private float retargetTimer;
    private Vector3 routDirection;
    private float routDirectionTimer;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true; // ruotiamo noi manualmente verso il bersaglio, non vogliamo che la fisica ribalti il nemico
        rb.interpolation = RigidbodyInterpolation.Interpolate; // ammorbidisce il movimento tra un FixedUpdate e l'altro
        rb.useGravity = false;
        rb.constraints |= RigidbodyConstraints.FreezePositionY;

        AcquireNearestTarget();
    }

    void FixedUpdate()
    {
        // Lo scontro col bersaglio (kinematic o dinamico) assegna al nemico una vera
        // velocità fisica per risolvere la sovrapposizione. Il movimento qui sotto è già
        // interamente guidato da MovePosition, quindi quella velocità non serve: se non la
        // azzeriamo resta "appiccicata" al Rigidbody e lo fa scivolare per inerzia dopo il tocco.
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        retargetTimer += Time.fixedDeltaTime;
        if (retargetTimer >= retargetInterval || target == null)
        {
            retargetTimer = 0f;
            AcquireNearestTarget();
        }

        if (target == null)
        {
            return;
        }

        // Boss morto: si scappa vagando a caso invece di caricare il bersaglio (vedi RoutState),
        // così gli Sbirro non fuggono tutti in linea retta e sincronizzata da esso.
        if (RoutState.IsActive)
        {
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

        Move(toTarget);
        Rotate(toTarget);
    }

    private void AcquireNearestTarget()
    {
        Transform nearest = null;
        float nearestSqrDistance = float.MaxValue;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            nearest = player.transform;
            nearestSqrDistance = (player.transform.position - transform.position).sqrMagnitude;
        }

        // Nessun tag/layer dedicato per i Raver: cerchiamo direttamente chi ha un RaverHealth,
        // stesso approccio (component-based, non layer-based) già usato da CamperProjectile.
        foreach (RaverHealth raver in FindObjectsByType<RaverHealth>(FindObjectsSortMode.None))
        {
            float sqrDistance = (raver.transform.position - transform.position).sqrMagnitude;
            if (sqrDistance < nearestSqrDistance)
            {
                nearest = raver.transform;
                nearestSqrDistance = sqrDistance;
            }
        }

        target = nearest;
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

    // In fuga (RoutState) il nemico vaga a caso senza sapere dove sia il bordo della mappa:
    // stesso motivo/fix già applicato in EnemyRangedAttack.
    private static Vector3 ClampToFloor(Vector3 position)
    {
        if (FloorBounds.Instance == null)
        {
            return position;
        }

        Bounds bounds = FloorBounds.Instance.Bounds;
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
