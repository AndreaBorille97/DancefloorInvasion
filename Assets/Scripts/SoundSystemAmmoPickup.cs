using UnityEngine;

// Da mettere sul prefab SoundSystemAmmo (Assets/Prefabs/SoundSystemAmmo.prefab), con il
// Collider impostato come Trigger (Is Trigger = true). Quando il Player lo tocca, attiva
// il sound system presente in scena (vedi SoundSystem.Activate, che da quel momento
// spara come il Player da ognuna delle 4 casse) e si distrugge. Se non viene raccolto
// entro lifespan secondi, scompare comunque da solo (stesso comportamento di
// RaverPickup/CamperPickup).
//
// Niente riferimento serializzato al sound system: essendo SoundSystemAmmo un prefab
// asset (spawnato a runtime da SoundSystemAmmoSpawner), l'Editor non permette di
// assegnargli un oggetto che vive solo nella scena. Lo cerchiamo quindi con
// FindAnyObjectByType, stesso approccio di DrugPowerUpPickup con RaverDrugBuff.
public class SoundSystemAmmoPickup : MonoBehaviour
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

        FindAnyObjectByType<SoundSystem>()?.Activate();

        Destroy(gameObject);
    }
}
