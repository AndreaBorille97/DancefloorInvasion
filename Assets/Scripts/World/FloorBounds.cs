using UnityEngine;

// Da mettere sul GameObject "floor" in scena, unico. Calcola una sola volta i bounds
// del pavimento (stesso criterio Renderer/Collider già usato da RaverSpawner,
// RobosbirroSpawner, EnemySpawner e ArenaBounds) e li espone come singleton statico:
// serve ai nemici instanziati a runtime (es. EnemyRangedAttack) per restare entro
// l'arena, cosa che un semplice [SerializeField] Transform floor non permetterebbe,
// dato che quei prefab non sono presenti in scena e non possono avere un riferimento
// pre-cablato a un oggetto che vive solo nella scena.
public class FloorBounds : MonoBehaviour
{
    public static FloorBounds Instance { get; private set; }

    public Bounds Bounds { get; private set; }

    void Awake()
    {
        Instance = this;
        Bounds = ComputeBounds();
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private Bounds ComputeBounds()
    {
        Renderer floorRenderer = GetComponentInChildren<Renderer>();
        if (floorRenderer != null)
        {
            return floorRenderer.bounds;
        }

        Collider floorCollider = GetComponentInChildren<Collider>();
        if (floorCollider != null)
        {
            return floorCollider.bounds;
        }

        return new Bounds(transform.position, Vector3.zero);
    }
}
