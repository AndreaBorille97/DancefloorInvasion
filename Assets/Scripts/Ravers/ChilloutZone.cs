using UnityEngine;

// Da mettere sul GameObject "Chillout" in scena, unico, insieme a un BoxCollider che ne
// definisce la forma/dimensione (segue il modello SM_ChilloutTent). È il punto verso cui va
// un Raver quando si esaurisce (vedi RaverHealth.GoDown): mentre è lì si cura lentamente da
// solo, oltre a poter essere aiutato dal cono del Player come di consueto. Stesso schema
// singleton statico di FloorBounds, così RaverHealth lo trova senza bisogno di un
// riferimento cablato a mano in Inspector su ogni singolo Raver.
[RequireComponent(typeof(BoxCollider))]
public class ChilloutZone : MonoBehaviour
{
    [Header("Area di arrivo")]
    [Tooltip("Margine (in unità di mondo) aggiunto attorno al BoxCollider per definire l'area di arrivo (vedi ContainsArrivalPoint): un Raver in cammino verso qui si considera arrivato e si ferma subito, esattamente dov'è, appena entra in quest'area — un po' più ampia del solo box, ma non arbitrariamente grande.")]
    [SerializeField] private float arrivalMargin = 1f;

    private BoxCollider zoneCollider;

    public static ChilloutZone Instance { get; private set; }

    void Awake()
    {
        Instance = this;
        zoneCollider = GetComponent<BoxCollider>();
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // Usato da RaverHealth.CheckChilloutArrival: true se worldPosition (X/Z, l'altezza non
    // conta: il movimento del Raver è planare) è dentro l'area del BoxCollider allargata di
    // arrivalMargin su ogni lato.
    public bool ContainsArrivalPoint(Vector3 worldPosition)
    {
        Bounds bounds = zoneCollider.bounds;
        return worldPosition.x >= bounds.min.x - arrivalMargin && worldPosition.x <= bounds.max.x + arrivalMargin
            && worldPosition.z >= bounds.min.z - arrivalMargin && worldPosition.z <= bounds.max.z + arrivalMargin;
    }

    // Anche in Editor: utile per tarare arrivalMargin senza dover avviare il Play.
    void OnDrawGizmos()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null)
        {
            return;
        }

        Bounds bounds = box.bounds;
        Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.8f);
        Gizmos.DrawWireCube(bounds.center, new Vector3(bounds.size.x + arrivalMargin * 2f, bounds.size.y, bounds.size.z + arrivalMargin * 2f));
    }
}
