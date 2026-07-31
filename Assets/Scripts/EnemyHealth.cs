using UnityEngine;

// Da mettere sul prefab del nemico, insieme a EnemyChase.
// Tiene i punti vita e distrugge il nemico quando arrivano a zero.
public class EnemyHealth : MonoBehaviour, IDamageable
{
    [SerializeField] private float maxHealth = 100f; // punti vita massimi del nemico
    [Tooltip("Danno tolto da un colpo di Camper: il quadruplo di un colpo di Raver normale (RaverAttack.damage = 25), non più un instant kill.")]
    [SerializeField] private float camperDamage = 100f;

    private float currentHealth;

    void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float amount)
    {
        currentHealth -= amount;
        if (currentHealth <= 0f)
        {
            Destroy(gameObject);
        }
    }

    // Chiamato da CamperVehicle al posto dell'insta-kill: stesso schema di RobosbirroHealth.TakeCamperHit().
    public void TakeCamperHit()
    {
        TakeDamage(camperDamage);
    }
}
