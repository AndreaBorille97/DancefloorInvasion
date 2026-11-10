using System;
using UnityEngine;

// Punto unico da cui partono i feedback di colpo (HitFlashFeedback, HitRingFeedback,
// HitKnockbackFeedback, HitSoundFeedback). Chi colpisce (SoundWaveProjectile) chiama solo
// RaiseEnemyHit; ogni feedback è un file a sé che si iscrive da solo all'avvio, senza nulla
// da collegare in scena o sui prefab. Per toglierne uno basta cancellarne il file (+ .meta);
// per toglierli tutti cancella anche questo e la riga HitEvents.RaiseEnemyHit in SoundWaveProjectile.
public static class HitEvents
{
    // nemico colpito, punto da cui è partita l'onda, direzione orizzontale normalizzata dall'onda al nemico
    public static event Action<GameObject, Vector3, Vector3> EnemyHit;

    public static void RaiseEnemyHit(GameObject enemy, Vector3 origin)
    {
        if (enemy == null || EnemyHit == null)
        {
            return;
        }

        Vector3 direction = enemy.transform.position - origin;
        direction.y = 0f;
        direction = direction.sqrMagnitude < 0.0001f ? Vector3.forward : direction.normalized;

        EnemyHit.Invoke(enemy, origin, direction);
    }

    // Nemico (EnemyHealth) morto, per qualunque causa: onda o scatto. Lo usa PlayerBerserk
    // per far tremare lo schermo più forte ad ogni uccisione.
    public static event Action<GameObject> EnemyKilled;

    public static void RaiseEnemyKilled(GameObject enemy)
    {
        EnemyKilled?.Invoke(enemy);
    }

    // Sbirro convertito da un'onda serotonina invece di morire (EnemyHealth.TakeSerotoninDamage).
    // PlayerBerserk lo tratta come un'uccisione: tremolio forte e hit-stop.
    public static event Action<GameObject> EnemyConverted;

    public static void RaiseEnemyConverted(GameObject enemy)
    {
        EnemyConverted?.Invoke(enemy);
    }
}
