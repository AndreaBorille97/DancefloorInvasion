using UnityEngine;

// Da mettere sul prefab di un'ammo, accanto a CollectibleShine: polverine bianche che girano
// lente attorno al modello ondeggiando su e giù, come polvere sospesa nell'aria. È un
// ParticleSystem costruito a runtime (come le scintille di CollectibleShine): le polverine
// nascono su una sfera attorno al modello, ci girano intorno (orbital) e il rumore le fa
// ondeggiare, poi svaniscono piano.
//
// Le polverine stanno sotto un oggetto che segue il figlio "Modello" (vedi AmmoModel) ma non
// gira con la radice, che fa ruotare CollectibleShine: girano alla loro velocità.
public class AmmoPowderSwirl : MonoBehaviour
{
    [SerializeField] private Color powderColor = new Color(1f, 1f, 1f, 0.9f);
    [Tooltip("Polverine nuove al secondo.")]
    [SerializeField] private float rate = 30f;
    [Tooltip("Raggio della sfera su cui nascono le polverine (unità).")]
    [SerializeField] private float radius = 0.5f;
    [Tooltip("Dimensione delle polverine (min, max).")]
    [SerializeField] private Vector2 size = new Vector2(0.04f, 0.1f);
    [Tooltip("Secondi di vita di una polverina (min, max).")]
    [SerializeField] private Vector2 lifetime = new Vector2(1.8f, 3f);
    [Tooltip("Velocità con cui girano attorno al modello (radianti al secondo).")]
    [SerializeField] private float orbitSpeed = 1.4f;
    [Tooltip("Quanto ondeggiano su e giù.")]
    [SerializeField] private float swayStrength = 0.35f;
    [Tooltip("Quanto è lento l'ondeggiamento: più basso = onde più larghe e morbide.")]
    [SerializeField] private float swayFrequency = 0.5f;

    private Transform holder;
    private Transform powderRoot;

    void Start()
    {
        holder = AmmoModel.FindHolder(transform);
        AmmoModel.Center(holder);

        powderRoot = new GameObject("Polverine").transform;
        powderRoot.SetParent(transform, false);
        CreateParticles(powderRoot.gameObject);
    }

    void LateUpdate()
    {
        if (powderRoot == null)
        {
            return;
        }

        powderRoot.position = holder.position;
        powderRoot.rotation = Quaternion.identity; // non gira con la radice
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
        main.startColor = powderColor;
        main.gravityModifier = 0f;
        main.maxParticles = 300;
        // Locale: le polverine seguono l'ammo mentre ondeggia su e giù (CollectibleShine).
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.prewarm = true; // già sparse attorno al modello appena compare l'ammo

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = rate;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius;
        shape.radiusThickness = 0.3f; // nascono verso la superficie della sfera, non in mezzo al modello

        ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.orbitalY = orbitSpeed;
        // Le altre componenti vanno messe esplicitamente con la stessa modalità (costante),
        // altrimenti Unity si lamenta che le curve del modulo non sono tutte dello stesso tipo.
        velocity.x = 0f;
        velocity.y = 0f;
        velocity.z = 0f;
        velocity.orbitalX = 0f;
        velocity.orbitalZ = 0f;
        velocity.radial = 0f;

        ParticleSystem.NoiseModule noise = particles.noise;
        noise.enabled = true;
        noise.strength = swayStrength;
        noise.frequency = swayFrequency;
        noise.scrollSpeed = 0.3f;
        noise.damping = true;

        // Compaiono e svaniscono piano, mai di colpo.
        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        color.color = fade;

        ParticleSystemRenderer particleRenderer = owner.GetComponent<ParticleSystemRenderer>();
        particleRenderer.material = new Material(Shader.Find("Sprites/Default"));
        particleRenderer.material.mainTexture = SoftParticleTexture.Get();
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        particleRenderer.receiveShadows = false;

        particles.Play();
    }

    void OnDestroy()
    {
        if (powderRoot != null)
        {
            ParticleSystemRenderer particleRenderer = powderRoot.GetComponent<ParticleSystemRenderer>();
            if (particleRenderer != null)
            {
                Destroy(particleRenderer.material);
            }
        }
    }
}
