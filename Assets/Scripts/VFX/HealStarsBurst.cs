using UnityEngine;

// Sbuffo di stelline verdi che salgono da chi viene curato, stile "salute +" dei videogiochi.
// Lo usa RaverHealth.Heal ad ogni onda sonora che cura un Raver.
//
// ParticleSystem costruito a runtime e distrutto da solo a fine effetto, con la stellina di
// StarParticleTexture e Sprites/Default (stesso approccio di CollectibleShine: shader sicuro
// anche in URP, niente asset da collegare).
public static class HealStarsBurst
{
    private const float Lifetime = 0.7f;
    private static readonly Color StarColor = new Color(0.35f, 1f, 0.4f, 1f);

    private static Material starMaterial; // condiviso da tutti gli sbuffi

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetState()
    {
        starMaterial = null;
    }

    public static void Spawn(GameObject target, int count)
    {
        if (target == null || count <= 0)
        {
            return;
        }

        Bounds bounds = MeasureBounds(target);

        GameObject burstObject = new GameObject("HealStars");
        // Al petto: a metà fra il centro e la cima dell'ingombro.
        burstObject.transform.position = new Vector3(bounds.center.x, Mathf.Lerp(bounds.center.y, bounds.max.y, 0.4f), bounds.center.z);

        ParticleSystem particles = burstObject.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.duration = 0.1f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(Lifetime * 0.7f, Lifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
        main.startColor = StarColor;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = -0.25f; // salgono, rallentando come se galleggiassero
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });

        // Da un disco orizzontale largo quanto il Raver, così escono tutt'intorno al corpo.
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = Mathf.Max(0.25f, Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.8f);
        shape.rotation = new Vector3(90f, 0f, 0f);
        shape.randomDirectionAmount = 0.3f;

        ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.y = new ParticleSystem.MinMaxCurve(1.2f, 2f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        ParticleSystem.RotationOverLifetimeModule rotation = particles.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-3f, 3f);

        // Compaiono di scatto, poi si rimpiccioliscono e svaniscono.
        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0f, 1f) },
        });

        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.6f), new Keyframe(0.15f, 1.1f), new Keyframe(1f, 0.3f)));

        ParticleSystemRenderer particleRenderer = burstObject.GetComponent<ParticleSystemRenderer>();
        if (starMaterial == null)
        {
            starMaterial = new Material(MaterialLibrary.SpriteDefault) { mainTexture = StarParticleTexture.Get() };
        }
        particleRenderer.sharedMaterial = starMaterial;
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        particleRenderer.receiveShadows = false;

        particles.Play();
        Object.Destroy(burstObject, Lifetime + 0.2f);
    }

    private static Bounds MeasureBounds(GameObject target)
    {
        bool found = false;
        Bounds bounds = new Bounds(target.transform.position + Vector3.up, Vector3.one);
        foreach (Renderer r in target.GetComponentsInChildren<Renderer>())
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

        return bounds;
    }
}
