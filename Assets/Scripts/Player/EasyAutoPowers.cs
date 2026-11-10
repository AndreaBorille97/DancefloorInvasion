using UnityEngine;

// Modalità facile (vedi GameDifficulty): khole e Hoffman non si lanciano a mano, ci pensa il
// gioco. Appena intorno al Player ci sono almeno minEnemies Sbirro (entro detectionRadius) e c'è
// una carica in tasca, il potere parte da solo. L'Hoffman ha la precedenza sul khole; il khole non
// parte mentre l'Hoffman è aperto (il Player è piantato a terra, attraversare i nemici non servirebbe).
// Boss e Sbirro convertiti dalla serotonina non contano. Il khole parte anche quando il Player è
// vicino a CyberGiorgia, da sola (vedi IsNearCyberGiorgia).
//
// In modalità normale non fa niente. Si aggiunge da solo al Player all'avvio, come PlayerKhole.
[RequireComponent(typeof(PlayerHoffman), typeof(PlayerKhole))]
public class EasyAutoPowers : MonoBehaviour
{
    [Tooltip("Quanti Sbirro devono stare intorno al Player perché parta il potere.")]
    [SerializeField] private int minEnemies = 3;
    [Tooltip("Entro quanti metri dal Player si contano gli Sbirro. Sta dentro il raggio dell'Hoffman (10.5), così quelli contati muoiono davvero.")]
    [SerializeField] private float detectionRadius = 6f;
    [Tooltip("Entro quanti metri dal centro di CyberGiorgia il khole parte anche senza altri Sbirro intorno. Più largo di detectionRadius perché lei è grossa.")]
    [SerializeField] private float bossRadius = 8f;
    [Tooltip("Ogni quanti secondi si contano gli Sbirro intorno.")]
    [SerializeField] private float checkInterval = 0.2f;

    private PlayerHoffman hoffman;
    private PlayerKhole khole;
    private float timer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToPlayer()
    {
        PlayerMovement player = FindAnyObjectByType<PlayerMovement>();
        if (player == null || player.GetComponent<EasyAutoPowers>() != null)
        {
            return;
        }

        // Se parte prima degli AttachToPlayer di PlayerHoffman/PlayerKhole, RequireComponent li
        // aggiunge qui e i loro controlli GetComponent evitano i doppioni.
        player.gameObject.AddComponent<EasyAutoPowers>();
    }

    void Awake()
    {
        hoffman = GetComponent<PlayerHoffman>();
        khole = GetComponent<PlayerKhole>();
    }

    void Update()
    {
        if (!GameDifficulty.IsEasy || (hoffman.Charges == 0 && khole.Charges == 0))
        {
            return;
        }

        timer -= Time.deltaTime;
        if (timer > 0f)
        {
            return;
        }
        timer = checkInterval;

        bool crowded = CountEnemiesAround() >= minEnemies;
        if (crowded && hoffman.TryCast())
        {
            return;
        }

        // Vicino a CyberGiorgia il khole parte anche senza la ressa: l'Hoffman su di lei non ha
        // effetto, attraversarla e non farsi colpire sì.
        if ((crowded || IsNearCyberGiorgia()) && !hoffman.IsPlaying)
        {
            khole.TryActivate();
        }
    }

    private bool IsNearCyberGiorgia()
    {
        CyberGiorgiaBoss boss = CyberGiorgiaBoss.Instance;
        if (boss == null)
        {
            return false;
        }

        Vector3 offset = boss.transform.position - transform.position;
        offset.y = 0f;
        return offset.sqrMagnitude <= bossRadius * bossRadius;
    }

    private int CountEnemiesAround()
    {
        float sqrRadius = detectionRadius * detectionRadius;
        Vector3 center = transform.position;
        int count = 0;
        foreach (EnemyHealth enemy in EnemyHealth.All)
        {
            if (enemy.IsBoss || enemy.IsCharmed || enemy.CurrentHealth <= 0f)
            {
                continue;
            }

            Vector3 offset = enemy.transform.position - center;
            offset.y = 0f;
            if (offset.sqrMagnitude <= sqrRadius)
            {
                count++;
            }
        }

        return count;
    }
}
