using UnityEngine;

// Da mettere su un GameObject in scena, al centro della mappa (lo piazza lì Tools/CyberGiorgia/
// Monta il boss): dopo spawnDelay secondi di gioco genera CyberGiorgia, una volta sola, nel punto
// in cui sta questo oggetto, coi piedi sul pavimento (FloorBounds). Il resto (segno rosso,
// caduta, atterraggio) lo fa lei, vedi CyberGiorgiaBoss.
public class CyberGiorgiaSpawner : MonoBehaviour
{
    [SerializeField] private GameObject bossPrefab;
    [Tooltip("Secondi di gioco dopo cui arriva CyberGiorgia. Per le prove: 0 = subito.")]
    [SerializeField] private float spawnDelay = 180f;

    private float elapsed;
    private bool spawned;

    void Update()
    {
        // Partita già finita (vedi RoutState): non arriva più.
        if (spawned || RoutState.IsActive)
        {
            return;
        }

        elapsed += Time.deltaTime;
        if (elapsed < spawnDelay)
        {
            return;
        }

        spawned = true;
        if (bossPrefab == null)
        {
            Debug.LogWarning($"[CyberGiorgiaSpawner] '{name}': nessun prefab del boss assegnato.");
            return;
        }

        Vector3 position = transform.position;
        if (FloorBounds.Instance != null)
        {
            position.y = FloorBounds.Instance.Bounds.max.y;
        }

        Instantiate(bossPrefab, position, Quaternion.identity);
    }
}
