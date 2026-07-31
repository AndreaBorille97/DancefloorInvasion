using System;

// Stato globale attivato quando la partita finisce in sconfitta: morte del Player,
// distruzione della Console/DJ (vedi DJConsoleHealth) o del SoundSystem (vedi SoundSystem.Die).
// Non è legato a un GameObject apposta, sullo stesso schema di RoutState: chi lo innesca
// potrebbe essere distrutto nello stesso frame in cui lo fa.
public static class GameOverState
{
    public static bool IsGameOver { get; private set; }
    public static event Action Triggered;

    public static void Trigger()
    {
        if (IsGameOver)
        {
            return;
        }

        IsGameOver = true;
        Triggered?.Invoke();
    }

    // Usato solo da un eventuale restart di partita: senza resettarlo, GameOverState
    // resterebbe attivo anche ricaricando la scena (static, sopravvive al reload).
    public static void Reset()
    {
        IsGameOver = false;
        Triggered = null;
    }
}
