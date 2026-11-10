using UnityEngine;

// Da mettere sul prefab di ogni ammo: quando il Player la raccoglie esplode una raffica di
// stelle gialle a cinque punte, come in Super Mario quando si prende la stella. Le stelle
// grandi schizzano fuori ad anello girando su sé stesse e lampeggiando, quelle piccole salgono
// e ricadono come coriandoli.
//
// Come CollectibleShine (che resta per le scintille del colore dell'ammo) la raccolta se la
// riconosce da solo: toccata dal Player e distrutta nello stesso fotogramma. Un'ammo che scade
// senza essere presa non fa partire niente.
//
// La raffica è un oggetto a sé, staccato: l'ammo si distrugge nel momento in cui viene presa.
public class CollectStarBurst : MonoBehaviour
{
    [ColorUsage(false, true)]
    [SerializeField] private Color starColor = new Color(1.4f, 1.15f, 0.15f, 1f);
    [Tooltip("Stelle grandi che schizzano fuori ad anello.")]
    [SerializeField] private int bigStars = 12;
    [Tooltip("Stelline piccole che salgono e ricadono.")]
    [SerializeField] private int smallStars = 24;
    [SerializeField] private Vector2 bigStarSize = new Vector2(0.14f, 0.24f);
    [SerializeField] private Vector2 smallStarSize = new Vector2(0.05f, 0.1f);

