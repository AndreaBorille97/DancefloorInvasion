using System.Collections.Generic;
using UnityEngine;

// Da mettere sul GameObject "ArenaBounds", con 4 figli (WallNord, WallSud, WallEst,
// WallOvest) ciascuno con un BoxCollider. All'avvio riposiziona e ridimensiona i 4 muri
// in base ai bounds di "floor" (stessa convenzione di RaverSpawner), così restano
// allineati al pavimento anche se questo viene ridimensionato, senza ricalcolarli a mano.
// Espone anche i collider dei muri come singleton statico: i nemici, spawnati fuori dal
// perimetro per sembrare "arrivare da fuori" invece di comparire dal nulla, li ignorano
// alla comparsa (vedi IgnoreCollisionsForEnemy, chiamato da EnemyHealth/RobosbirroHealth)
// così ci camminano attraverso. Player e Raver restano invece contenuti dai muri.
public class ArenaBounds : MonoBehaviour
{
    public static ArenaBounds Instance { get; private set; }

    private Collider[] wallColliders;

    [SerializeField] private Transform floor; // oggetto "floor", usato per calcolare i confini della mappa (Renderer o, in mancanza, Collider)
    [SerializeField] private float wallThickness = 2f;
    [SerializeField] private float wallHeight = 10f;

    [Header("Muri (figli con BoxCollider)")]
    [SerializeField] private Transform wallNord;
    [SerializeField] private Transform wallSud;
    [SerializeField] private Transform wallEst;
    [SerializeField] private Transform wallOvest;

    void Awake()
    {
        Instance = this;
        UpdateWalls();
        CacheWallColliders();
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void CacheWallColliders()
    {
        List<Collider> colliders = new List<Collider>();
        AddWallColliders(colliders, wallNord);
        AddWallColliders(colliders, wallSud);
        AddWallColliders(colliders, wallEst);
        AddWallColliders(colliders, wallOvest);

        // Anche il BoxCollider del pavimento (l'oggetto "FloorBoundAnchors" che ospita
        // ArenaBounds e FloorBounds) è un blocco solido alto ~2 unità che copre tutta
        // l'arena: un nemico che entra da fuori ne urterebbe la parete verticale al bordo,
        // restando fermo lì nonostante abbia già attraversato i 4 muri. Va quindi ignorato
        // anche quello. I nemici hanno la Y bloccata e niente gravità, non ci "poggiano"
        // sopra, quindi ignorarlo non li fa cadere.
        if (transform.parent != null)
        {
            foreach (Collider parentCollider in transform.parent.GetComponents<Collider>())
            {
                if (!parentCollider.isTrigger)
                {
                    colliders.Add(parentCollider);
                }
            }
        }

        wallColliders = colliders.ToArray();
    }

    private static void AddWallColliders(List<Collider> destination, Transform wall)
    {
        if (wall == null)
        {
            return;
        }

        destination.AddRange(wall.GetComponentsInChildren<Collider>());
    }

    // Chiamato da un nemico alla comparsa: disattiva la collisione fisica tra i suoi
    // collider e i 4 muri dell'arena, così può attraversarli camminando dagli spawner
    // piazzati fuori dal perimetro fin dentro l'arena. Non tocca la collisione con
    // nient'altro (pavimento, altri nemici, Raver, obiettivi), né quella di Player/Raver
    // coi muri, che restano bloccati dentro come prima.
    public static void IgnoreCollisionsForEnemy(GameObject enemy)
    {
        if (Instance == null || Instance.wallColliders == null || enemy == null)
        {
            return;
        }

        Collider[] enemyColliders = enemy.GetComponentsInChildren<Collider>();
        foreach (Collider enemyCollider in enemyColliders)
        {
            foreach (Collider wallCollider in Instance.wallColliders)
            {
                if (wallCollider != null)
                {
                    Physics.IgnoreCollision(enemyCollider, wallCollider, true);
                }
            }
        }
    }

    private void UpdateWalls()
    {
        if (floor == null)
        {
            return;
        }

        Bounds bounds = GetFloorBounds();
        float wallY = bounds.center.y;

        PlaceWall(
            wallNord,
            new Vector3(bounds.center.x, wallY, bounds.max.z + wallThickness * 0.5f),
            new Vector3(bounds.size.x + wallThickness * 2f, wallHeight, wallThickness));
        PlaceWall(
            wallSud,
            new Vector3(bounds.center.x, wallY, bounds.min.z - wallThickness * 0.5f),
            new Vector3(bounds.size.x + wallThickness * 2f, wallHeight, wallThickness));
        PlaceWall(
            wallEst,
            new Vector3(bounds.max.x + wallThickness * 0.5f, wallY, bounds.center.z),
            new Vector3(wallThickness, wallHeight, bounds.size.z + wallThickness * 2f));
        PlaceWall(
            wallOvest,
            new Vector3(bounds.min.x - wallThickness * 0.5f, wallY, bounds.center.z),
            new Vector3(wallThickness, wallHeight, bounds.size.z + wallThickness * 2f));
    }

    private static void PlaceWall(Transform wall, Vector3 position, Vector3 scale)
    {
        if (wall == null)
        {
            return;
        }

        wall.position = position;

        // scale è la dimensione desiderata in world space, ma localScale è relativa al parent:
        // se ArenaBounds è annidato sotto "floor" (che può avere una scala propria, anche non
        // uniforme o con segno negativo su un asse), va convertita dividendo per il lossyScale
        // del parent, altrimenti il muro risulta troppo piccolo/grande o "specchiato" e i nemici
        // ci passano attraverso pur sembrando ben posizionato in Scene view.
        Vector3 parentLossyScale = wall.parent != null ? wall.parent.lossyScale : Vector3.one;
        wall.localScale = new Vector3(
            scale.x / SafeDivisor(parentLossyScale.x),
            scale.y / SafeDivisor(parentLossyScale.y),
            scale.z / SafeDivisor(parentLossyScale.z));
    }

    private static float SafeDivisor(float value)
    {
        return Mathf.Abs(value) < 0.0001f ? 1f : value;
    }

    private Bounds GetFloorBounds()
    {
        // Cerca prima un Renderer (anche nei figli, in caso "floor" sia un oggetto composito),
        // altrimenti usa il Collider come confine della mappa.
        Renderer floorRenderer = floor.GetComponentInChildren<Renderer>();
        if (floorRenderer != null)
        {
            Debug.Log($"[ArenaBounds DEBUG] bounds da Renderer di '{floorRenderer.name}': center={floorRenderer.bounds.center}, size={floorRenderer.bounds.size}");
            return floorRenderer.bounds;
        }

        Collider floorCollider = floor.GetComponentInChildren<Collider>();
        if (floorCollider != null)
        {
            Debug.Log($"[ArenaBounds DEBUG] bounds da Collider di '{floorCollider.name}' (enabled={floorCollider.enabled}): center={floorCollider.bounds.center}, size={floorCollider.bounds.size}");
            return floorCollider.bounds;
        }

        Debug.LogWarning($"[ArenaBounds DEBUG] NESSUN Renderer/Collider trovato su '{floor.name}' (activeInHierarchy={floor.gameObject.activeInHierarchy}) -> bounds a size zero in {floor.position}. I muri collasseranno su questo punto.");
        return new Bounds(floor.position, Vector3.zero);
    }
}
