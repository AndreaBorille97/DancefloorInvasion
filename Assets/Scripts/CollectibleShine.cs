using UnityEngine;

// Da mettere sul prefab di un qualsiasi oggetto da raccogliere (tutte le ammo): gli dà il
// comportamento classico del collectible da videogioco — sta sollevato da terra, gira su sé
// stesso lentamente, ondeggia su e giù, ha qualche scintilla che gli svolazza intorno, e quando
// viene preso lascia uno sbuffo di stelline.
//
// Non serve che gli script di raccolta (SpeedUpPowerUpAmmoPickup, DrugPowerUpPickup, CamperPickup
// e compagnia) chiamino niente: la raccolta se la riconosce da solo. Ogni ammo si distrugge nel
// momento in cui il Player la tocca, quindi basta guardare quelle due cose insieme — toccato dal
// Player e distrutto nello stesso fotogramma (vedi OnDestroy). Un'ammo che scade senza essere
// raccolta, o la fine della partita, non fanno scattare niente.
//
// Tutto procedurale, niente prefab o immagini da collegare a mano: le scintille sono un
// ParticleSystem costruito a runtime (stesso approccio di EnemyHealth.SpawnDeathDust) con la
// stellina di StarParticleTexture.
//
// Lo sbuffo della raccolta è un oggetto a sé, staccato: il collectible si distrugge nel momento
// in cui viene preso, e le particelle figlie sparirebbero con lui prima di vedersi.
public class CollectibleShine : MonoBehaviour
{
    [Header("Movimento")]
    [Tooltip("Di quanto l'oggetto si stacca da terra rispetto a dove nasce. È il centro attorno a cui poi ondeggia.")]
    [SerializeField] private float hoverHeight = 0.45f;
    [Tooltip("Giri su sé stesso, in gradi al secondo: tienilo basso, deve essere una rotazione pigra da vetrina, non una trottola.")]
    [SerializeField] private float spinSpeed = 45f;
    [Tooltip("Quanto sale e scende rispetto alla quota di riposo (unità).")]
    [SerializeField] private float bobHeight = 0.18f;
    [Tooltip("Secondi per un'oscillazione completa su e giù: più alto = più lento e pesante.")]
    [SerializeField] private float bobPeriod = 2.4f;

    [Header("Scintille")]
    [Tooltip("Colore delle stelline, sia quelle che svolazzano intorno sia quelle della raccolta.")]
    [SerializeField] private Color sparkleColor = new Color(1f, 0.95f, 0.6f, 1f);
    [Tooltip("Quante scintille al secondo svolazzano intorno all'oggetto mentre aspetta di essere raccolto.")]
    [SerializeField] private float sparkleRate = 7f;
    [Tooltip("Raggio entro cui nascono le scintille attorno all'oggetto.")]
    [SerializeField] private float sparkleRadius = 0.55f;
    [Tooltip("Quante stelline sbottano tutte insieme quando l'oggetto viene raccolto.")]
    [SerializeField] private int collectSparkleCount = 18;

    private Vector3 restPosition; // quota di riposo: il centro dell'ondeggiamento
    private float bobTimer;
    private int touchedFrame = -1; // fotogramma in cui il Player ci è passato sopra, vedi OnDestroy

    void Start()
    {
        // Lo spawner fa nascere le munizioni appena sopra il pavimento: da lì le stacchiamo di
        // hoverHeight, invece di chiedere allo spawner di cambiare quota — così vale per
        // qualunque cosa faccia nascere questo prefab.
        restPosition = transform.position + Vector3.up * hoverHeight;
        transform.position = restPosition;

        CreateAmbientSparkles();
    }

    void Update()
    {
        transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);

