using UnityEngine;
using UnityEngine.Rendering;

// La morte da Hoffman (PlayerHoffman): lo Sbirro esplode in un lampo psichedelico invece di
// schizzare via in polvere. Un lampo bianco al centro, una raffica di sfere che volano via e
// frenano, una spirale di scintille che gira attorno al punto, tre anelli che corrono a terra
// e una luce che cambia colore. Ogni particella scorre l'arcobaleno per conto suo (vedi
// LateUpdate): i colori non stanno mai fermi.
//
// Lo Sbirro vero muore subito (EnemyHealth.HoffmanExplode lo distrugge come ogni altra morte):
// qui c'è solo l'effetto, che si autodistrugge a fine esplosione.
public class PsychedelicExplosion : MonoBehaviour
{
    private const float Lifetime = 2.8f;   // poi l'oggetto si distrugge (le bolle vivono fino a 2.4 s)
    private const float HueSpeed = 1.6f;   // giri dell'arcobaleno al secondo
    private const int MaxParticles = 512;

    private ParticleSystem[] systems;
    private Material bubbleMaterial;
    private ParticleSystem.Particle[] buffer;
    private Light glow;
    private float size;
    private float elapsed;

    // Va chiamato prima di distruggere lo Sbirro: misura il modello per dare all'esplosione la sua taglia.
    public static void Spawn(GameObject enemy)
    {
        Vector3 center = enemy.transform.position + Vector3.up;
        float height = 2f;
        bool any = false;
        Bounds bounds = default;
        foreach (Renderer renderer in enemy.GetComponentsInChildren<Renderer>())
        {
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
            {
                continue;
            }
            if (any)
            {
                bounds.Encapsulate(renderer.bounds);
            }
            else
            {
                bounds = renderer.bounds;
                any = true;
            }
        }
        if (any)
        {
            center = bounds.center;
            height = Mathf.Max(1f, bounds.size.y);
        }

        GameObject go = new GameObject("PsychedelicExplosion");
        go.transform.position = center;
        PsychedelicExplosion explosion = go.AddComponent<PsychedelicExplosion>();
        explosion.size = height * 0.5f;
        explosion.Build(height * 0.5f);
    }

