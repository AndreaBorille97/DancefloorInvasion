using UnityEngine;

// Da mettere sul prefab SerotoninAmmo (Assets/Prefabs/SerotoninAmmo.prefab, il modello pink
// punisher che prima usavano gli scatti), con un Collider impostato come Trigger (Is Trigger =
// true). Quando il Player lo tocca gli dà una carica del potere serotonina (vedi
// PlayerSerotonin.AddCharges), da usare con R2 quando vuole, e si distrugge. Se non viene
// raccolto entro lifespan secondi, scompare comunque da solo (come KholeAmmoPickup).
//
// Il fare bella figura (sollevata da terra, che gira e ondeggia, con le scintille intorno e lo
// sbuffo di stelline alla raccolta) è tutto di CollectibleShine, sullo stesso oggetto.
public class SerotoninAmmoPickup : MonoBehaviour
{
    [SerializeField] private float lifespan = 10f; // N: secondi dopo cui il powerup scompare se non raccolto
    [Tooltip("Quante cariche serotonina dà una raccolta.")]
    [SerializeField] private int serotonins = 1;

    void Start()
    {
        Destroy(gameObject, lifespan);
    }

    void OnTriggerEnter(Collider other)
    {
        PlayerSerotonin serotonin = other.GetComponent<PlayerSerotonin>();
        if (serotonin == null)
        {
            return;
        }

        // In modalità facile niente tasca: parte subito.
        if (GameDifficulty.IsEasy)
        {
            serotonin.ActivateNow();
        }
        else
        {
            serotonin.AddCharges(serotonins);
        }
        Destroy(gameObject);
    }
}
