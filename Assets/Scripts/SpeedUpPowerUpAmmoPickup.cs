using UnityEngine;

// Da mettere sul prefab SpeedUpPowerUpAmmo (Assets/Prefabs/SpeedUpPowerUpAmmo.prefab), con
// un Collider impostato come Trigger (Is Trigger = true). Quando il Player lo tocca, gli ricarica
// un po' di stamina (vedi PlayerStamina) e si distrugge. Se invece non viene raccolto
// entro lifespan secondi, scompare comunque da solo (stesso comportamento di CamperPickup).
//
// Il fare bella figura (sollevata da terra, che gira e ondeggia, con le scintille intorno e lo
// sbuffo di stelline alla raccolta) è tutto di CollectibleShine, sullo stesso oggetto: si
// arrangia da solo, qui non c'è niente da chiamare.
public class SpeedUpPowerUpAmmoPickup : MonoBehaviour
{
    [SerializeField] private float lifespan = 10f; // N: secondi dopo cui il powerup scompare se non raccolto
    [Tooltip("Quanta stamina ricarica (la barra piena vale PlayerStamina.maxStamina, di default 100). È l'unico modo di ricaricarla: la stamina non torna da sola.")]
    [SerializeField] private float staminaAmount = 30f;

    void Start()
    {
        Destroy(gameObject, lifespan);
    }

    void OnTriggerEnter(Collider other)
    {
        PlayerStamina stamina = other.GetComponent<PlayerStamina>();
        if (stamina == null)
        {
            return;
        }

        stamina.AddStamina(staminaAmount);
        Destroy(gameObject);
    }
}
