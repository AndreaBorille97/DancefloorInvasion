using UnityEngine;

// Tempo per le animazioni di scena (sound system: acid lines, occhio, faretti...). In gioco è il
// tempo normale; mentre è aperto il menu della modalità (DifficultyMenu, gioco in pausa con
// timeScale 0) va in tempo reale, così il palco resta vivo mentre la folla balla.
public static class StageClock
{
    // Secondi passati nel menu, sommati al tempo di gioco: così Time non torna indietro quando
    // il menu si chiude.
    private static float menuSeconds;

    public static float DeltaTime => DifficultyMenu.IsOpen ? UnityEngine.Time.unscaledDeltaTime : UnityEngine.Time.deltaTime;
    public static float Time => UnityEngine.Time.time + menuSeconds;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        menuSeconds = 0f;
    }

    // Chiamato da DifficultyMenu ad ogni frame in cui è aperto.
    public static void AdvanceMenu(float seconds)
    {
        menuSeconds += seconds;
    }
}
