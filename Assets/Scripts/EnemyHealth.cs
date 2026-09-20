using UnityEngine;

// Da mettere sul prefab del nemico, insieme a EnemyChase.
// Tiene i punti vita e distrugge il nemico quando arrivano a zero.
public class EnemyHealth : MonoBehaviour, IDamageable
{
    [SerializeField] private float maxHealth = 100f; // punti vita massimi del nemico
    [Tooltip("Danno tolto da un colpo di Camper: il quadruplo di un colpo di Raver normale (RaverAttack.damage = 25), non più un instant kill.")]
    [SerializeField] private float camperDamage = 100f;

    [Header("Morte")]
    [Tooltip("Quante particelle rosse compongono la polvere alla morte. Non si applica all'IdroSbirro (troppo grosso/fisso per svanire in polvere, vedi Die).")]
    [SerializeField] private int deathDustParticleCount = 24;
    [SerializeField] private Color deathDustColor = new Color(0.6f, 0.05f, 0.05f, 1f);
    [Tooltip("Velocità con cui le particelle di polvere si allontanano dal punto della morte. Deve restare in scala con le altre velocità di gioco (Player.moveSpeed = 6) mentre resta abbastanza alta da coprire una distanza ben visibile entro deathDustLifetime: con valori enormi (es. 2000) la particella percorre l'intero schermo in una frazione di frame e sparisce prima ancora di essere renderizzata, quindi risulta invisibile invece che 'veloce'; con valori troppo bassi il tragitto totale resta minuscolo e l'effetto sembra fiacco anche con lo stretch. Il rendering \"stretched\" (vedi SpawnDeathDust) esagera comunque la sensazione di rapidità allungando la particella in base a questa velocità.")]
    [SerializeField] private float deathDustSpeed = 60f;
    [SerializeField] private float deathDustSize = 0.2f;
    [Tooltip("Breve apposta: l'effetto deve leggersi come un lampo/colpo di fucile a pompa (appare e sparisce di scatto), non come una nuvola di detriti che si dirada lentamente.")]
    [SerializeField] private float deathDustLifetime = 0.2f;
    [Tooltip("Apertura del cono di sparo della polvere, centrato sulla direzione opposta a quella da cui è arrivato il colpo fatale.")]
    [SerializeField] private float deathDustSpreadAngle = 18f;

    [Header("Pezzi che schizzano via")]
    [Tooltip("Quanto è grande un occhio, in unità di mondo: piccolo apposta (lo Sbirro è alto circa 2), deve leggersi come un pezzo staccato, non come una palla.")]
    [SerializeField] private float deathEyeSize = 0.32f;
    [Tooltip("Quanto è grande l'ossicino. Un filo più dell'occhio: è lungo e stretto, quindi a parità di misura si vede meno.")]
    [SerializeField] private float deathBoneSize = 0.5f;
    [Tooltip("Quanto durano occhi e ossicino. Volutamente un filo più della polvere (deathDustLifetime): un occhio ha bisogno di qualche fotogramma in più per essere riconosciuto come tale, e sotto quella soglia resta solo un lampo bianco. Mettilo uguale a deathDustLifetime se li vuoi sparire esattamente insieme alla polvere.")]
    [SerializeField] private float deathDebrisLifetime = 0.35f;

    // Quanto freno prendono le particelle dopo lo scatto iniziale. Lo usano sia la polvere sia i
    // pezzi: è metà del motivo per cui percorrono la stessa traiettoria (l'altra metà è la
    // velocità di partenza, deathDustSpeed, che è la stessa per tutti).
    private const float DeathDrag = 6f;

    private float currentHealth;

    void Awake()
    {
        currentHealth = maxHealth;

        // Spawnati fuori dal perimetro (vedi EnemySpawner piazzati oltre i muri), i nemici
        // devono poter entrare camminandoci attraverso invece di comparire dentro dal nulla.
        ArenaBounds.IgnoreCollisionsForEnemy(gameObject);
    }

    public void TakeDamage(float amount, Vector3 sourcePosition)
    {
        currentHealth -= amount;
        if (currentHealth <= 0f)
        {
            Die(sourcePosition);
        }
    }

    // Chiamato da CamperVehicle al posto dell'insta-kill: stesso schema di RobosbirroHealth.TakeCamperHit().
    public void TakeCamperHit(Vector3 sourcePosition)
    {
        TakeDamage(camperDamage, sourcePosition);
    }

