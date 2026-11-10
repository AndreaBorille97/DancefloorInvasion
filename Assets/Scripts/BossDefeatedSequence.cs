using UnityEngine;

// Creato da FinalVictory quando sono morti il boss e tutti gli IdroSbirro, su un GameObject a
// parte (chi è morto per ultimo viene distrutto subito): victoryDelay secondi dopo dà la vittoria
// (WinState, pausa e pannello di WinUI). Nel frattempo gli Sbirro rimasti tornano agli spawner
// invece di attaccare (vedi RoutState) e Raver/Player continuano a poterli eliminare: chi è
// ancora in giro allo scadere resta lì, la vittoria non lo aspetta.
public class BossDefeatedSequence : MonoBehaviour
{
    private const float victoryDelay = 10f;

    private float waited;

    void Start()
    {
        Debug.Log($"[BossDefeatedSequence] Boss e IdroSbirro abbattuti: vittoria fra {victoryDelay:0} s.");
    }

    void Update()
    {
        waited += Time.deltaTime;
        if (waited < victoryDelay)
        {
            return;
        }

        Debug.Log("[BossDefeatedSequence] Vittoria.");
        WinState.Trigger();
        Destroy(gameObject);
    }
}
