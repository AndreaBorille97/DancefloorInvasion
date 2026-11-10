using UnityEngine;

// La condizione di vittoria: la partita è vinta quando sono morti sia il boss (CyberGiorgia) sia
// tutti gli IdroSbirro, in qualunque ordine. Gli IdroSbirro da abbattere sono uno per ogni
// EnemySpawner che ne genera uno (vedi EnemySpawner.SpawnsIdroSbirro), oggi due.
// A quel punto non arrivano più Sbirro, quelli rimasti tornano agli spawner (RoutState), e
// quando se ne sono andati scatta la vittoria (BossDefeatedSequence, poi WinState).
// Statica come RoutState: chi muore viene distrutto nello stesso frame in cui la avvisa.
public static class FinalVictory
{
    private static bool bossKilled;
    private static int idroSbirroKilled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        bossKilled = false;
        idroSbirroKilled = 0;
    }

    // Da CyberGiorgiaBoss.OnDestroy, solo se è morta davvero.
    public static void BossKilled()
    {
        bossKilled = true;
        Check();
    }

    // Da IdroSbirro.OnDestroy, solo se è morto davvero.
    public static void IdroSbirroKilled()
    {
        idroSbirroKilled++;
        Check();
    }

    private static void Check()
    {
        Debug.Log($"[FinalVictory] Boss abbattuto: {(bossKilled ? "sì" : "no")}, IdroSbirro abbattuti {idroSbirroKilled}/{CountIdroSbirro()}"
            + (DefeatSequence.IsRunning ? " (sconfitta in corso: niente vittoria)" : ""));
        // Sotto il game over la partita continua (GameOverUI): una vittoria lì non deve scattare.
        if (DefeatSequence.IsRunning || !bossKilled || RoutState.IsActive || idroSbirroKilled < CountIdroSbirro())
        {
            return;
        }

        RoutState.Activate();
        new GameObject("BossDefeatedSequence").AddComponent<BossDefeatedSequence>();
    }

    private static int CountIdroSbirro()
    {
        int count = 0;
        foreach (EnemySpawner spawner in Object.FindObjectsByType<EnemySpawner>())
        {
            if (spawner.SpawnsIdroSbirro)
            {
                count++;
            }
        }
        return count;
    }
}
