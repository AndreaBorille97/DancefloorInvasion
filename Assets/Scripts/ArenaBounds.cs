using UnityEngine;

// Da mettere sul GameObject "ArenaBounds", con 4 figli (WallNord, WallSud, WallEst,
// WallOvest) ciascuno con un BoxCollider. All'avvio riposiziona e ridimensiona i 4 muri
// in base ai bounds di "floor" (stessa convenzione di RaverSpawner), così restano
// allineati al pavimento anche se questo viene ridimensionato, senza ricalcolarli a mano.
public class ArenaBounds : MonoBehaviour
{
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
        UpdateWalls();
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
            return floorRenderer.bounds;
        }

        Collider floorCollider = floor.GetComponentInChildren<Collider>();
        if (floorCollider != null)
        {
            return floorCollider.bounds;
        }

        return new Bounds(floor.position, Vector3.zero);
    }
}
