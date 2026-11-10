using UnityEngine;

// Da mettere sul prefab di un'ammo, accanto a CollectibleShine: sbuffi di gas colorato che
// nascono attorno al modello, salgono piano dondolando, si allargano e sfumano. Niente bordi:
// ogni sbuffo è una macchia morbida (SoftParticleTexture), e sovrapponendosi fanno una nuvola.
// Come AmmoPowderSwirl è un ParticleSystem costruito a runtime, sotto un oggetto che segue il
// figlio "Modello" (vedi AmmoModel) ma non gira con la radice.
public class AmmoBubbles : MonoBehaviour
{
    [SerializeField] private Color bubbleColor = new Color(1f, 1f, 1f, 0.45f);
    [Tooltip("Sbuffi nuovi al secondo.")]
    [SerializeField] private float rate = 14f;
    [Tooltip("Raggio della sfera su cui nascono gli sbuffi (unità).")]
    [SerializeField] private float radius = 0.45f;
    [Tooltip("Dimensione degli sbuffi (min, max).")]
    [SerializeField] private Vector2 size = new Vector2(0.3f, 0.6f);
    [Tooltip("Secondi di vita di uno sbuffo (min, max).")]
    [SerializeField] private Vector2 lifetime = new Vector2(1.8f, 3f);
    [Tooltip("Velocità con cui salgono (unità al secondo).")]
    [SerializeField] private float riseSpeed = 0.15f;
    [Tooltip("Velocità con cui girano attorno al modello (radianti al secondo).")]
    [SerializeField] private float orbitSpeed = 0.5f;
    [Tooltip("Quanto dondolano salendo.")]
    [SerializeField] private float wobbleStrength = 0.2f;

    private Transform holder;
    private Transform bubbleRoot;
    private Material material;

    void Start()
    {
        holder = AmmoModel.FindHolder(transform);
        AmmoModel.Center(holder);

        bubbleRoot = new GameObject("Gas").transform;
        bubbleRoot.SetParent(transform, false);
        CreateParticles(bubbleRoot.gameObject);
    }

    void LateUpdate()
    {
        if (bubbleRoot == null)
        {
            return;
        }

        bubbleRoot.position = holder.position;
        bubbleRoot.rotation = Quaternion.identity; // non gira con la radice
    }

    private void CreateParticles(GameObject owner)
    {
        ParticleSystem particles = owner.AddComponent<ParticleSystem>();
        // Fermato subito: AddComponent lo fa partire e da acceso non si può cambiare la durata.
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.loop = true;
        main.duration = 5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startColor = bubbleColor;
        main.gravityModifier = 0f;
        main.maxParticles = 150;
        // Girati a caso e lenti a ruotare: le macchie sovrapposte non sembrano tutte uguali.
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        // Locale: il gas segue l'ammo mentre ondeggia su e giù (CollectibleShine).
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.prewarm = true;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = rate;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius;
        shape.radiusThickness = 0.3f;

        ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.y = riseSpeed;
        velocity.orbitalY = orbitSpeed;
        // Tutte le componenti con la stessa modalità (costante), vedi AmmoPowderSwirl.
        velocity.x = 0f;
        velocity.z = 0f;
        velocity.orbitalX = 0f;
        velocity.orbitalZ = 0f;
        velocity.radial = 0f;

        ParticleSystem.NoiseModule noise = particles.noise;
        noise.enabled = true;
        noise.strength = wobbleStrength;
        noise.frequency = 0.8f;
        noise.scrollSpeed = 0.4f;
        noise.damping = true;

        ParticleSystem.RotationOverLifetimeModule rotation = particles.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

        // Nascono raccolti e si allargano mentre salgono, come il gas che si disperde.
        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particles.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.4f, 1f, 1.5f));

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
        color.color = fade;

        ParticleSystemRenderer particleRenderer = owner.GetComponent<ParticleSystemRenderer>();
        // Additivo: il gas si somma alla luce della scena e i colori restano accesi anche sul buio.
        material = AmmoModel.CreateAdditiveMaterial(Color.white);
        material.mainTexture = SoftParticleTexture.Get();
        particleRenderer.material = material;
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        particleRenderer.receiveShadows = false;

        particles.Play();
    }

    void OnDestroy()
    {
        if (material != null)
        {
            Destroy(material);
        }
    }
}
