using UnityEngine;

// Da mettere sul prefab CamperAmmo raccoglibile in scena (Assets/Prefabs/CamperAmmo.prefab),
// con un Collider impostato come Trigger (Is Trigger = true). Quando il Player lo tocca,
// rabbocca al massimo la benzina del Camper piazzato in scena (vedi CamperVehicle.RefillFuel)
// e si distrugge. Il Raver va mandato al Camper a piacere premendo L2 (vedi
// PlayerCamperSummon), non più automaticamente alla raccolta. Se non viene raccolto entro
// lifespan secondi, scompare comunque da solo.
public class CamperPickup : MonoBehaviour
{
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

        FindAnyObjectByType<CamperVehicle>()?.RefillFuel();

        Destroy(gameObject);
    }
}
