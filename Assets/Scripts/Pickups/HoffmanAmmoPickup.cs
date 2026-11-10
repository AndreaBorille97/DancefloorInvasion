using UnityEngine;

// Da mettere sul prefab HoffmanAmmo (Assets/Prefabs/HoffmanAmmo.prefab), con un Collider
// impostato come Trigger (Is Trigger = true). Quando il Player lo tocca gli dà un Hoffman in
// tasca (vedi PlayerHoffman.AddCharges), da lanciare con L1 quando vuole, e si distrugge.
// Se invece non viene raccolto entro lifespan secondi, scompare comunque da solo (stesso
// comportamento di SpeedUpPowerUpAmmoPickup).
//
// Il fare bella figura (sollevata da terra, che gira e ondeggia, con le scintille intorno e lo
// sbuffo di stelline alla raccolta) è tutto di CollectibleShine, sullo stesso oggetto.
public class HoffmanAmmoPickup : MonoBehaviour
{
    [SerializeField] private float lifespan = 10f; // N: secondi dopo cui il powerup scompare se non raccolto
    [Tooltip("Quanti Hoffman dà una raccolta.")]
    [SerializeField] private int blackHoles = 1;

    void Start()
    {
        Destroy(gameObject, lifespan);
    }

    void OnTriggerEnter(Collider other)
    {
        PlayerHoffman thrower = other.GetComponent<PlayerHoffman>();
        if (thrower == null)
        {
            return;
        }

        thrower.AddCharges(blackHoles);
        Destroy(gameObject);
    }
}
