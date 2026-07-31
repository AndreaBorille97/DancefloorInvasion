using UnityEngine;

// Da mettere sul prefab DrugAmmo, con un Collider impostato come Trigger (Is Trigger = true).
// Quando il Player lo tocca, raddoppia per buffDuration secondi vita, danno e velocità di
// tutti i Raver già presenti in scena in quel momento (vedi RaverDrugBuff). I Raver generati
// dopo la raccolta non vengono influenzati. Se non viene raccolto entro lifespan secondi,
// scompare comunque da solo (stesso comportamento di RaverPickup/CamperPickup).
public class DrugPowerUpPickup : MonoBehaviour
{
    [Header("Effetto")]
    [SerializeField] private float buffDuration = 20f; // N: durata in secondi del raddoppio

    [Header("Pickup")]
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

        foreach (RaverDrugBuff raverBuff in FindObjectsByType<RaverDrugBuff>(FindObjectsSortMode.None))
        {
            raverBuff.Activate(buffDuration);
        }

        Destroy(gameObject);
    }
}
