using System.Collections;
using UnityEngine;

// Da mettere su un GameObject in scena (es. "GameManager"). Mostra il pannello di
// sconfitta non appena GameOverState viene attivato, da qualunque fonte (Player, Console/DJ,
// SoundSystem), e dopo returnToMenuSeconds torna al menu principale (SceneRestart).
// Niente pausa: la partita continua sotto il pannello, sia se è morto il Player (gli Sbirro
// vanno avanti per conto loro) sia se è saltato un obiettivo (i Raver scappano e gli Sbirro li
// inseguono, DefeatRout).
// Senza un pannello assegnato scrive "GAME OVER" a schermo, come WinUI con "VITTORIA".
public class GameOverUI : MonoBehaviour
{
    [SerializeField] private GameObject gameOverPanel;
    [Tooltip("Secondi (reali) fra la comparsa del game over e il ritorno al menu principale.")]
    [SerializeField] private float returnToMenuSeconds = 4f;

    private bool showFallback;
    private GUIStyle fallbackStyle;

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
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
        }
        else
        {
            showFallback = true;
        }
        StartCoroutine(ReturnToMenu());
    }

    // Tempo reale: non risente di un hit-stop (PlayerBerserk) che rallenta il gioco proprio in quel momento.
    private IEnumerator ReturnToMenu()
    {
        yield return new WaitForSecondsRealtime(returnToMenuSeconds);
        SceneRestart.ReloadToMenu();
    }

    void OnGUI()
    {
        if (!showFallback)
        {
            return;
        }

        if (fallbackStyle == null)
        {
            fallbackStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
            fallbackStyle.normal.textColor = Color.red;
        }

        fallbackStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.12f);
        GUI.Label(new Rect(0f, 0f, Screen.width, Screen.height), "GAME OVER", fallbackStyle);
    }
}
