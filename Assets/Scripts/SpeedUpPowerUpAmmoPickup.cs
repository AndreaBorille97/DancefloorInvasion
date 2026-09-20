using UnityEngine;

// Da mettere sul prefab SpeedUpPowerUpAmmo (Assets/Prefabs/SpeedUpPowerUpAmmo.prefab), con
// un Collider impostato come Trigger (Is Trigger = true). Quando il Player lo tocca, gli dà
// una munizione Speed (vedi PlayerSpeedAmmo) e si distrugge. Se invece non viene raccolto
// entro lifespan secondi, scompare comunque da solo (stesso comportamento di CamperPickup).
//
// Il fare bella figura (sollevata da terra, che gira e ondeggia, con le scintille intorno e lo
// sbuffo di stelline alla raccolta) è tutto di CollectibleShine, sullo stesso oggetto: si
// arrangia da solo, qui non c'è niente da chiamare.
public class SpeedUpPowerUpAmmoPickup : MonoBehaviour
{
    [SerializeField] private float lifespan = 10f; // N: secondi dopo cui il powerup scompare se non raccolto

    void Start()
    {
        Destroy(gameObject, lifespan);
    }

    void OnTriggerEnter(Collider other)
    {
        PlayerSpeedAmmo ammo = other.GetComponent<PlayerSpeedAmmo>();
        if (ammo == null)
        {
            return;
        }

        ammo.AddAmmo(1);
        Destroy(gameObject);
    }
}
