using System;

// Stato globale attivato quando il Player sopravvive abbastanza a lungo (vedi
// SurvivalTimer): per ora finisce subito la partita in vittoria; in futuro, al suo posto,
// da qui partirà la boss battle. Non è legato a un GameObject apposta, stesso schema di
// GameOverState/RoutState: chi lo innesca potrebbe essere distrutto nello stesso frame.
public static class WinState
{
    public static bool IsWon { get; private set; }
    public static event Action Triggered;

    public static void Trigger()
    {
        if (IsWon)
        {
            return;
        }

        IsWon = true;
        Triggered?.Invoke();
    }

    // Usato solo da un eventuale restart di partita: senza resettarlo, WinState resterebbe
    // attivo anche ricaricando la scena (static, sopravvive al reload).
    public static void Reset()
    {
        IsWon = false;
        Triggered = null;
    }
}
