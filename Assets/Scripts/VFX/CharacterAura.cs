using UnityEngine;

// Aura attorno a un personaggio (vedi Style):
//  - RaverBoost: rossa, attorno a un Raver mentre ha il boost di vita/danno del giocoliere (vedi
//    RaverJuggler.ApplyGroupBuff e RaverHealth.SetGroupBuffTint, che la accende e la spegne).
//    Prima il boost si vedeva solo tingendo di rosso la capsula, che però con i modelli animati
//    è spenta: senza questa aura non si vedeva niente.
//  - SpeedBoost: oro metallizzato, attorno al Player mentre corre più veloce (anfetamina, vedi
//    PlayerMovement.ApplySpeedBoost). Il metallizzato sono i riflessi: lampi piccoli
//    bianco-oro che si accendono e spengono di scatto sul corpo, come la luce su un metallo.
//
// ParticleSystem costruiti a runtime, con Sprites/Default come HealStarsBurst (shader sicuro
// anche in URP, niente asset da collegare):
//  - bagliori morbidi che salgono tutt'intorno al corpo;
//  - se lo stile lo prevede, un anello sulla sabbia che si allarga e svanisce, a ripetizione.
// Spegnendola smettono solo di nascere particelle: quelle già in aria finiscono il loro giro,
// così l'aura svanisce invece di sparire di colpo.
public class CharacterAura : MonoBehaviour
{
    public enum Style { RaverBoost, SpeedBoost }

    private struct StyleSettings
    {
        public Color GlowColor;
        public float GlowRate;      // bagliori al secondo
        public float GlowSize;      // rispetto al raggio del personaggio
        public float GlowRise;      // rispetto alla sua altezza, al secondo
        public bool Ring;
        public Color RingColor;
        public float GlintRate;     // riflessi al secondo, 0 = niente riflessi
        public Color GlintColor;
        public float Spread;        // quanto si allarga attorno al corpo (1 = come RaverBoost)
        public bool FollowBody;     // particelle che seguono il personaggio invece di restargli dietro
    }

    private static StyleSettings Settings(Style style)
    {
        switch (style)
        {
            // Stretta sul corpo e che lo segue: correndo veloce, in world si allungava in una scia
            // che riempiva mezzo schermo. Il grosso del giallo metallizzato è la patina sul
            // modello (MetallicPatina); qui resta un alone sottile e i riflessi.
            case Style.SpeedBoost:
                return new StyleSettings
                {
                    GlowColor = new Color(1f, 0.8f, 0.15f, 0.45f),
                    GlowRate = 18f,
                    GlowSize = 0.9f,
                    GlowRise = 0.6f,
                    Ring = false,
                    GlintRate = 18f,
                    GlintColor = new Color(1f, 0.96f, 0.75f, 1f),
                    Spread = 0.55f,
                    FollowBody = true,
                };
            default:
                return new StyleSettings
                {
                    GlowColor = new Color(1f, 0.12f, 0.08f, 0.55f),
                    GlowRate = 22f,
                    GlowSize = 1.2f,
                    GlowRise = 0.9f,
                    Ring = true,
                    RingColor = new Color(1f, 0.15f, 0.1f, 0.7f),
                    Spread = 1f,
                };
        }
    }

    // Un material per texture, condiviso da tutti: il colore va sulle particelle, non qui.
    private static Material glowMaterial;
    private static Material ringMaterial;

    private Style style;
    private ParticleSystem glow;
    private ParticleSystem ring; // null se lo stile non ha l'anello
    private ParticleSystem glints; // null se lo stile non ha i riflessi
    private float bodyHeight;
    private bool followBody;
    private bool visible;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetState()
    {
        glowMaterial = null;
        ringMaterial = null;
    }

    // Accende o spegne l'aura di quello stile su character, creandola la prima volta che serve.
    public static void Set(GameObject character, Style style, bool on)
    {
        CharacterAura aura = null;
        foreach (CharacterAura candidate in character.GetComponents<CharacterAura>())
        {
            if (candidate.style == style)
            {
                aura = candidate;
                break;
            }
        }

        if (aura == null)
        {
            if (!on)
            {
                return;
            }
            aura = character.AddComponent<CharacterAura>();
            aura.style = style;
        }

        aura.SetVisible(on);
    }

