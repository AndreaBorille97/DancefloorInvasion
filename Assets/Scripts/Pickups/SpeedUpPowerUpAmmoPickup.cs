using UnityEngine;

// Da mettere sul prefab SpeedUpPowerUpAmmo (Assets/Prefabs/SpeedUpPowerUpAmmo.prefab), con
// un Collider impostato come Trigger (Is Trigger = true). Quando il Player lo tocca gli dà una
// carica di scatto (vedi PlayerDash.AddCharges), da usare con R1 quando vuole, e si distrugge. Se
// invece non viene raccolto entro lifespan secondi, scompare comunque da solo (stesso
// comportamento degli altri pickup).
//
// Il fare bella figura (sollevata da terra, che gira e ondeggia, con le scintille intorno e lo
// sbuffo di stelline alla raccolta) è tutto di CollectibleShine, sullo stesso oggetto: si
// arrangia da solo, qui non c'è niente da chiamare.
public class SpeedUpPowerUpAmmoPickup : MonoBehaviour
{
    [SerializeField] private float lifespan = 10f; // N: secondi dopo cui il powerup scompare se non raccolto
    [Tooltip("Quante cariche di scatto dà una raccolta.")]
    [SerializeField] private int dashes = 1;

    void Start()
    {
        Destroy(gameObject, lifespan);
    }

    void OnTriggerEnter(Collider other)
    {
        PlayerDash dash = other.GetComponent<PlayerDash>();
        if (dash == null)
        {
            return;
        }

        dash.AddCharges(dashes);
        Destroy(gameObject);
    }
}
