using UnityEngine;

// Creato da RobosbirroHealth.Die() su un GameObject a parte (non sul Robosbirro stesso,
// che viene distrutto subito): il suo compito è aspettare che l'ultimo Sbirro rimasto in
// scena sparisca (nessun EnemyHealth trovato) prima di mettere in pausa il gioco e
// mostrare il pannello di vittoria, dato che nel frattempo gli Sbirro scappano invece di
// attaccare (vedi RoutState) e Raver/Player continuano a poterli eliminare.
public class BossDefeatedSequence : MonoBehaviour
{
    private GameObject victoryPanel;

    public void Init(GameObject victoryPanel)
    {
        this.victoryPanel = victoryPanel;
    }

    void Update()
    {
        if (FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None).Length > 0)
        {
            return;
        }

        Time.timeScale = 0f;
        if (victoryPanel != null)
        {
            victoryPanel.SetActive(true);
        }

        Destroy(gameObject);
    }
}
