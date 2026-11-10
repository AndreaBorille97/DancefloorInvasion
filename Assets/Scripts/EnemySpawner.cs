using UnityEngine;

// Da associare a un GameObject "spawner" in scena: ogni tot secondi
// istanzia un nemico in una posizione casuale attorno allo spawner,
// evitando di sovrapporlo a nemici già presenti. Dopo idroSbirroDelay genera
// anche il suo IdroSbirro, una volta sola.
public class EnemySpawner : MonoBehaviour
{
    [System.Serializable]
    private class EnemyOption
    {
        public GameObject prefab;   // tipo di nemico da spawnare
        public float weight = 1f;   // peso relativo: più alto = più probabile rispetto agli altri
        [Tooltip("Secondi di gioco trascorsi prima che questo tipo possa comparire (0 = disponibile da subito). Es. Sbirro2 dopo 60s, Sbirro3 dopo 120s.")]
        public float unlockTime = 0f;
    }

    [Header("Spawn")]
    [SerializeField] private EnemyOption[] enemyOptions; // tipi di nemico spawnabili, con probabilità relativa
    [SerializeField] private float spawnInterval = 2f; // intervallo di spawn iniziale (secondi tra uno spawn e l'altro)

    [Header("Difficoltà crescente")]
    [Tooltip("Di quanto si accorcia l'intervallo di spawn per ogni secondo di gioco trascorso: valori più alti fanno aumentare la velocità di spawn più in fretta.")]
    [SerializeField] private float intervalDecreasePerSecond = 0.004f;
    [SerializeField] private float minSpawnInterval = 0.3f; // intervallo minimo, sotto il quale non si scende anche a tempo infinito
    [Tooltip("Moltiplica l'intervallo di spawn in ogni momento della partita, minimo compreso: 2 = metà degli Sbirro, 1 = come da valori sopra.")]
    [SerializeField] private float spawnIntervalMultiplier = 2f;
    [Tooltip("Quanti Sbirro in meno mentre CyberGiorgia è in campo (dal segno rosso alla sua morte): 0.3 = il 30% in meno.")]
    [Range(0f, 0.9f)] [SerializeField] private float bossSpawnReduction = 0.3f;

    [Header("Posizionamento")]
    [SerializeField] private float spawnRadius = 3f;      // raggio entro cui scegliere una posizione casuale attorno allo spawner
    [SerializeField] private float spawnHeight = 0.35f;   // altezza da terra a cui spawnare (pivot del nemico rispetto al terreno), evita che nasca dentro al pavimento
    [SerializeField] private float minSpawnDistance = 1.5f; // distanza minima richiesta da altri nemici già presenti
    [SerializeField] private LayerMask enemyLayer;          // layer dei nemici, usato per controllare le sovrapposizioni
    [SerializeField] private int maxSpawnAttempts = 10;      // tentativi massimi per trovare una posizione libera
    [Tooltip("Oggetto \"floor\": se assegnato, le posizioni di spawn vengono ristrette ai suoi bounds, utile per spawner vicini ai bordi dell'arena. Ignorato se è impostato un percorso (path): in quel caso lo spawner sta fuori dall'arena di proposito.")]
    [SerializeField] private Transform floor;

    [Header("Percorso obbligatorio")]
    [Tooltip("Se assegnato, ogni nemico generato segue questo percorso (vedi EnemyPath) dallo spawn fino all'ultimo waypoint, ignorando bersagli e aggro, poi passa all'AI normale. Pensato per spawner piazzati fuori dall'arena, così i nemici fanno un giro prestabilito ed entrano da dove vuoi tu invece che dal punto più vicino.")]
    [SerializeField] private EnemyPath path;

    [Header("Arrivo del Player")]
    [Tooltip("Appena il Player atterra (DifficultyMenu.PlayerLanded) nasce uno Sbirro a questi metri fuori dal bordo dell'arena, sulla linea fra lo spawner e il centro: quando si comincia a giocare ce n'è già uno che sta per entrare. 0 = niente.")]
    [SerializeField] private float landingSpawnOutside = 4f;

    [Header("IdroSbirro")]
    [Tooltip("Generato una volta sola, dopo idroSbirroDelay: segue il percorso qui sopra e va alla sua postazione (vedi IdroSbirro). Vuoto = nessun IdroSbirro da questo spawner.")]
    [SerializeField] private GameObject idroSbirroPrefab;
    [Tooltip("Secondi di gioco dopo cui arriva l'IdroSbirro.")]
    [SerializeField] private float idroSbirroDelay = 240f;
    [Tooltip("Solo per le prove: lo spawner genera subito l'IdroSbirro e nessun altro Sbirro.")]
    [SerializeField] private bool onlyIdroSbirro = false;

    // Usato da FinalVictory per sapere quanti IdroSbirro vanno abbattuti per vincere.
    public bool SpawnsIdroSbirro => idroSbirroPrefab != null && isActiveAndEnabled;

    private float timer;
    private float elapsedTime; // tempo totale trascorso dall'avvio, usato per accorciare l'intervallo di spawn
    private bool idroSbirroSpawned;
    private bool sawLandingWait;
    private bool landingSpawnDone;

    void Start()
    {
        if (onlyIdroSbirro)
        {
            SpawnIdroSbirro();
        }

        // Il primo Sbirro nasce al primo Update, senza aspettare un intervallo intero: dallo spawner
        // all'arena c'è già tanta strada. Col menu della modalità aperto (timeScale 0) resta fermo
        // allo spawner e parte appena si sceglie.
        timer = float.MaxValue;
    }