    private int touchedFrame = -1;
    private static Texture2D starTexture;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            touchedFrame = Time.frameCount;
        }
    }

    private void OnDestroy()
    {
        // Destroy() agisce a fine fotogramma: se coincide col tocco del Player, è stata raccolta.
        if (touchedFrame == Time.frameCount)
        {
            Play(transform.position);
        }
    }

    public void Play(Vector3 position)
    {
        GameObject burst = new GameObject("CollectStars");
        burst.transform.position = position;

        CreateBigStars(burst.transform);
        CreateSmallStars(burst.transform);

        Destroy(burst, 2f);
    }

    // Anello orizzontale di stelle grandi, un po' verso l'alto: escono veloci, frenano, girano
    // su sé stesse e lampeggiano prima di sparire.
    private void CreateBigStars(Transform parent)
    {
        ParticleSystem particles = CreateSystem(parent, "BigStars");

        ParticleSystem.MainModule main = particles.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 6f);
        main.startSize = new ParticleSystem.MinMaxCurve(bigStarSize.x, bigStarSize.y);
        main.gravityModifier = 0.25f;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)bigStars) });

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.2f;
        shape.arcMode = ParticleSystemShapeMultiModeValue.BurstSpread; // distribuite in modo uniforme sull'anello
        shape.rotation = new Vector3(-90f, 0f, 0f); // il cerchio sta nel piano XY: così diventa orizzontale

        // Una spinta verso l'alto uguale per tutte, così l'anello si apre salendo.
        ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = 0f;
        velocity.y = 1.8f;
        velocity.z = 0f;

        // Frenano subito: schizzano fuori e poi restano a galleggiare un attimo.
        ParticleSystem.LimitVelocityOverLifetimeModule limit = particles.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.drag = 4f;

        ParticleSystem.RotationOverLifetimeModule rotation = particles.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-6f, 6f);

        // Lampeggio: la grandezza pulsa più volte mentre si spengono.
        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.2f), new Keyframe(0.12f, 1.15f), new Keyframe(0.3f, 0.85f),
            new Keyframe(0.45f, 1.05f), new Keyframe(0.6f, 0.75f), new Keyframe(0.75f, 0.95f),
            new Keyframe(1f, 0f)));

        Fade(particles, 0.7f);
        particles.Play();
    }

    // Coriandoli di stelline: salgono a fontana e ricadono.
    private void CreateSmallStars(Transform parent)
    {
        ParticleSystem particles = CreateSystem(parent, "SmallStars");

        ParticleSystem.MainModule main = particles.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 5.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(smallStarSize.x, smallStarSize.y);
        main.gravityModifier = 1.4f;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)smallStars) });

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 40f;
        shape.radius = 0.1f;
        shape.rotation = new Vector3(-90f, 0f, 0f); // cono rivolto verso l'alto

        ParticleSystem.RotationOverLifetimeModule rotation = particles.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-10f, 10f);

        Fade(particles, 0.6f);
        particles.Play();
    }

    private ParticleSystem CreateSystem(Transform parent, string name)
    {
        GameObject owner = new GameObject(name);
        owner.transform.SetParent(parent, false);
        ParticleSystem particles = owner.AddComponent<ParticleSystem>();
        // Fermato subito: AddComponent lo fa partire e da acceso non si può cambiare la durata.
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.loop = false;
        main.duration = 0.2f;
        main.startColor = starColor;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 100;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;

        ParticleSystemRenderer particleRenderer = owner.GetComponent<ParticleSystemRenderer>();
        // Sprites/Default come CollectibleShine: c'è di sicuro anche in URP.
        particleRenderer.material = new Material(Shader.Find("Sprites/Default"));
        particleRenderer.material.mainTexture = GetStarTexture();
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        particleRenderer.receiveShadows = false;
        // Il materiale è suo: va via con l'oggetto (vedi Destroy in Play).
        Destroy(particleRenderer.material, 2f);
        return particles;
    }

    // Piene fino a fadeStart della vita, poi svaniscono; partono con un lampo bianco.
    private static void Fade(ParticleSystem particles, float fadeStart)
    {
        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, fadeStart), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
    }

    // Stella a cinque punte bianca (il colore lo dà la particella), con il bordo sfumato e un
    // alone tenue attorno: disegnata una volta sola e condivisa.
    private static Texture2D GetStarTexture()
    {
        if (starTexture != null)
        {
            return starTexture;
        }

        const int N = 128;
        const float outer = 0.92f, inner = 0.4f;
        starTexture = new Texture2D(N, N, TextureFormat.RGBA32, false)
        {
            name = "CollectStar",
            wrapMode = TextureWrapMode.Clamp,
        };

        Color[] pixels = new Color[N * N];
        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                // Coordinate da -1 a 1, punta in alto.
                float px = (x + 0.5f) / N * 2f - 1f;
                float py = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(px * px + py * py);
                float angle = Mathf.Atan2(px, py); // 0 = in alto

                // Raggio del bordo della stella in questa direzione: interpolato fra punta
                // (outer) e incavo (inner) lungo ciascuno dei dieci spicchi.
                float sector = Mathf.PI / 5f;
                float t = Mathf.Abs(Mathf.Repeat(angle + sector, 2f * sector) - sector) / sector; // 0 = punta, 1 = incavo
                float tipX = 0f, tipY = outer;
                float notchX = Mathf.Sin(sector) * inner, notchY = Mathf.Cos(sector) * inner;
                float edge = EdgeRadius(t, tipX, tipY, notchX, notchY, sector);

                float star = Mathf.Clamp01((edge - r) * N * 0.5f); // bordo netto ma antialias
                float glow = Mathf.Clamp01(1f - r) * 0.35f;         // alone tenue
                float a = Mathf.Max(star, glow * (1f - star));
                pixels[y * N + x] = new Color(1f, 1f, 1f, a);
            }
        }

        starTexture.SetPixels(pixels);
        starTexture.Apply();
        return starTexture;
    }

    // Distanza dal centro del lato dritto fra punta e incavo, alla frazione t dello spicchio.
    private static float EdgeRadius(float t, float tipX, float tipY, float notchX, float notchY, float sector)
    {
        float a = t * sector; // angolo dal raggio della punta
        Vector2 dir = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
        Vector2 p0 = new Vector2(tipX, tipY), p1 = new Vector2(notchX, notchY);
        // Intersezione fra il raggio dal centro e il segmento p0-p1.
        Vector2 d = p1 - p0;
        float denom = dir.x * d.y - dir.y * d.x;
        if (Mathf.Abs(denom) < 1e-6f)
        {
            return p0.magnitude;
        }
        float s = (p0.x * d.y - p0.y * d.x) / denom;
        return s;
    }
}