    // L'IdroSbirro usa anche lui EnemyHealth (per essere danneggiabile come gli altri Sbirro,
    // vedi IdroSbirroHoseAttack) ma non deve sparire in polvere: troppo grosso e fisso per
    // avere senso visivamente, resta semplicemente distrutto come prima di questa feature.
    private void Die(Vector3 sourcePosition)
    {
        if (GetComponent<IdroSbirroHoseAttack>() == null)
        {
            Vector3 blastDirection = MeasureBlastDirection(sourcePosition);
            SpawnDeathDust(blastDirection);

            // Gli occhi partono dalla testa, l'ossicino dal centro del corpo: è da lì che
            // verrebbe un osso, e partendo tutti dallo stesso punto sembrerebbero un mazzo solo.
            SpawnDeathDebris("SbirroDeathEyes", EyeTexture.Get(), 2, deathEyeSize, MeasureHeadPoint(), blastDirection);
            SpawnDeathDebris("SbirroDeathBone", BoneTexture.Get(), 1, deathBoneSize, MeasureBodyPoint(), blastDirection);
        }

        Destroy(gameObject);
    }

    // Da che parte vola via tutto quanto: dalla parte opposta a chi ha sparato il colpo fatale,
    // in orizzontale. Se il colpo è arrivato esattamente dal punto in cui si trova il nemico
    // (non dovrebbe, ma è meglio non dividere per zero) si sceglie una direzione a caso.
    private Vector3 MeasureBlastDirection(Vector3 sourcePosition)
    {
        Vector3 direction = transform.position - sourcePosition;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = Random.insideUnitSphere;
            direction.y = 0f;
        }