    private void Build(float groundOffset)
    {
        Material material = new Material(MaterialLibrary.SpriteDefault);
        material.mainTexture = RoundParticleTexture.Get();

        // Lampo: una sola sfera bianca che si gonfia e sparisce.
        ParticleSystem flash = CreateSystem("Lampo", material, Vector3.zero, Quaternion.identity);
        SetMain(flash, 0.25f, 0.25f, 0f, 0f, size * 2.6f, size * 3.2f, 0f);
        flash.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
        SetShape(flash, ParticleSystemShapeType.Sphere, 0.01f);
        GrowThenShrink(flash, 0.6f, 1.4f, 0f);

        // Raffica: sfere colorate sparate ovunque, frenate dall'aria.
        ParticleSystem orbs = CreateSystem("Sfere", material, Vector3.zero, Quaternion.identity);
        SetMain(orbs, 0.7f, 1.3f, 5f * size, 11f * size, 0.15f * size, 0.45f * size, -0.3f);
        orbs.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 60) });
        SetShape(orbs, ParticleSystemShapeType.Sphere, 0.2f * size);
        Drag(orbs, 0.12f);
        GrowThenShrink(orbs, 1f, 1.2f, 0f);

        // Spirale: scintille che girano attorno al centro allargandosi.
        ParticleSystem spiral = CreateSystem("Spirale", material, Vector3.zero, Quaternion.identity);
        SetMain(spiral, 1f, 1.6f, 0.5f * size, 1.5f * size, 0.08f * size, 0.2f * size, -0.15f);
        spiral.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 70), new ParticleSystem.Burst(0.15f, 50) });
        SetShape(spiral, ParticleSystemShapeType.Sphere, 0.6f * size);
        ParticleSystem.VelocityOverLifetimeModule velocity = spiral.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = velocity.y = velocity.z = new ParticleSystem.MinMaxCurve(0f);
        velocity.orbitalY = new ParticleSystem.MinMaxCurve(7f);
        velocity.radial = new ParticleSystem.MinMaxCurve(2.5f * size);
        GrowThenShrink(spiral, 1f, 1f, 0f);

        // Anelli a terra: tre onde di particelle che si allargano, una dopo l'altra.
        ParticleSystem rings = CreateSystem("Anelli", material, Vector3.down * groundOffset + Vector3.up * 0.1f, Quaternion.Euler(90f, 0f, 0f));
        SetMain(rings, 0.6f, 0.75f, 9f * size, 9f * size, 0.22f * size, 0.3f * size, 0f);
        rings.emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, 72), new ParticleSystem.Burst(0.12f, 72), new ParticleSystem.Burst(0.24f, 72)
        });
        ParticleSystem.ShapeModule ringShape = rings.shape;
        ringShape.enabled = true;
        ringShape.shapeType = ParticleSystemShapeType.Circle;
        ringShape.radius = 0.3f * size;
        ringShape.radiusThickness = 0f;
        ringShape.arcMode = ParticleSystemShapeMultiModeValue.BurstSpread;
        GrowThenShrink(rings, 1f, 1f, 0.2f);

        // Bolle: sfere trasparenti col bordo lucido (BubbleTexture) che schizzano fuori, frenano,
        // salgono ondeggiando e alla fine scoppiano (si gonfiano di colpo e spariscono).
        bubbleMaterial = new Material(material);
        bubbleMaterial.mainTexture = BubbleTexture();
        ParticleSystem bubbles = CreateSystem("Bolle", bubbleMaterial, Vector3.zero, Quaternion.identity);
        SetMain(bubbles, 1.4f, 2.4f, 1.5f * size, 4.5f * size, 0.1f * size, 0.28f * size, -0.12f);
        bubbles.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 40), new ParticleSystem.Burst(0.1f, 15) });
        SetShape(bubbles, ParticleSystemShapeType.Sphere, 0.5f * size);
        Drag(bubbles, 0.08f);
        ParticleSystem.NoiseModule noise = bubbles.noise;
        noise.enabled = true;
        noise.strength = 0.6f * size;
        noise.frequency = 0.8f;
        noise.scrollSpeed = 0.6f;
        ParticleSystem.SizeOverLifetimeModule bubbleSize = bubbles.sizeOverLifetime;
        bubbleSize.enabled = true;
        bubbleSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.3f), new Keyframe(0.2f, 0.75f), new Keyframe(0.9f, 0.8f), new Keyframe(0.97f, 1f), new Keyframe(1f, 0f)));

        systems = new[] { flash, orbs, spiral, rings, bubbles };
        buffer = new ParticleSystem.Particle[MaxParticles];

        GameObject lightObject = new GameObject("Luce");
        lightObject.transform.SetParent(transform, false);
        glow = lightObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.range = 9f * size;
        glow.shadows = LightShadows.None;

        foreach (ParticleSystem system in systems)
        {
            system.Play();
        }
        Recolor();
    }

    private ParticleSystem CreateSystem(string name, Material material, Vector3 localPosition, Quaternion localRotation)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = localRotation;

        ParticleSystem system = go.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = 0f;

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return system;
    }

    private static void SetMain(ParticleSystem system, float minLife, float maxLife, float minSpeed, float maxSpeed,
                                float minSize, float maxSize, float gravity)
    {
        ParticleSystem.MainModule main = system.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 0.5f;
        main.maxParticles = MaxParticles;
        main.startLifetime = new ParticleSystem.MinMaxCurve(minLife, maxLife);
        main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
    }

    private static void SetShape(ParticleSystem system, ParticleSystemShapeType type, float radius)
    {
        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = true;
        shape.shapeType = type;
        shape.radius = radius;
    }

    private static void Drag(ParticleSystem system, float dampen)
    {
        ParticleSystem.LimitVelocityOverLifetimeModule limit = system.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = new ParticleSystem.MinMaxCurve(0f);
        limit.dampen = dampen;
    }

    // Grandezza nel tempo: parte da start, arriva a peak a un quarto della vita e torna a end.
    private static void GrowThenShrink(ParticleSystem system, float start, float peak, float end)
    {
        float top = Mathf.Max(start, peak, end, 0.0001f);
        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = system.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(top,
            new AnimationCurve(new Keyframe(0f, start / top), new Keyframe(0.25f, peak / top), new Keyframe(1f, end / top)));
    }

    // Ogni particella ha la sua tinta di partenza (dal randomSeed) e scorre l'arcobaleno; svanisce
    // nell'ultimo terzo della vita.
    private void Recolor()
    {
        foreach (ParticleSystem system in systems)
        {
            int count = system.GetParticles(buffer);
            for (int i = 0; i < count; i++)
            {
                ParticleSystem.Particle particle = buffer[i];
                float life = particle.remainingLifetime / Mathf.Max(0.0001f, particle.startLifetime);
                float offset = (particle.randomSeed % 1000u) / 1000f;
                float hue = Mathf.Repeat(offset + elapsed * HueSpeed, 1f);
                Color color = Color.HSVToRGB(hue, 0.85f, 1f);
                color.a = Mathf.Clamp01(life * 3f);
                particle.startColor = color;
                buffer[i] = particle;
            }
            system.SetParticles(buffer, count);
        }
    }

    void LateUpdate()
    {
        elapsed += Time.deltaTime;
        if (elapsed >= Lifetime)
        {
            Destroy(gameObject);
            return;
        }

        Recolor();
        glow.color = Color.HSVToRGB(Mathf.Repeat(elapsed * HueSpeed, 1f), 0.8f, 1f);
        glow.intensity = 6f * Mathf.Clamp01(1f - elapsed / 1.2f);
    }

    void OnDestroy()
    {
        if (systems != null && systems.Length > 0 && systems[0] != null)
        {
            Destroy(systems[0].GetComponent<ParticleSystemRenderer>().sharedMaterial);
        }
        if (bubbleMaterial != null)
        {
            Destroy(bubbleMaterial);
        }
    }

    // Una bolla: dentro quasi trasparente, bordo pieno e un riflesso in alto a sinistra.
    // Disegnata una volta sola e condivisa, come RoundParticleTexture.
    private static Texture2D bubbleTexture;

    private static Texture2D BubbleTexture()
    {
        if (bubbleTexture != null)
        {
            return bubbleTexture;
        }

        const int size = 64;
        const float radius = size * 0.5f;
        bubbleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(radius, radius);
        Vector2 highlight = center + new Vector2(-0.38f, 0.38f) * radius;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float d = Vector2.Distance(p, center) / radius;
                float inside = Mathf.Clamp01((1f - d) * radius / 1.5f); // bordo anti-aliasato
                float rim = Mathf.Pow(Mathf.Clamp01(d), 4f);            // il bordo è più denso
                float shine = Mathf.Clamp01(1f - Vector2.Distance(p, highlight) / (radius * 0.22f));
                float alpha = inside * Mathf.Clamp01(0.12f + rim * 0.9f + shine * shine);
                bubbleTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        bubbleTexture.Apply();
        return bubbleTexture;
    }
}
