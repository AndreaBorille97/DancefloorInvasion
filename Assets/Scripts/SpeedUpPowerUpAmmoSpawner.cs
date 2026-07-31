using UnityEngine;

// Da associare a un GameObject "spawner" dedicato in scena (stesso approccio di
// CamperSpawner/RaverSpawner/DrugSpawner, non va messo su GameManager né su "floor"): ogni
// tot secondi, scelti a caso tra 1 e maxSpawnInterval, istanzia uno SpeedUpPowerUpAmmo in un
// punto casuale sopra il pavimento (i confini della mappa sono letti dai bounds del
// Renderer di "floor").
public class SpeedUpPowerUpAmmoSpawner : MonoBehaviour
{
    [Header("Spawn")]
    [SerializeField] private GameObject ammoPrefab; // prefab dello SpeedUpPowerUpAmmo da spawnare
    [SerializeField] private Transform floor; // oggetto "floor", usato per calcolare i confini della mappa (Renderer o, in mancanza, Collider)
    [Tooltip("N: intervallo tra uno spawn e l'altro scelto a caso ogni volta tra 1 e questo valore (secondi).")]
    [SerializeField] private float maxSpawnInterval = 20f;
    [SerializeField] private float spawnHeight = 0.5f; // altezza sopra il pavimento a cui spawnare, evita che nasca incassato nel terreno

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
        SpawnAmmo();
    }

    private void PickNextSpawnDelay()
    {
        nextSpawnDelay = Random.Range(1f, Mathf.Max(1f, maxSpawnInterval));
    }

    private void SpawnAmmo()
    {
        if (ammoPrefab == null || floor == null)
        {
            return;
        }

        Instantiate(ammoPrefab, GetRandomPositionOnFloor(), Quaternion.identity);
    }

    private Vector3 GetRandomPositionOnFloor()
    {
        Bounds bounds = GetFloorBounds();
        float x = Random.Range(bounds.min.x, bounds.max.x);
        float z = Random.Range(bounds.min.z, bounds.max.z);
        return new Vector3(x, bounds.max.y + spawnHeight, z);
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