        bobTimer += Time.deltaTime;
        float phase = bobPeriod > 0.01f ? bobTimer / bobPeriod * Mathf.PI * 2f : 0f;
        transform.position = restPosition + Vector3.up * (Mathf.Sin(phase) * bobHeight);
    }

    private void OnTriggerEnter(Collider other)
    {
        // Chi raccoglie davvero è lo script di pickup, qui interessa solo sapere che il Player
        // ci è passato sopra proprio adesso.
        if (other.CompareTag("Player"))
        {
            touchedFrame = Time.frameCount;
        }
    }

    private void OnDestroy()
    {
        // Distrutta nello stesso fotogramma in cui il Player l'ha toccata: è stata raccolta, e si
        // festeggia. Distrutta in un altro momento vuol dire che è semplicemente scaduta (vedi il
        // lifespan degli script di pickup) o che si sta uscendo dalla partita: niente stelline.
        // Destroy() agisce a fine fotogramma, quindi i due numeri coincidono davvero.
        if (touchedFrame == Time.frameCount)
        {
            PlayCollectBurst();
        }
    }

    // Lo sbuffo della raccolta. Pubblico perché possa lanciarlo anche qualcun altro, ma di norma
    // ci pensa OnDestroy qui sopra.
    public void PlayCollectBurst()
    {
        GameObject burstObject = new GameObject("CollectBurst");
        burstObject.transform.position = transform.position;

        ParticleSystem particles = burstObject.AddComponent<ParticleSystem>();

        // AddComponent lo fa partire subito (Play On Awake di default): senza fermarlo prima, il
        // main module risulterebbe "in play" e Unity rifiuterebbe di cambiargli la durata.
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        const float lifetime = 0.55f;

        ParticleSystem.MainModule main = particles.main;
        main.playOnAwake = true;
        main.loop = false;
        main.duration = lifetime;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.8f, 3.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
        main.startColor = sparkleColor;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = 1.2f; // le stelline schizzano su e ricadono: è quello che dà il "pop"
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, collectSparkleCount) });

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.15f;

        FadeOverLifetime(particles);
        SetUpStarRenderer(burstObject.GetComponent<ParticleSystemRenderer>());

        particles.Play();

        // Un margine oltre la durata: le ultime particelle nate a fine burst devono poter finire
        // la loro vita, altrimenti sparirebbero a metà volo.
        Destroy(burstObject, lifetime * 2f);
    }

    // Le scintille di attesa: figlie dell'oggetto e simulate nel suo spazio locale, così salgono
    // e scendono insieme a lui invece di restare sospese dove sono nate.
    private void CreateAmbientSparkles()
    {
        GameObject sparkleObject = new GameObject("Sparkles");
        sparkleObject.transform.SetParent(transform, false);

        ParticleSystem particles = sparkleObject.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.playOnAwake = true;
        main.loop = true;
        main.duration = 2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.35f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.22f);
        main.startColor = sparkleColor;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = sparkleRate;

        // Le scintille nascono sul guscio della sfera, non dentro: dentro finirebbero per lo più
        // nascoste dal modello, mentre così si vedono staccate tutt'intorno.
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = sparkleRadius;
        shape.radiusThickness = 0f;

        // Deriva verso l'alto: le scintille salgono piano come faville, non restano ferme dove nascono.
        ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.y = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);

        FadeOverLifetime(particles);
        SetUpStarRenderer(sparkleObject.GetComponent<ParticleSystemRenderer>());

        particles.Play();
    }

    // Nascono e muoiono sfumando invece di apparire e sparire di colpo: è quello che le fa
    // leggere come scintille e non come puntini accesi.
    private static void FadeOverLifetime(ParticleSystem particles)
    {
        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f),
            },
            alphaKeys = new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.25f),
                new GradientAlphaKey(0f, 1f),
            },
        });

        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.3f),
            new Keyframe(0.3f, 1f),
            new Keyframe(1f, 0.2f)));
    }

    private static void SetUpStarRenderer(ParticleSystemRenderer particleRenderer)
    {
        // Sprites/Default come gli altri effetti del progetto (vedi EnemyHealth): è uno shader
        // che c'è di sicuro anche in URP, a differenza di quelli "Particles/..." del vecchio
        // pipeline, che qui risulterebbero mancanti e darebbero particelle rosa shocking.
        particleRenderer.material = new Material(Shader.Find("Sprites/Default"));
        particleRenderer.material.mainTexture = StarParticleTexture.Get();
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        particleRenderer.receiveShadows = false;
    }
}