        return direction.normalized;
    }

    // Un GameObject a sé con un ParticleSystem creato a runtime (nessun prefab da collegare
    // a mano, stesso approccio di IdroSbirroWaterball per il fascio d'acqua): la polvere vola
    // via dal punto della morte lungo direction (vedi MeasureBlastDirection: la direzione
    // opposta a quella da cui è arrivato il colpo fatale), non un'esplosione uniforme in
    // tutte le direzioni.
    private void SpawnDeathDust(Vector3 direction)
    {
        GameObject dustObject = new GameObject("SbirroDeathDust");
        dustObject.transform.position = transform.position;
        dustObject.transform.rotation = Quaternion.LookRotation(direction);

        ParticleSystem particles = dustObject.AddComponent<ParticleSystem>();

        // AddComponent lo fa partire subito (Play On Awake di default): senza fermarlo prima,
        // il main module sotto risulterebbe "in play" e Unity rifiuterebbe di cambiargli la
        // durata ("Setting the duration while system is still playing is not supported").
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = deathDustLifetime;
        main.startLifetime = deathDustLifetime;
        main.startSpeed = deathDustSpeed;
        main.startSize = deathDustSize;
        main.startColor = deathDustColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, deathDustParticleCount) });

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = deathDustSpreadAngle;

        // A velocità costante per tutta la vita, l'occhio legge solo "quanto lontano è
        // arrivata" (dispersione), non "quanto velocemente ci è arrivata": la sensazione di
        // rapidità viene dalla decelerazione brusca dopo lo scatto iniziale, non dal moto
        // lineare uniforme. Drag alto = le particelle partono a deathDustSpeed e frenano
        // rapidamente, come schegge/pallini di un colpo di fucile che perdono energia di
        // scatto invece di scivolare via a velocità costante. limit = deathDustSpeed evita
        // che il clamp del modulo intervenga: è il drag da solo a curvare la decelerazione.
        ParticleSystem.LimitVelocityOverLifetimeModule limitVelocity = particles.limitVelocityOverLifetime;
        limitVelocity.enabled = true;
        limitVelocity.drag = DeathDrag;
        limitVelocity.limit = deathDustSpeed;

        ParticleSystemRenderer particleRenderer = dustObject.GetComponent<ParticleSystemRenderer>();
        particleRenderer.material = new Material(Shader.Find("Sprites/Default"));
        particleRenderer.material.mainTexture = RoundParticleTexture.Get();
        particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Stretched Billboard invece del default (che disegna un punto pieno, sempre della
        // stessa forma qualunque sia la velocità): qui ogni particella si allunga in una scia
        // proporzionale alla propria velocità, il modo in cui l'occhio percepisce "veloce".
        // velocityScale*deathDustSpeed dà la lunghezza della scia in world unit (qui ~3): deve
        // essere una frazione ben visibile del tragitto totale (deathDustSpeed*deathDustLifetime,
        // qui ~12), non una scheggia minuscola (troppo piccola per leggersi come "veloce", solo
        // "presente") né la quasi totalità del tragitto (l'intero effetto sembra un unico quad
        // già disteso al primo frame invece di una scia che insegue un punto in movimento).
        particleRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        particleRenderer.velocityScale = 0.05f;
        particleRenderer.lengthScale = 2.5f;

        particles.Play();
        Destroy(dustObject, deathDustLifetime + 0.1f);
    }

    // I pezzi che saltano via insieme alla polvere: i due occhi (EyeTexture) e l'ossicino
    // (BoneTexture). Sono particelle e basta, disegnate in billboard, quindi guardano sempre la
    // camera — che per un occhio è tutto quello che serve, perché la pupilla si legge sempre.
    //
    // Viaggiano sulla stessa traiettoria della polvere rossa, e non per somiglianza ma perché
    // condividono i tre numeri che la definiscono: partono a deathDustSpeed, frenano con lo
    // stesso DeathDrag, e si aprono dentro lo stesso cono di deathDustSpreadAngle gradi. Niente
    // gravità, come la polvere: in due decimi di secondo una caduta non si vedrebbe comunque, e
    // basterebbe a staccarli dal gruppo.
    //
    // Le particelle le spariamo a mano con Emit invece di lasciarle al cono di emissione: un
    // cono le tira a caso, e due occhi partiti nella stessa identica direzione sembrano uno
    // solo. Qui vengono aperti a ventaglio, uno per lato.
    private void SpawnDeathDebris(string name, Texture2D texture, int count, float size, Vector3 origin, Vector3 direction)
    {
        GameObject debrisObject = new GameObject(name);
        debrisObject.transform.position = origin;

        ParticleSystem particles = debrisObject.AddComponent<ParticleSystem>();

        // Stesso motivo di SpawnDeathDust: AddComponent lo fa partire subito, e da "in play"
        // Unity non lascia cambiare la durata.
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = deathDebrisLifetime;
        main.startLifetime = deathDebrisLifetime;
        main.startSpeed = 0f; // la velocità gliela diamo pezzo per pezzo, vedi Emit più sotto
        main.startSize = size;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f); // non partono uguali
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = count;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = false; // nessuna emissione automatica: i pezzi li mettiamo noi

        // Lo stesso freno della polvere: è questo, insieme alla velocità di partenza, a dare la
        // curva di decelerazione: scatto secco e poi stop, invece di scivolare via lineari.
        ParticleSystem.LimitVelocityOverLifetimeModule limitVelocity = particles.limitVelocityOverLifetime;
        limitVelocity.enabled = true;
        limitVelocity.drag = DeathDrag;
        limitVelocity.limit = deathDustSpeed;

        // Girano su se stessi mentre volano: un pezzo che rotola via invece di traslare.
        ParticleSystem.RotationOverLifetimeModule rotation = particles.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-7f, 7f); // radianti al secondo, verso a caso

        // Spariscono in dissolvenza sull'ultimo tratto, così non svaniscono di colpo a mezz'aria.
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
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.65f),
                new GradientAlphaKey(0f, 1f),
            },
        });

        ParticleSystemRenderer particleRenderer = debrisObject.GetComponent<ParticleSystemRenderer>();
        particleRenderer.material = new Material(Shader.Find("Sprites/Default"));
        particleRenderer.material.mainTexture = texture;
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        particles.Play();

        Vector3 side = Vector3.Cross(Vector3.up, direction);
        for (int i = 0; i < count; i++)
        {
            // Da -1 a +1 lungo il ventaglio: con un pezzo solo resta a 0, cioè dritto in mezzo
            // al cono, esattamente dove va il grosso della polvere.
            float fan = count > 1 ? Mathf.Lerp(-1f, 1f, (float)i / (count - 1)) : 0f;
            Quaternion spread = Quaternion.AngleAxis(fan * deathDustSpreadAngle, Vector3.up)
                                * Quaternion.AngleAxis(Random.Range(-0.4f, 0.4f) * deathDustSpreadAngle, side);

            particles.Emit(
                new ParticleSystem.EmitParams
                {
                    position = origin + side * (fan * size * 0.6f),
                    velocity = spread * direction * deathDustSpeed,
                    startLifetime = deathDebrisLifetime,
                    startSize = size,
                    startColor = Color.white,
                },
                1);
        }

        Destroy(debrisObject, deathDebrisLifetime + 0.1f);
    }

    // Il centro del corpo del nemico, da dove parte l'ossicino.
    private Vector3 MeasureBodyPoint()
    {
        return TryMeasureModel(out Bounds bounds) ? bounds.center : transform.position;
    }

    // Dove sta la testa del nemico: il pivot degli Sbirro è ai piedi, quindi gli occhi
    // partirebbero da terra. La ricaviamo dall'ingombro del modello invece che da un'altezza
    // scritta a mano, così vale per qualunque nemico e per qualunque taglia.
    private Vector3 MeasureHeadPoint()
    {
        if (!TryMeasureModel(out Bounds bounds))
        {
            return transform.position;
        }

        // Poco sotto la cima: la testa, non la punta del cappello.
        return new Vector3(bounds.center.x, Mathf.Lerp(bounds.center.y, bounds.max.y, 0.75f), bounds.center.z);
    }

    // Ingombro del modello del nemico, in coordinate mondo.
    private bool TryMeasureModel(out Bounds bounds)
    {
        bounds = new Bounds(transform.position, Vector3.zero);
        bool found = false;

        foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
        {
            // Un renderer senza geometria ha un ingombro nullo piantato sul pivot: contarlo
            // tirerebbe la misura giù verso i piedi.
            if (renderer.localBounds.size.sqrMagnitude <= 0.000001f)
            {
                continue;
            }

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
                continue;
            }

            bounds.Encapsulate(renderer.bounds);
        }

        return found;
    }
}
