using UnityEngine;

// Feedback di colpo n.2: il nemico colpito arretra leggermente, lontano dal punto da cui è
// partita l'onda. Si iscrive da solo a HitEvents. Per toglierlo cancella questo file.
//
// I nemici si muovono con rb.MovePosition (EnemyChase, EnemyRangedAttack), che parte sempre da
// rb.position. Il Runner gira PRIMA di loro (DefaultExecutionOrder) e sposta rb.position di un
// pezzetto per ogni FixedUpdate: così il loro passo successivo parte già dal punto arretrato e
// lo spostamento si somma al loro invece di venire sovrascritto. Muri e bordo arena li gestiscono
// poi loro come sempre (ClampToFloor).
public static class HitKnockbackFeedback
{
    private const float Distance = 0.6f;  // quanto arretra in tutto (unità)
    private const float Duration = 0.12f; // in quanto tempo

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        HitEvents.EnemyHit -= OnEnemyHit;
        HitEvents.EnemyHit += OnEnemyHit;
    }

    private static void OnEnemyHit(GameObject enemy, Vector3 origin, Vector3 direction)
    {
        // I boss (l'IdroSbirro è un camion, CyberGiorgia) non vengono spostati dai colpi.
        if (enemy == null || (enemy.TryGetComponent(out EnemyHealth health) && health.IsBoss))
        {
            return;
        }

        Rigidbody rb = enemy.GetComponent<Rigidbody>();
        if (rb == null)
        {
            return;
        }

        Runner runner = enemy.GetComponent<Runner>();
        if (runner == null)
        {
            runner = enemy.AddComponent<Runner>();
        }

        runner.Push(rb, direction);
    }

    [DefaultExecutionOrder(-100)]
    private class Runner : MonoBehaviour
    {
        private Rigidbody rb;
        private Vector3 direction;
        private float elapsed = Duration; // fermo finché non arriva un colpo

        // Un nuovo colpo durante il contraccolpo riparte da capo nella nuova direzione,
        // invece di sommarsi: con lo sparo continuo i nemici non devono volare via.
        public void Push(Rigidbody body, Vector3 dir)
        {
            rb = body;
            direction = dir;
            elapsed = 0f;
        }

        void FixedUpdate()
        {
            if (rb == null || elapsed >= Duration)
            {
                return;
            }

            // Ease-out: lo strattone è tutto all'inizio, poi si ferma morbido.
            float before = EaseOut(elapsed / Duration);
            elapsed = Mathf.Min(elapsed + Time.fixedDeltaTime, Duration);
            float after = EaseOut(elapsed / Duration);

            // In berserk i nemici arretrano di più (PlayerBerserk.KnockbackMultiplier).
            rb.position += direction * (Distance * PlayerBerserk.KnockbackMultiplier * (after - before));
        }

        private static float EaseOut(float t)
        {
            return 1f - (1f - t) * (1f - t);
        }
    }
}
