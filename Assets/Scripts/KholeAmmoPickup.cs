using UnityEngine;

// Da mettere sul prefab KholeAmmo (Assets/Prefabs/KholeAmmo.prefab), con un Collider impostato
// come Trigger (Is Trigger = true). Quando il Player lo tocca gli dà una carica del potere khole
// (vedi PlayerKhole.AddCharges), da usare con L2 quando vuole, e si distrugge. Se non viene
// raccolto entro lifespan secondi, scompare comunque da solo (come HoffmanAmmoPickup).
//
// Il fare bella figura (sollevata da terra, che gira e ondeggia, con le scintille intorno e lo
// sbuffo di stelline alla raccolta) è tutto di CollectibleShine, sullo stesso oggetto.
public class KholeAmmoPickup : MonoBehaviour
{
    [SerializeField] private float lifespan = 10f; // N: secondi dopo cui il powerup scompare se non raccolto
    [Tooltip("Quante cariche khole dà una raccolta.")]
    [SerializeField] private int kholes = 1;

    void Start()
    {
        Destroy(gameObject, lifespan);
    }

    void OnTriggerEnter(Collider other)
    {
        PlayerKhole khole = other.GetComponent<PlayerKhole>();
        if (khole == null)
        {
            return;
        }

        khole.AddCharges(kholes);
        Destroy(gameObject);
    }
}
