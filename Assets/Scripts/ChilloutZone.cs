using UnityEngine;

// Da mettere sul GameObject "Chillout" in scena, unico. È il punto verso cui va un Raver
// quando si esaurisce (vedi RaverHealth.GoDown): mentre è lì si cura lentamente da solo,
// oltre a poter essere aiutato dal cono del Player come di consueto. Stesso schema
// singleton statico di FloorBounds, così RaverHealth lo trova senza bisogno di un
// riferimento cablato a mano in Inspector su ogni singolo Raver.
public class ChilloutZone : MonoBehaviour
{
    public static ChilloutZone Instance { get; private set; }

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
