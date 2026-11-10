using UnityEngine;

// Da mettere sul prefab BerserkAmmo (Assets/Prefabs/BerserkAmmo.prefab), con un Collider
// impostato come Trigger (Is Trigger = true). Quando il Player lo tocca attiva subito la modalità
// berserk (vedi PlayerBerserk.Activate) e si distrugge. Se non viene raccolto entro lifespan
// secondi, scompare comunque da solo (come KholeAmmoPickup).
//
// Il fare bella figura (sollevata da terra, che gira e ondeggia, con le scintille intorno e lo
// sbuffo di stelline alla raccolta) è tutto di CollectibleShine, sullo stesso oggetto.
public class BerserkAmmoPickup : MonoBehaviour
{
    [SerializeField] private float lifespan = 10f; // N: secondi dopo cui il powerup scompare se non raccolto

    void Start()
    {
        Destroy(gameObject, lifespan);
    }

    void OnTriggerEnter(Collider other)
    {
        PlayerBerserk berserk = other.GetComponent<PlayerBerserk>();
        if (berserk == null)
        {
            return;
        }

        berserk.Activate();
        PlayerMolecules.Show(PlayerMolecules.Molecola.Alcool);
        Destroy(gameObject);
    }
}
