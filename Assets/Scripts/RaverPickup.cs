using UnityEngine;

// Da mettere sull'oggetto raccoglibile "Raver Ammo" in scena, con un Collider impostato
// come Trigger (Is Trigger = true). Quando il Player lo tocca, genera un Raver nel punto
// in cui si trovava il pickup e si distrugge. Se non viene raccolto entro lifespan
// secondi, scompare comunque da solo (stesso comportamento di CamperPickup).
public class RaverPickup : MonoBehaviour
{
    [SerializeField] private GameObject raverPrefab; // prefab del Raver da generare alla raccolta
    [SerializeField] private float lifespan = 10f; // N: secondi dopo cui il powerup scompare se non raccolto

    void Awake()
    {
#if UNITY_EDITOR
        // Stesso errore comune di PlayerShooting.projectilePrefab: trascinare l'oggetto
        // dalla Hierarchy invece del vero Prefab asset dalla cartella Project.
        if (raverPrefab != null && !UnityEditor.EditorUtility.IsPersistent(raverPrefab))
        {
            Debug.LogError($"{nameof(RaverPickup)}: '{nameof(raverPrefab)}' punta a un'istanza nella scena, non a un Prefab asset. Trascina il prefab dalla cartella Project (Assets), non l'oggetto dalla Hierarchy.", this);
        }
#endif
    }

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

        if (raverPrefab != null)
        {
            Instantiate(raverPrefab, transform.position, transform.rotation);
        }

        Destroy(gameObject);
    }
}
