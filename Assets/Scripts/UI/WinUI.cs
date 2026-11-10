using System.Collections;
using UnityEngine;

// Da mettere su un GameObject in scena (es. "GameManager"). Mostra il pannello di vittoria
// e mette in pausa il gioco non appena WinState viene attivato (vedi BossDefeatedSequence),
// stesso schema di GameOverUI per la sconfitta, e dopo returnToMenuSeconds torna al menu
// principale (SceneRestart). Senza un pannello assegnato scrive "VITTORIA" a schermo, così la
// fine della partita non sembra un blocco del gioco.
public class WinUI : MonoBehaviour
{
    [SerializeField] private GameObject winPanel;
    [Tooltip("Secondi (reali) fra la comparsa della vittoria e il ritorno al menu principale.")]
    [SerializeField] private float returnToMenuSeconds = 2f;

    private bool showFallback;
    private GUIStyle fallbackStyle;

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
        else
        {
            showFallback = true;
        }
        StartCoroutine(ReturnToMenu());
    }

    // Tempo reale: la vittoria mette in pausa (timeScale 0), un'attesa normale non finirebbe mai.
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
            fallbackStyle.normal.textColor = Color.white;
        }

        fallbackStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.12f);
        GUI.Label(new Rect(0f, 0f, Screen.width, Screen.height), "VITTORIA", fallbackStyle);
    }
}
