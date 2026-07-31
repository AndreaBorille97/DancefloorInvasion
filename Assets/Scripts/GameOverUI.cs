using UnityEngine;

// Da mettere su un GameObject in scena (es. "GameManager"). Mostra il pannello di
// sconfitta e mette in pausa il gioco non appena GameOverState viene attivato, da
// qualunque fonte (Player, Console/DJ, SoundSystem) — stesso schema di pausa+pannello
// già usato da BossDefeatedSequence per la vittoria.
public class GameOverUI : MonoBehaviour
{
    [SerializeField] private GameObject gameOverPanel;

    void OnEnable()
    {
        GameOverState.Triggered += HandleGameOver;
    }

    void OnDisable()
    {
        GameOverState.Triggered -= HandleGameOver;
    }

    private void HandleGameOver()
    {
        Time.timeScale = 0f;
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
        }
    }
}
