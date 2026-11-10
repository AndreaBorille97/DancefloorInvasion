using UnityEngine;

// Da mettere sul prefab SpeedBoostAmmo (Assets/Prefabs/SpeedBoostAmmo.prefab), con un Collider
// impostato come Trigger (Is Trigger = true). Quando il Player lo tocca corre più veloce per un
// po' (PlayerMovement.ApplySpeedBoost, che accende anche l'aura oro), compare la molecola
// dell'anfetamina e il pickup si distrugge: effetto immediato, niente da accumulare, come il
// LifeUp. Se non viene raccolto entro lifespan secondi, scompare comunque da solo.
//
// Il fare bella figura (luccichio, bolle, stelline alla raccolta) è tutto dei componenti già sul
// prefab (CollectibleShine, AmmoBubbles, CollectStarBurst): qui non c'è niente da chiamare.
public class SpeedBoostAmmoPickup : MonoBehaviour
{
    [SerializeField] private float lifespan = 10f; // N: secondi dopo cui il powerup scompare se non raccolto
    [Tooltip("Di quanto si moltiplica la velocità di camminata: 2 = +100%, il doppio.")]
    [SerializeField] private float speedMultiplier = 2f;
    [Tooltip("Secondi in cui il Player resta più veloce.")]
    [SerializeField] private float duration = 10f;

    void Start()
    {
        Destroy(gameObject, lifespan);
    }

    void OnTriggerEnter(Collider other)
    {
        PlayerMovement movement = other.GetComponent<PlayerMovement>();
        if (movement == null)
        {
            return;
        }

        movement.ApplySpeedBoost(speedMultiplier, duration);
        PlayerMolecules.Show(PlayerMolecules.Molecola.Anfetamina);
        Destroy(gameObject);
    }
}
