using UnityEngine;

// Da mettere sul prefab LifeUpPowerUpAmmo, con un Collider impostato come Trigger
// (Is Trigger = true). Quando il Player lo tocca, gli restituisce subito una vita (vedi
// PlayerHealth.AddLife) e si distrugge: non c'è munizione da accumulare, l'effetto è
// immediato. Il massimo raggiungibile resta quello impostato su PlayerHealth (maxHits),
// cioè la vita con cui il Player parte. Se non viene raccolto entro lifespan secondi,
// scompare comunque da solo (stesso comportamento di SpeedUpPowerUpAmmoPickup).
public class LifeUpPowerUpAmmoPickup : MonoBehaviour
{
    [SerializeField] private float lifespan = 10f; // N: secondi dopo cui il powerup scompare se non raccolto

    void Start()
    {
        Destroy(gameObject, lifespan);
    }

    void OnTriggerEnter(Collider other)
    {
        PlayerHealth playerHealth = other.GetComponent<PlayerHealth>();
        if (playerHealth == null)
        {
            return;
        }

        playerHealth.AddLife(1);
        Destroy(gameObject);
    }
}
