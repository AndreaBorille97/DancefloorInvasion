using UnityEngine;

// Da associare a un GameObject "spawner" in scena: ogni tot secondi
// istanzia un nemico in una posizione casuale attorno allo spawner,
// evitando di sovrapporlo a nemici già presenti. Il boss (Robosbirro) ha
// un suo spawner dedicato, vedi RobosbirroSpawner: qui, se in scena ci
// fossero più EnemySpawner, ognuno finirebbe per spawnarne uno proprio.
public class EnemySpawner : MonoBehaviour
{
    [System.Serializable]
    private class EnemyOption
    {
        public GameObject prefab;   // tipo di nemico da spawnare
        public float weight = 1f;   // peso relativo: più alto = più probabile rispetto agli altri
    }

    [Header("Spawn")]
    [SerializeField] private EnemyOption[] enemyOptions; // tipi di nemico spawnabili, con probabilità relativa
    [SerializeField] private float spawnInterval = 2f; // intervallo di spawn iniziale (secondi tra uno spawn e l'altro)

    [Header("Difficoltà crescente")]
    [Tooltip("Di quanto si accorcia l'intervallo di spawn per ogni secondo di gioco trascorso: valori più alti fanno aumentare la velocità di spawn più in fretta.")]
    [SerializeField] private float intervalDecreasePerSecond = 0.004f;
    [SerializeField] private float minSpawnInterval = 0.3f; // intervallo minimo, sotto il quale non si scende anche a tempo infinito

    [Header("Posizionamento")]
    [SerializeField] private float spawnRadius = 3f;      // raggio entro cui scegliere una posizione casuale attorno allo spawner
    [SerializeField] private float spawnHeight = 0.35f;   // altezza da terra a cui spawnare (pivot del nemico rispetto al terreno), evita che nasca dentro al pavimento
    [SerializeField] private float minSpawnDistance = 1.5f; // distanza minima richiesta da altri nemici già presenti
    [SerializeField] private LayerMask enemyLayer;          // layer dei nemici, usato per controllare le sovrapposizioni
    [SerializeField] private int maxSpawnAttempts = 10;      // tentativi massimi per trovare una posizione libera
    [Tooltip("Oggetto \"floor\": se assegnato, le posizioni di spawn vengono ristrette ai suoi bounds, utile per spawner vicini ai bordi dell'arena.")]
    [SerializeField] private Transform floor;

    private float timer;
    private float elapsedTime; // tempo totale trascorso dall'avvio, usato per accorciare l'intervallo di spawn

    // Mentre il Robosbirro è in scena, la difficoltà resta congelata al 15% del tempo
    // impiegato a comparire (es. entra dopo 5 min: l'intervallo resta quello che si aveva a
    // 45 secondi, invece di continuare ad accorciarsi).
    private bool robosbirroWasPresent;
    private float difficultyFreezeElapsedTime;

    void Update()
    {
        // Il boss è morto: fine degli spawn per questa partita (vedi RoutState, attivato da
        // RobosbirroHealth.Die()), gli Sbirro rimasti scappano invece di essere rimpiazzati.
        if (RoutState.IsActive)
        {
            return;
        }

        elapsedTime += Time.deltaTime;
        timer += Time.deltaTime;

        if (timer < GetCurrentSpawnInterval())
        {
            return;
        }

        timer = 0f;
        SpawnEnemy();
    }

    private float GetCurrentSpawnInterval()
    {
        float difficultyElapsedTime = elapsedTime;

        bool robosbirroPresent = FindAnyObjectByType<RobosbirroHealth>() != null;
        if (robosbirroPresent)
        {
            if (!robosbirroWasPresent)
            {
                // Primo frame col Robosbirro in scena: congela la difficoltà al 15% del
                // tempo di gioco trascorso finora (se entra dopo 5 min, resta come a 45 secondi).
                difficultyFreezeElapsedTime = elapsedTime * 0.15f;
            }

            difficultyElapsedTime = difficultyFreezeElapsedTime;
        }

        robosbirroWasPresent = robosbirroPresent;

        // L'intervallo si accorcia in modo lineare col tempo di gioco (spawn sempre più frequenti),
        // ma non scende mai sotto minSpawnInterval.
        return Mathf.Max(minSpawnInterval, spawnInterval - difficultyElapsedTime * intervalDecreasePerSecond);
    }

    private void SpawnEnemy()
    {
        GameObject enemyPrefab = GetRandomEnemyPrefab();
        if (enemyPrefab == null)
        {
            return;
        }

        // Se non si trova una posizione libera entro maxSpawnAttempts, si rinuncia
        // allo spawn di questo ciclo piuttosto che sovrapporre i nemici.
        if (TryGetFreeSpawnPosition(out Vector3 spawnPosition))
        {
            Instantiate(enemyPrefab, spawnPosition, transform.rotation);
        }
    }

    private GameObject GetRandomEnemyPrefab()
    {
        float totalWeight = 0f;
        foreach (EnemyOption option in enemyOptions)
        {
            if (option.prefab != null)
            {
                totalWeight += option.weight;
            }
        }

        if (totalWeight <= 0f)
        {
            return null;
        }

        float roll = Random.Range(0f, totalWeight);
        foreach (EnemyOption option in enemyOptions)
        {
            if (option.prefab == null)
            {
                continue;
            }

            roll -= option.weight;
            if (roll <= 0f)
            {
                return option.prefab;
            }
        }

        return null;
    }

    private bool TryGetFreeSpawnPosition(out Vector3 result)
    {
        for (int i = 0; i < maxSpawnAttempts; i++)
        {
            Vector3 candidate = GetRandomPositionAroundSpawner();

            // Controlla che non ci sia già un nemico entro minSpawnDistance dal punto candidato.
            if (!Physics.CheckSphere(candidate, minSpawnDistance * 0.5f, enemyLayer))
            {
                result = candidate;
                return true;
            }
        }

        result = Vector3.zero;
        return false;
    }

    private Vector3 GetRandomPositionAroundSpawner()
    {
        // Punto casuale entro un cerchio di raggio spawnRadius, sul piano orizzontale (X/Z),
        // centrato sulla posizione dello spawner: è l'offset di generazione richiesto.
        // spawnHeight solleva il punto sopra il terreno, così il nemico non nasce incassato nel pavimento.
        Vector2 randomOffset = Random.insideUnitCircle * spawnRadius;
        Vector3 candidate = transform.position + new Vector3(randomOffset.x, spawnHeight, randomOffset.y);

        // Se lo spawner è vicino a un bordo (es. agli angoli dell'arena), il cerchio di spawn
        // potrebbe sporgere fuori dal pavimento: si riporta il punto entro i bounds del floor.
        if (floor != null)
        {
            Bounds bounds = GetFloorBounds();
            candidate.x = Mathf.Clamp(candidate.x, bounds.min.x, bounds.max.x);
            candidate.z = Mathf.Clamp(candidate.z, bounds.min.z, bounds.max.z);
        }

        return candidate;
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
