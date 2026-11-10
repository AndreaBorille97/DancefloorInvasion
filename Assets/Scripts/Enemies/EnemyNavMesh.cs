using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Costruisce da solo, all'avvio della partita, il NavMesh su cui EnemyChase calcola i percorsi
// per aggirare gli ostacoli (vedi EnemyChase.GetNextWaypoint). Non va messo in scena: si crea
// da sé dopo il caricamento di ogni scena che ha un FloorBounds (vedi CreateForScene). Serve
// solo a calcolare percorsi: nessun NavMeshAgent, i nemici continuano a muoversi da soli col
// loro Rigidbody. L'IdroSbirro parcheggiato non entra qui: è un NavMeshObstacle che si
// ritaglia il suo buco a runtime (vedi IdroSbirro).
//
// Niente bake in editor né NavMeshSurface: in questo progetto è tutto sul layer Default, quindi
// non si può filtrare per layer, e un bake a mano dimenticato lasciava i nemici senza percorsi
// senza nessun segnale. Qui si sceglie a mano cosa entra:
// - la superficie calpestabile è il riquadro di FloorBounds, aggiunto come box piatto: il
//   pavimento non ha per forza un collider (i nemici non usano la gravità);
// - gli ostacoli sono i collider solidi (non Trigger) fermi nell'arena, tranne chi si muove o
//   viene gestito a parte: Player, Raver, nemici, qualunque Rigidbody non kinematic e i
//   muri di ArenaBounds (che i nemici attraversano comunque).
//
// Per controllare il risultato: in Play Mode, con la finestra AI > Navigation aperta, l'area
// calpestabile compare in azzurro nella Scene view.
public class EnemyNavMesh : MonoBehaviour
{
    // Solo gli ostacoli fra poco sotto il pavimento e questa altezza sopra contano: travi, luci
    // e tralicci in alto non bloccano nessuno.
    private const float ObstacleHeightAboveFloor = 6f;

    private NavMeshDataInstance instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForScene()
    {
        if (FindAnyObjectByType<EnemyNavMesh>() != null || FindAnyObjectByType<FloorBounds>() == null)
        {
            return;
        }

        new GameObject(nameof(EnemyNavMesh)).AddComponent<EnemyNavMesh>();
    }

    void Start()
    {
        // In Start e non in Awake: FloorBounds e ArenaBounds hanno già sistemato pavimento e
        // muri nei loro Awake.
        Build();
    }

    void OnDestroy()
    {
        instance.Remove();
    }

    private void Build()
    {
        FloorBounds floor = FindAnyObjectByType<FloorBounds>();
        if (floor == null)
        {
            return;
        }

        Bounds floorBounds = floor.Bounds;
        float floorTop = floorBounds.max.y;

        Bounds collectBounds = new Bounds(
            new Vector3(floorBounds.center.x, floorTop + ObstacleHeightAboveFloor * 0.5f - 0.5f, floorBounds.center.z),
            new Vector3(floorBounds.size.x, ObstacleHeightAboveFloor + 1f, floorBounds.size.z));

        List<NavMeshBuildSource> collected = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(collectBounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0,
            new List<NavMeshBuildMarkup>(), collected);

        List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();
        List<string> obstacleNames = new List<string>();
        foreach (NavMeshBuildSource source in collected)
        {
            if (source.component is Collider collider && IsObstacle(collider))
            {
                sources.Add(source);
                obstacleNames.Add(collider.name);
            }
        }

        // Il pavimento: un box sottile con la faccia superiore all'altezza di FloorBounds.
        const float floorThickness = 0.2f;
        sources.Add(new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Box,
            size = new Vector3(floorBounds.size.x, floorThickness, floorBounds.size.z),
            transform = Matrix4x4.Translate(new Vector3(floorBounds.center.x, floorTop - floorThickness * 0.5f, floorBounds.center.z)),
            area = 0,
        });

        Bounds buildBounds = collectBounds;
        buildBounds.Expand(new Vector3(0f, 2f, 0f));
        NavMeshData data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources, buildBounds,
            Vector3.zero, Quaternion.identity);
        if (data == null)
        {
            Debug.LogError("[EnemyNavMesh] Costruzione del NavMesh fallita: i nemici andranno in linea retta.", this);
            return;
        }

        instance = NavMesh.AddNavMeshData(data);

        obstacleNames.Sort();
        Debug.Log($"[EnemyNavMesh] NavMesh pronto: {obstacleNames.Count} ostacoli.\n"
            + string.Join("\n", obstacleNames), this);
    }

    // Cosa blocca il passaggio: collider solidi fermi. Chi si muove (o è gestito a parte)
    // resta fuori, altrimenti lascerebbe un buco dove stava all'avvio.
    private static bool IsObstacle(Collider collider)
    {
        if (collider.isTrigger || !collider.enabled || !collider.gameObject.activeInHierarchy)
        {
            return false;
        }

        if (collider.CompareTag("Player")
            || collider.GetComponentInParent<PlayerHealth>() != null
            || collider.GetComponentInParent<RaverHealth>() != null
            || collider.GetComponentInParent<EnemyHealth>() != null
            || collider.GetComponentInParent<ArenaBounds>() != null
            || collider.GetComponentInParent<FloorBounds>() != null)
        {
            return false;
        }

        Rigidbody body = collider.attachedRigidbody;
        return body == null || body.isKinematic;
    }
}
