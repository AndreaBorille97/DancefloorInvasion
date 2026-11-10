using UnityEngine;

// Da mettere su un GameObject in scena (es. "GameManager"). Non innesca la vittoria (vedi
// FinalVictory): serve solo a esporre RemainingSeconds per il conto alla rovescia mostrato in
// minimappa (vedi MinimapUI), che segna l'arrivo del boss. Va tenuto allo stesso tempo di
// CyberGiorgiaSpawner.spawnDelay.
public class SurvivalTimer : MonoBehaviour
{
    [Tooltip("Secondi dall'inizio della partita dopo cui arriva il boss (CyberGiorgiaSpawner.spawnDelay): qui serve solo per il conto alla rovescia in minimappa.")]
    [SerializeField] private float survivalDuration = 180f;

    private float elapsed;

    // Usato dalla minimappa (vedi MinimapUI) per mostrare il conto alla rovescia.
    public float RemainingSeconds => Mathf.Max(0f, survivalDuration - elapsed);

    void Update()
    {
        elapsed += Time.deltaTime;
    }
}
