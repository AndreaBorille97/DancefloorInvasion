using UnityEngine;

// Da associare a un GameObject "spawner" dedicato in scena, unico (stesso approccio di
// CamperSpawner/RaverSpawner, non va messo su GameManager né su "floor"): dopo spawnTime
// secondi di gioco trascorsi, istanzia il Robosbirro una sola volta per partita in un
// punto casuale sopra il pavimento (bounds letti dal Renderer di "floor", o dal suo
// Collider in mancanza). Tenerlo separato da EnemySpawner evita che, se in scena ci sono
// più EnemySpawner (uno per angolo dell'arena), ognuno provi a spawnare il proprio
// Robosbirro: qui l'unicità è garantita mettendo un solo RobosbirroSpawner in scena.
public class RobosbirroSpawner : MonoBehaviour
{
    [Header("Spawn")]
    [SerializeField] private GameObject robosbirroPrefab; // prefab del Robosbirro da spawnare
    [SerializeField] private Transform floor; // oggetto "floor", usato per calcolare i confini della mappa (Renderer o, in mancanza, Collider)
    [Tooltip("Secondi di gioco trascorsi dopo cui compare il Robosbirro (una sola volta per partita).")]
    [SerializeField] private float spawnTime = 360f;
    [SerializeField] private float spawnHeight = 0.5f; // margine extra sopra il pavimento, oltre alla metà altezza del prefab (vedi GetPrefabHalfHeight)

    private float elapsedTime;
    private bool spawned;

    void Update()
    {
        if (spawned)
        {
            return;
        }

        elapsedTime += Time.deltaTime;
        if (elapsedTime >= spawnTime)
        {
            spawned = true;
            SpawnRobosbirro();
        }
    }

    private void SpawnRobosbirro()
    {
        if (robosbirroPrefab == null || floor == null)
        {
            return;
        }

        Instantiate(robosbirroPrefab, GetRandomPositionOnFloor(), Quaternion.identity);
    }

    private Vector3 GetRandomPositionOnFloor()
    {
        Bounds bounds = GetFloorBounds();
        float x = Random.Range(bounds.min.x, bounds.max.x);
        float z = Random.Range(bounds.min.z, bounds.max.z);
        return new Vector3(x, bounds.max.y + GetPrefabHalfHeight() + spawnHeight, z);
    }

    // Il pivot del Robosbirro è al centro del collider (non alla base): con la sua scala
    // (ben più grande di 1), spawnHeight da solo lo fa nascere incassato nel pavimento.
    private float GetPrefabHalfHeight()
    {
        Renderer prefabRenderer = robosbirroPrefab.GetComponentInChildren<Renderer>();
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
