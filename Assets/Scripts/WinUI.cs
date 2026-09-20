using UnityEngine;

// Da mettere su un GameObject in scena (es. "GameManager"). Mostra il pannello di vittoria
// e mette in pausa il gioco non appena WinState viene attivato (vedi SurvivalTimer),
// stesso schema di GameOverUI per la sconfitta.
public class WinUI : MonoBehaviour
{
    [SerializeField] private GameObject winPanel;

    void OnEnable()
    {
        WinState.Triggered += HandleWin;
    }

    void OnDisable()
    {
        WinState.Triggered -= HandleWin;
    }

    private void HandleWin()
    {
        Time.timeScale = 0f;
        if (winPanel != null)
        {
            winPanel.SetActive(true);
        }
    }
}