    void Update()
    {
        // Partita finita (boss e IdroSbirro abbattuti, vedi RoutState): fine degli spawn, gli
        // Sbirro rimasti tornano allo spawner invece di essere rimpiazzati.
        if (RoutState.IsActive || onlyIdroSbirro)
        {
            return;
        }

        UpdateLandingSpawn();

        elapsedTime += Time.deltaTime;
        timer += Time.deltaTime;

        if (elapsedTime >= idroSbirroDelay)
        {
            SpawnIdroSbirro();
        }

        if (timer < GetCurrentSpawnInterval())
        {
            return;
        }

        timer = 0f;
        SpawnEnemy();
    }

    private float GetCurrentSpawnInterval()
    {
        // L'intervallo si accorcia in modo lineare col tempo di gioco (spawn sempre più frequenti),
        // ma non scende mai sotto minSpawnInterval.
        float interval = spawnIntervalMultiplier * Mathf.Max(minSpawnInterval, spawnInterval - elapsedTime * intervalDecreasePerSecond);
        // Il 30% di Sbirro in meno = intervallo diviso 0.7 (non moltiplicato 1.3, che ne toglierebbe il 23%).
        return CyberGiorgiaBoss.IsPresent ? interval / (1f - bossSpawnReduction) : interval;
    }

    // Quelli nati allo spawner sono ancora per strada quando il Player atterra (spawner a ~60 m,
    // passo 3 m/s): uno in più, già sul bordo, perché la partita cominci con qualcuno in arrivo.
    // Niente percorso: è già oltre il giro, e con un percorso tornerebbe indietro al primo waypoint.
    private void UpdateLandingSpawn()
    {
        if (landingSpawnDone)
        {
            return;
        }

        // PlayerLanded è vero anche senza menu della modalità: conta solo il passaggio da falso a vero.
        if (!DifficultyMenu.PlayerLanded)
        {
            sawLandingWait = true;
            return;
        }

        landingSpawnDone = true;
        if (!sawLandingWait || landingSpawnOutside <= 0f)
        {
            return;
        }

        GameObject enemyPrefab = GetRandomEnemyPrefab();
        if (enemyPrefab == null || !TryGetArenaEdgePoint(out Vector3 position, out Vector3 inward))
        {
            return;
        }

        Instantiate(enemyPrefab, position, Quaternion.LookRotation(inward));
    }

    // Il punto landingSpawnOutside metri fuori dal bordo dell'arena (FloorBounds), sulla linea
    // fra lo spawner e il centro. Falso se lo spawner sta già dentro l'arena.
    private bool TryGetArenaEdgePoint(out Vector3 position, out Vector3 inward)
    {
        position = Vector3.zero;
        inward = Vector3.forward;
        if (FloorBounds.Instance == null)
        {
            return false;
        }

        Bounds arena = FloorBounds.Instance.Bounds;
        Vector3 outward = transform.position - arena.center;
        outward.y = 0f;
        float tx = Mathf.Abs(outward.x) > 0.001f ? arena.extents.x / Mathf.Abs(outward.x) : float.PositiveInfinity;
        float tz = Mathf.Abs(outward.z) > 0.001f ? arena.extents.z / Mathf.Abs(outward.z) : float.PositiveInfinity;
        float t = Mathf.Min(tx, tz);
        if (t >= 1f)
        {
            return false;
        }

        Vector3 edge = arena.center + outward * t;
        position = edge + outward.normalized * landingSpawnOutside;
        position.y = transform.position.y + spawnHeight;
        inward = -outward.normalized;
        return true;
    }

    private void SpawnIdroSbirro()
    {
        if (idroSbirroSpawned || idroSbirroPrefab == null)
        {
            return;
        }

        idroSbirroSpawned = true;
        GameObject idroSbirro = Instantiate(idroSbirroPrefab, transform.position, transform.rotation);
        if (path != null && idroSbirro.TryGetComponent(out IdroSbirro truck))
        {
            truck.SetRoute(path.GetWorldPoints());
        }
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
            GameObject enemy = Instantiate(enemyPrefab, spawnPosition, transform.rotation);
            AssignPath(enemy);
        }
    }

    private void AssignPath(GameObject enemy)
    {
        if (path == null)
        {
            return;
        }

        // Il nemico ha EnemyChase (melee) oppure EnemyRangedAttack (a distanza), mai
        // entrambi: si prova con tutti e due e chi c'è riceve il percorso.
        if (enemy.TryGetComponent(out EnemyChase chase))
        {
            chase.SetPath(path);
        }

        if (enemy.TryGetComponent(out EnemyRangedAttack ranged))
        {
            ranged.SetPath(path);
        }
    }

    private GameObject GetRandomEnemyPrefab()
    {
        float totalWeight = 0f;
        foreach (EnemyOption option in enemyOptions)
        {
            if (option.prefab != null && elapsedTime >= option.unlockTime)
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
            if (option.prefab == null || elapsedTime < option.unlockTime)
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
        // Con un percorso assegnato invece lo spawner sta fuori apposta: clampare lo
        // riporterebbe dentro l'arena, vanificando il giro.
        if (floor != null && path == null)
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

        Debug.LogWarning($"[EnemySpawner DEBUG] '{name}': nessun Renderer/Collider trovato su '{floor.name}' (activeInHierarchy={floor.gameObject.activeInHierarchy}) -> bounds a size zero in {floor.position}. Tutti gli spawn verranno clampati su questo punto.");
        return new Bounds(floor.position, Vector3.zero);
    }
}
