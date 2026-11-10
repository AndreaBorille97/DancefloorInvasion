using System;
using UnityEngine;

// Modalità di gioco scelta a inizio partita dal menu (vedi DifficultyMenu).
//
// Normale = il gioco com'è sempre stato. Facile:
// - i Raver hanno il triplo della vita (RaverHealth, anche quelli già in scena alla scelta);
// - la probabilità che uno Sbirro punti dritto all'obiettivo cresce più lentamente: invece di
//   guardie a terra / guardie totali è il suo quadrato (EnemyChase.DirectAttackChance), quindi
//   con metà guardie a terra sfonda un nemico su quattro invece che uno su due, e tutti solo a
//   barriera completamente a terra come prima;
// - i power-up partono appena raccolti (serotonina compresa, vedi SerotoninAmmoPickup), tranne lo
//   scatto (resta a cariche su R1) e khole/Hoffman, che il gioco lancia da solo quando servono
//   (vedi EasyAutoPowers).
public static class GameDifficulty
{
    public enum Mode { Normal, Easy }

    // Moltiplicatore della vita dei Raver in modalità facile.
    public const float EasyRaverHealthMultiplier = 3f;

    public static Mode Current { get; private set; } = Mode.Normal;
    public static bool IsEasy => Current == Mode.Easy;

    // Chiamato una volta, quando il giocatore sceglie dal menu.
    public static event Action<Mode> Chosen;

    // Con "Enter Play Mode" senza ricarica del dominio i campi statici sopravvivono tra una
    // partita e l'altra nell'Editor: si riparte sempre da Normale.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Current = Mode.Normal;
        Chosen = null;
    }

    public static void Choose(Mode mode)
    {
        Current = mode;
        Chosen?.Invoke(mode);
    }
}
