// Stato globale attivato da RobosbirroHealth alla morte del boss. Mentre è attivo:
// - EnemyChase ed EnemyRangedAttack fanno scappare gli Sbirro rimasti dal bersaglio più
//   vicino (Player o Raver) invece di inseguirlo/attaccarlo;
// - EnemySpawner smette definitivamente di generare nuovi nemici.
// Non è legato a un GameObject apposta: deve restare leggibile anche nei frame successivi
// alla distruzione del Robosbirro stesso.
public static class RoutState
{
    public static bool IsActive { get; private set; }

    public static void Activate()
    {
        IsActive = true;
    }
}
