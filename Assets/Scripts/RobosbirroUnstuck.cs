using UnityEngine;

// Da mettere sul prefab del Robosbirro, insieme a EnemyRangedAttack: se il boss resta
// fermo per più di un secondo (angolo della mappa, collider incastrati, sciame di Raver
// che gli impedisce di muoversi) o se il Player si allontana troppo (es. gli sfugge
// dietro ostacoli o è più veloce di lui), si teletrasporta nei dintorni del Player. Mentre
// EnemyRangedAttack è in stato Attacking il boss si ferma di proposito per sparare: quello
// non conta come blocco (vedi EnemyRangedAttack.IsAttacking).
[RequireComponent(typeof(EnemyRangedAttack))]
[RequireComponent(typeof(Rigidbody))]
public class RobosbirroUnstuck : MonoBehaviour
{
    [Header("Rilevamento blocco")]
    [Tooltip("Ogni quanti secondi si controlla quanto si è spostato il Robosbirro: se nell'intervallo si muove meno di minMoveDistance (fuori dallo stato Attacking), si teletrasporta subito.")]
    [SerializeField] private float checkInterval = 1f;
    [SerializeField] private float minMoveDistance = 0.5f;

    [Header("Distanza dal Player")]
    [Tooltip("Se il Robosbirro supera questa distanza dal Player, si teletrasporta nei suoi dintorni allo stesso modo del caso di blocco.")]
    [SerializeField] private float maxDistanceFromPlayer = 12f;

    [Header("Teletrasporto")]
    [Tooltip("Distanza minima dal Player per il punto di arrivo: evita che il Robosbirro spawni incollato al Player.")]
    [SerializeField] private float minTeleportRadius = 3f;
    [Tooltip("Distanza massima dal Player per il punto di arrivo (raggio entro cui si sceglie un punto casuale attorno al Player).")]
    [SerializeField] private float teleportRadius = 8f;

    private EnemyRangedAttack rangedAttack;
    private Rigidbody rb;
    private Transform player;
    private Vector3 lastCheckedPosition;
    private float checkTimer;

    void Awake()
    {
        rangedAttack = GetComponent<EnemyRangedAttack>();
        rb = GetComponent<Rigidbody>();
        player = GameObject.FindGameObjectWithTag("Player")?.transform;
        lastCheckedPosition = rb.position;
    }

    void Update()
    {
        checkTimer += Time.deltaTime;
        if (checkTimer < checkInterval)
        {
            return;
        }
        checkTimer = 0f;

        if (player != null && (rb.position - player.position).sqrMagnitude > maxDistanceFromPlayer * maxDistanceFromPlayer)
        {
            Teleport();
            return;
        }

        // Mentre attacca è normale che stia fermo: non è un blocco.
        if (rangedAttack.IsAttacking)
        {
            lastCheckedPosition = rb.position;
            return;
        }

        float movedDistance = (rb.position - lastCheckedPosition).magnitude;
        lastCheckedPosition = rb.position;

        if (movedDistance < minMoveDistance)
        {
            Teleport();
        }
    }

    private void Teleport()
    {
        if (player == null)
        {
            return;
        }

        float randomAngle = Random.Range(0f, Mathf.PI * 2f);
        float randomDistance = Random.Range(minTeleportRadius, teleportRadius);
        Vector2 randomOffset = new Vector2(Mathf.Cos(randomAngle), Mathf.Sin(randomAngle)) * randomDistance;
        Vector3 destination = player.position + new Vector3(randomOffset.x, 0f, randomOffset.y);
        destination.y = rb.position.y;

        if (FloorBounds.Instance != null)
        {
            Bounds bounds = FloorBounds.Instance.Bounds;
            destination.x = Mathf.Clamp(destination.x, bounds.min.x, bounds.max.x);
            destination.z = Mathf.Clamp(destination.z, bounds.min.z, bounds.max.z);
        }

        rb.position = destination;
        lastCheckedPosition = destination;
    }
}
