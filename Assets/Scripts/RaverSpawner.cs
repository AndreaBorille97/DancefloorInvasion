using UnityEngine;

// Da associare a un GameObject "spawner" dedicato in scena (stessa logica di
// CamperSpawner, non va messo su GameManager né su "floor"): ogni tot secondi,
// scelti a caso tra 1 e maxSpawnInterval, istanzia un Raver in un punto casuale
// sopra il pavimento (i confini della mappa sono letti dai bounds del Renderer di "floor").
public class RaverSpawner : MonoBehaviour
{
    [Header("Spawn")]
    [SerializeField] private GameObject raverPrefab; // prefab del Raver da spawnare
    [SerializeField] private Transform floor; // oggetto "floor", usato per calcolare i confini della mappa (Renderer o, in mancanza, Collider)
    [Tooltip("N: intervallo tra uno spawn e l'altro scelto a caso ogni volta tra 1 e questo valore (secondi).")]
    [SerializeField] private float maxSpawnInterval = 10f;
    [SerializeField] private float spawnHeight = 0.5f; // margine extra sopra il pavimento, oltre alla metà altezza del prefab (vedi GetPrefabHalfHeight)

    private float timer;
    private float nextSpawnDelay;

    void Awake()
    {
        PickNextSpawnDelay();
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer < nextSpawnDelay)
        {
            return;
        }

        timer = 0f;
        PickNextSpawnDelay();
        SpawnRaver();
    }

    private void PickNextSpawnDelay()
    {
        nextSpawnDelay = Random.Range(1f, Mathf.Max(1f, maxSpawnInterval));
    }

    private void SpawnRaver()
    {
        if (raverPrefab == null || floor == null)
        {
            return;
        }

        Instantiate(raverPrefab, GetRandomPositionOnFloor(), Quaternion.identity);
    }

    private Vector3 GetRandomPositionOnFloor()
    {
        Bounds bounds = GetFloorBounds();
        float x = Random.Range(bounds.min.x, bounds.max.x);
        float z = Random.Range(bounds.min.z, bounds.max.z);
        return new Vector3(x, bounds.max.y + GetPrefabHalfHeight() + spawnHeight, z);
    }

    // Il pivot del Raver è al centro del collider (non alla base): senza tener conto di
    // questa metà altezza, spawnHeight da solo lo fa nascere incassato nel pavimento.
    private float GetPrefabHalfHeight()
    {
        Renderer prefabRenderer = raverPrefab.GetComponentInChildren<Renderer>();
        return prefabRenderer != null ? prefabRenderer.bounds.extents.y : 0f;
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
