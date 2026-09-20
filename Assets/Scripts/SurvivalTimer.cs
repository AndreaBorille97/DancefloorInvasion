using UnityEngine;

// Da mettere su un GameObject in scena (es. "GameManager"). Non innesca più la vittoria da
// solo (vedi IdroSbirroSpawner, che ora la fa scattare alla morte dell'IdroSbirro): serve
// solo a esporre RemainingSeconds per il conto alla rovescia mostrato in minimappa (vedi
// MinimapUI), tenuto con lo stesso survivalDuration di IdroSbirroSpawner.spawnTime così il
// countdown arriva a 0 esattamente quando l'IdroSbirro compare.
public class SurvivalTimer : MonoBehaviour
{
    [Tooltip("Secondi dall'inizio della partita dopo cui compare l'IdroSbirro: stesso valore di IdroSbirroSpawner.spawnTime, qui serve solo per il conto alla rovescia in minimappa.")]
    [SerializeField] private float survivalDuration = 240f;

    private float elapsed;

    // Usato dalla minimappa (vedi MinimapUI) per mostrare il conto alla rovescia.
    public float RemainingSeconds => Mathf.Max(0f, survivalDuration - elapsed);

    void Update()
    {
        elapsed += Time.deltaTime;
    }
}