    private void SetVisible(bool on)
    {
        if (on == visible)
        {
            return;
        }
        visible = on;

        if (glow == null)
        {
            Build();
        }

        if (on)
        {
            PlaceAtFeet();
            glow.Play();
            ring?.Play();
            glints?.Play();
        }
        else
        {
            glow.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            ring?.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            glints?.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    private void Build()
    {
        StyleSettings settings = Settings(style);
        Bounds bounds = MeasureBounds();
        // Spread stringe solo dove nascono i bagliori, non quanto sono grossi: rimpicciolendo anche
        // loro, su un modello alto 3 m l'aura dell'anfetamina era sparita.
        float bodyRadius = Mathf.Max(0.35f, Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.75f);
        float radius = bodyRadius * settings.Spread;
        float height = Mathf.Max(1f, bounds.size.y);
        followBody = settings.FollowBody;

        glow = CreateSystem("AuraGlow");
        ParticleSystem.MainModule main = glow.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.3f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(bodyRadius * settings.GlowSize * 0.75f, bodyRadius * settings.GlowSize * 1.25f);
        main.startColor = settings.GlowColor;
        main.maxParticles = 60;

        ParticleSystem.EmissionModule emission = glow.emission;
        emission.rateOverTime = settings.GlowRate;

        // Da un anello attorno ai piedi, poi su lungo il corpo.
        ParticleSystem.ShapeModule shape = glow.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius;
        shape.radiusThickness = 0.3f;
        shape.rotation = new Vector3(90f, 0f, 0f);

        ParticleSystem.VelocityOverLifetimeModule velocity = glow.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.y = new ParticleSystem.MinMaxCurve(height * settings.GlowRise * 0.8f, height * settings.GlowRise * 1.2f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        SetFade(glow, 0.25f, 0.6f);
        ParticleSystem.SizeOverLifetimeModule size = glow.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 0.25f)));

        ParticleSystemRenderer glowRenderer = glow.GetComponent<ParticleSystemRenderer>();
        glowRenderer.sharedMaterial = GetMaterial(ref glowMaterial, SoftGlowTexture());
        glowRenderer.renderMode = ParticleSystemRenderMode.Billboard;

        bodyHeight = height;
        if (settings.GlintRate > 0f)
        {
            BuildGlints(settings, radius, bodyRadius, height);
        }

        if (!settings.Ring)
        {
            return;
        }

        ring = CreateSystem("AuraRing");
        ParticleSystem.MainModule ringMain = ring.main;
        ringMain.loop = true;
        ringMain.startLifetime = 0.9f;
        ringMain.startSpeed = 0f;
        ringMain.startSize = radius * 3.2f;
        ringMain.startColor = settings.RingColor;
        ringMain.maxParticles = 4;

        ParticleSystem.EmissionModule ringEmission = ring.emission;
        ringEmission.rateOverTime = 1.6f;

        ParticleSystem.ShapeModule ringShape = ring.shape;
        ringShape.enabled = false; // nasce sempre al centro, sotto i piedi

        SetFade(ring, 0.15f, 0.5f);
        ParticleSystem.SizeOverLifetimeModule ringSize = ring.sizeOverLifetime;
        ringSize.enabled = true;
        ringSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.45f), new Keyframe(1f, 1f)));

        ParticleSystemRenderer ringRenderer = ring.GetComponent<ParticleSystemRenderer>();
        ringRenderer.sharedMaterial = GetMaterial(ref ringMaterial, RingTexture());
        ringRenderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
    }

    // Riflessi: lampi piccoli e brevissimi sparsi nel volume del corpo, che si gonfiano e si
    // spengono di colpo. Stesso material dei bagliori: il colore quasi bianco fa il lucido.
    private void BuildGlints(StyleSettings settings, float radius, float bodyRadius, float height)
    {
        glints = CreateSystem("AuraGlints");
        ParticleSystem.MainModule main = glints.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(bodyRadius * 0.25f, bodyRadius * 0.5f);
        main.startColor = settings.GlintColor;
        main.maxParticles = 40;

        ParticleSystem.EmissionModule emission = glints.emission;
        emission.rateOverTime = settings.GlintRate;

        // Un cilindro grosso quanto il corpo: la forma Box è comoda, centrata a metà altezza.
        ParticleSystem.ShapeModule shape = glints.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(radius * 1.6f, height * 0.9f, radius * 1.6f);

        ParticleSystem.SizeOverLifetimeModule size = glints.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0f)));

        ParticleSystemRenderer glintRenderer = glints.GetComponent<ParticleSystemRenderer>();
        glintRenderer.sharedMaterial = GetMaterial(ref glowMaterial, SoftGlowTexture());
        glintRenderer.renderMode = ParticleSystemRenderMode.Billboard;
    }

    private ParticleSystem CreateSystem(string name)
    {
        GameObject systemObject = new GameObject(name);
        systemObject.transform.SetParent(transform, false);

        ParticleSystem system = systemObject.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = system.main;
        main.playOnAwake = false;
        // In world: chi cammina si lascia dietro una scia invece di trascinarsi l'aura. Gli stili
        // FollowBody no: restano attaccati al corpo anche a tutta velocità.
        main.simulationSpace = followBody ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;

        ParticleSystemRenderer systemRenderer = system.GetComponent<ParticleSystemRenderer>();
        systemRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        systemRenderer.receiveShadows = false;
        return system;
    }

    // Entra in fadeIn e svanisce da fadeOut in poi (frazioni della vita della particella).
    private static void SetFade(ParticleSystem system, float fadeIn, float fadeOut)
    {
        ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            alphaKeys = new[]
            {
                new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn),
                new GradientAlphaKey(1f, fadeOut), new GradientAlphaKey(0f, 1f),
            },
        });
    }

    // L'aura nasce ai piedi del modello, che sta sulla sabbia (non sul fondo della capsula).
    private void PlaceAtFeet()
    {
        Bounds bounds = MeasureBounds();
        Vector3 feet = new Vector3(transform.position.x, bounds.min.y + 0.05f, transform.position.z);
        glow.transform.position = feet;
        if (ring != null)
        {
            ring.transform.position = feet + Vector3.up * 0.02f;
        }
        if (glints != null)
        {
            glints.transform.position = feet + Vector3.up * (bodyHeight * 0.5f);
        }
    }

    // Ingombro dei Renderer visibili del personaggio (il modello animato; una capsula spenta no).
    private Bounds MeasureBounds()
    {
        bool found = false;
        Bounds bounds = new Bounds(transform.position, Vector3.one);
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer))
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

    private static Material GetMaterial(ref Material material, Texture2D texture)
    {
        if (material == null)
        {
            material = new Material(MaterialLibrary.SpriteDefault) { mainTexture = texture };
        }
        return material;
    }

    // Disco che sfuma dal centro al bordo, senza contorno: un alone.
    private static Texture2D SoftGlowTexture()
    {
        return DrawTexture((distance) => Mathf.Pow(Mathf.Clamp01(1f - distance), 2f));
    }

    // Anello sottile e morbido vicino al bordo, vuoto al centro.
    private static Texture2D RingTexture()
    {
        return DrawTexture((distance) => Mathf.Clamp01(1f - Mathf.Abs(distance - 0.8f) / 0.12f));
    }

    private static Texture2D DrawTexture(System.Func<float, float> alphaAtDistance)
    {
        const int size = 64;
        const float half = size * 0.5f;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / half - 1f;
                float v = (y + 0.5f) / half - 1f;
                float distance = Mathf.Sqrt(u * u + v * v); // 0 al centro, 1 sul bordo
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alphaAtDistance(distance)));
            }
        }
        texture.Apply();
        return texture;
    }
}
