using UnityEngine;

// Feedback di colpo n.2: lineette di "spostamento d'aria" che sfrecciano dietro al nemico colpito,
// nella direzione in cui l'onda lo attraversa. Si iscrive da solo a HitEvents. Per toglierlo
// cancella questo file.
public static class HitStreaksFeedback
{
    private const int Count = 6;
    private const float Lifetime = 0.2f;
    private const float Speed = 3.5f; // con Lifetime fa ~0.7 m di corsa: arrivano a filo del modello partendo da vicino
    private const float Width = 0.06f;
    private const float Length = 1.2f; // lunghezza di ogni lineetta, in metri
    private const float SideSpread = 0.5f; // quanto sono sparse ai lati del nemico
    private const float HeightSpread = 0.6f; // e in altezza, attorno al petto
    private static readonly Color StreakColor = new Color(1f, 1f, 1f, 0.8f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        HitEvents.EnemyHit -= OnEnemyHit;
        HitEvents.EnemyHit += OnEnemyHit;
    }

    private static void OnEnemyHit(GameObject enemy, Vector3 origin, Vector3 direction)
    {
        if (enemy == null)
        {
            return;
        }

        Vector3 chest = MeasureChestPoint(enemy, direction, out float backDistance);
        Spawn(chest, direction, backDistance);
    }

    private static void Spawn(Vector3 center, Vector3 direction, float backDistance)
    {
        GameObject streaksObject = new GameObject("HitStreaks");
        streaksObject.transform.position = center;

        ParticleSystem particles = streaksObject.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // altrimenti la durata non si può cambiare

        ParticleSystem.MainModule main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = Lifetime;
        main.startLifetime = Lifetime;
        main.startSpeed = 0f; // velocità data riga per riga con Emit
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Count;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = false;

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.4f), new GradientAlphaKey(0f, 1f) },
        });

        // Stretched Billboard: ogni particella è un trattino lungo nella direzione in cui vola.
        // Lunghezza = Width * lengthScale + velocità * velocityScale.
        ParticleSystemRenderer particleRenderer = streaksObject.GetComponent<ParticleSystemRenderer>();
        particleRenderer.material = new Material(Shader.Find("Sprites/Default"));
        particleRenderer.material.mainTexture = RoundParticleTexture.Get();
        particleRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        particleRenderer.lengthScale = 1f;
        particleRenderer.velocityScale = (Length - Width) / Speed;
        particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        particles.Play();

        // Alla schiena del nemico (il lato opposto a quello da cui arriva l'onda), sparse ai lati e
        // in altezza, e si muovono in direzione opposta al colpo, verso la schiena: svaniscono
        // appena fuori dal modello, senza attraversarlo.
        Vector3 side = Vector3.Cross(Vector3.up, direction);
        for (int i = 0; i < Count; i++)
        {
            Vector3 start = center
                + direction * (backDistance + Random.Range(0.4f, 0.7f))
                + side * Random.Range(-SideSpread, SideSpread)
                + Vector3.up * Random.Range(-HeightSpread, HeightSpread);

            particles.Emit(
                new ParticleSystem.EmitParams
                {
                    position = start,
                    velocity = -direction * (Speed * Random.Range(0.8f, 1.2f)),
                    startLifetime = Lifetime * Random.Range(0.8f, 1f),
                    startSize = Width,
                    startColor = StreakColor,
                },
                1);
        }

        Object.Destroy(streaksObject, Lifetime + 0.1f);
    }

    // Petto: a metà fra il centro e la cima dell'ingombro del modello (il pivot degli Sbirro è ai piedi).
    // backDistance: quanto il modello si estende dal centro verso la schiena (lungo direction).
    private static Vector3 MeasureChestPoint(GameObject enemy, Vector3 direction, out float backDistance)
    {
        bool found = false;
        Bounds bounds = new Bounds(enemy.transform.position, Vector3.zero);
        foreach (Renderer r in enemy.GetComponentsInChildren<Renderer>())
        {
            if (!(r is MeshRenderer || r is SkinnedMeshRenderer))
            {
                continue;
            }

            if (!found)
            {
                bounds = r.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        backDistance = found
            ? bounds.extents.x * Mathf.Abs(direction.x) + bounds.extents.z * Mathf.Abs(direction.z)
            : 0.5f;

        return found
            ? new Vector3(bounds.center.x, Mathf.Lerp(bounds.center.y, bounds.max.y, 0.4f), bounds.center.z)
            : enemy.transform.position + Vector3.up;
    }
}
