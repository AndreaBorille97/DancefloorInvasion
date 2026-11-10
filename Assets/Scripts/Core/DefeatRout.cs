using UnityEngine;

// Sconfitta per obiettivo distrutto (Console o SoundSystem, vedi DefeatSequence): la festa è
// finita. Il gioco non va in pausa (GameOverUI): i Raver mollano le postazioni e scappano dallo
// Sbirro più vicino, gli Sbirro smettono di badare a obiettivi e corsie e danno la caccia ai Raver
// ancora in piedi (EnemyChase.AcquireNearestTarget). Dopo qualche secondo si torna al menu
// (GameOverUI, SceneRestart).
//
// Meccanica semplice apposta: ogni tanto ogni Raver sceglie un punto qualche metro più in là,
// dalla parte opposta allo Sbirro più vicino, dentro il pavimento, e ci corre (RaverChase.SetForcedPosition).
public class DefeatRout : MonoBehaviour
{
    public static bool IsActive { get; private set; }

    // Ogni quanto i Raver riscelgono da che parte scappare.
    private const float FleeInterval = 0.3f;
    // Sbirro più lontani di così non mettono paura: il Raver resta dov'è arrivato.
    private const float ScareRadius = 14f;
    // Quanti metri più in là punta ogni volta.
    private const float FleeStep = 6f;
    // Margine dai bordi del pavimento.
    private const float FloorMargin = 1f;

    private float timer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        IsActive = false;
    }

    public static void Activate()
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        new GameObject("DefeatRout").AddComponent<DefeatRout>();
    }

    // Per EnemyChase: il Raver in piedi più vicino, preferendo a parità quelli non ancora
    // inseguiti da nessuno, così gli Sbirro si sparpagliano invece di finire tutti sullo stesso.
    public static Transform FindNearestRaver(Vector3 from)
    {
        Transform nearest = null;
        float bestScore = float.MaxValue;
        foreach (RaverHealth raver in RaverHealth.All)
        {
            if (raver.IsDown)
            {
                continue;
            }

            float score = Vector3.Distance(raver.transform.position, from) + raver.EngagedCount * 3f;
            if (score < bestScore)
            {
                bestScore = score;
                nearest = raver.transform;
            }
        }
        return nearest;
    }

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0f)
        {
            return;
        }
        timer = FleeInterval;

        System.Collections.Generic.List<EnemyHealth> sbirri = EnemyHealth.All;
        foreach (RaverHealth raver in RaverHealth.All)
        {
            // A terra va alla chillout per conto suo (RaverHealth): non lo si tocca.
            if (raver.IsDown || !raver.TryGetComponent(out RaverChase chase))
            {
                continue;
            }

            // Scappa e basta: niente più pugni.
            if (raver.TryGetComponent(out RaverAttack attack))
            {
                attack.enabled = false;
            }

            Vector3 position = raver.transform.position;
            Vector3 away = Vector3.zero;
            float nearestSqr = ScareRadius * ScareRadius;
            foreach (EnemyHealth sbirro in sbirri)
            {
                if (sbirro.IsCharmed)
                {
                    continue;
                }

                Vector3 offset = position - sbirro.transform.position;
                offset.y = 0f;
                if (offset.sqrMagnitude < nearestSqr)
                {
                    nearestSqr = offset.sqrMagnitude;
                    away = offset;
                }
            }

            if (away == Vector3.zero)
            {
                continue;
            }

            Vector3 destination = position + away.normalized * FleeStep;
            if (FloorBounds.Instance != null)
            {
                Bounds floor = FloorBounds.Instance.Bounds;
                destination.x = Mathf.Clamp(destination.x, floor.min.x + FloorMargin, floor.max.x - FloorMargin);
                destination.z = Mathf.Clamp(destination.z, floor.min.z + FloorMargin, floor.max.z - FloorMargin);
            }
            chase.SetForcedPosition(destination);
        }
    }
}
