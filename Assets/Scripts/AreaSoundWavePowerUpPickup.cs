using UnityEngine;

// Da mettere sul prefab AreaSoundWavePowerUpSpawner, con un Collider impostato come
// Trigger (Is Trigger = true). Quando il Player lo tocca, allarga per powerUpDuration
// secondi l'apertura del cono (coneAngle) di ogni onda sonora sparata (vedi
// PlayerSoundWavePowerUps e SoundWaveProjectile). Se non viene raccolto entro lifespan
// secondi, scompare comunque da solo (stesso comportamento di RaverPickup/CamperPickup).
public class AreaSoundWavePowerUpPickup : MonoBehaviour
{
    [Header("Effetto")]
    [SerializeField] private float bonusConeAngle = 30f; // N: gradi aggiunti all'apertura del cono mentre il powerup è attivo
    [SerializeField] private float powerUpDuration = 20f; // N: durata in secondi dell'effetto dopo la raccolta

    [Header("Pickup")]
    [SerializeField] private float lifespan = 10f; // N: secondi dopo cui il powerup scompare se non raccolto

    void Start()
    {
        Destroy(gameObject, lifespan);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player"))
        {
            return;
        }

        other.GetComponent<PlayerSoundWavePowerUps>()?.ActivateAreaPowerUp(bonusConeAngle, powerUpDuration);

        Destroy(gameObject);
    }
}
