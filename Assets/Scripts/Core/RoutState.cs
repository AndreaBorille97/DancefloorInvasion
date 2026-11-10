using UnityEngine;

// Stato globale di fine partita, attivato da FinalVictory quando sono morti boss e IdroSbirro.
// Mentre è attivo:
// - EnemyChase ed EnemyRangedAttack fanno tornare gli Sbirro rimasti allo spawner da cui
//   sono venuti, a ritroso lungo il percorso d'arrivo, e lì li fanno sparire (vedi RoutHome);
// - EnemyAttack non fa più partire attacchi;
// - EnemySpawner smette definitivamente di generare nuovi nemici.
// Non è legato a un GameObject apposta: deve restare leggibile anche nei frame successivi
// alla distruzione di chi l'ha attivato.
public static class RoutState
{
    public static bool IsActive { get; private set; }

    // Senza ricaricamento del dominio all'avvio del Play (Enter Play Mode Options) resterebbe
    // attivo dalla partita prima, e gli spawner non partirebbero.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        IsActive = false;
    }

    public static void Activate()
    {
        IsActive = true;
    }
}
