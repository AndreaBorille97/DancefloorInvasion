using UnityEngine;

// Da mettere sul prefab del nemico, insieme a EnemyChase.
// Tiene i punti vita e distrugge il nemico quando arrivano a zero.
public class EnemyHealth : MonoBehaviour, IDamageable
{
    [SerializeField] private float maxHealth = 100f; // punti vita massimi del nemico
    [Tooltip("Quanta parte della vita massima toglie un colpo di Camper: 1 = una passata uccide, qualunque vita abbia lo Sbirro. È una frazione e non un numero di punti fisso apposta, vedi TakeCamperHit.")]
    [Range(0f, 1f)]
    [SerializeField] private float camperDamagePercent = 1f;

    [Header("Morte")]
    [Tooltip("Quante particelle rosse compongono la polvere alla morte. Non si applica all'IdroSbirro (troppo grosso/fisso per svanire in polvere, vedi Die).")]
    [SerializeField] private int deathDustParticleCount = 60;
    [SerializeField] private Color deathDustColor = new Color(0.6f, 0.05f, 0.05f, 1f);
    [Tooltip("Velocità con cui le particelle di polvere si allontanano dal punto della morte. Deve restare in scala con le altre velocità di gioco (Player.moveSpeed = 6) mentre resta abbastanza alta da coprire una distanza ben visibile entro deathDustLifetime: con valori enormi (es. 2000) la particella percorre l'intero schermo in una frazione di frame e sparisce prima ancora di essere renderizzata, quindi risulta invisibile invece che 'veloce'; con valori troppo bassi il tragitto totale resta minuscolo e l'effetto sembra fiacco anche con lo stretch. Il rendering \"stretched\" (vedi SpawnDeathDust) esagera comunque la sensazione di rapidità allungando la particella in base a questa velocità.")]
    [SerializeField] private float deathDustSpeed = 60f;
    [SerializeField] private float deathDustSize = 0.2f;
    [Tooltip("Breve apposta: l'effetto deve leggersi come un lampo/colpo di fucile a pompa (appare e sparisce di scatto), non come una nuvola di detriti che si dirada lentamente.")]
    [SerializeField] private float deathDustLifetime = 0.2f;
    [Tooltip("Apertura del cono di sparo della polvere. Il cono è centrato sulla direzione opposta a quella da cui è arrivato il colpo fatale, tranne per le uccisioni di scatto (vedi TakeDashHit), dove punta invece verso il punto da cui lo scatto è partito.")]
    [SerializeField] private float deathDustSpreadAngle = 18f;

    [Header("Pezzi che schizzano via")]
    [Tooltip("Quanto è grande un occhio, in unità di mondo: piccolo apposta (lo Sbirro è alto circa 2), deve leggersi come un pezzo staccato, non come una palla.")]
    [SerializeField] private float deathEyeSize = 0.55f;
    [Tooltip("Quanto durano gli occhi. Volutamente un filo più della polvere (deathDustLifetime): un occhio ha bisogno di qualche fotogramma in più per essere riconosciuto come tale, e sotto quella soglia resta solo un lampo bianco. Mettilo uguale a deathDustLifetime se li vuoi sparire esattamente insieme alla polvere.")]
    [SerializeField] private float deathDebrisLifetime = 0.35f;
    [Tooltip("Quanta strada fa ogni occhio, rispetto a quella dei getti rossi (1 = arriva dove arrivano loro). Scelta a caso fra questi due valori, occhio per occhio: così i due non si fermano mai appaiati.")]
    [SerializeField] private Vector2 deathEyeReach = new Vector2(0.8f, 1.1f);

    // Quanto freno prende la polvere dopo lo scatto iniziale (gli occhi no, vedi SpawnDeathEyes): scatto secco
    // e poi stop, invece di scivolare via lineari.
    private const float DeathDrag = 6f;

    private float currentHealth;

    // Letti dall'IdroSbirro per capire quanto ha incassato e quando ritirarsi.
    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;

    // I boss (IdroSbirro, CyberGiorgia) si abbattono solo a colpi: niente uccisioni sul colpo
    // (scatto, Hoffman), niente conversione da serotonina, i colpi non li spostano.
    public bool IsBoss => GetComponent<IdroSbirro>() != null || GetComponent<CyberGiorgiaBoss>() != null;

    void Awake()
    {
        currentHealth = maxHealth;

        // Spawnati fuori dal perimetro (vedi EnemySpawner piazzati oltre i muri), i nemici
        // devono poter entrare camminandoci attraverso invece di comparire dentro dal nulla.
        ArenaBounds.IgnoreCollisionsForEnemy(gameObject);
    }

    // In Start e non in Awake: i segni nascono attorno al modello, che va misurato quando è già
    // al suo posto. Servono a riconoscere gli Sbirro nella ressa: lampeggiante sopra la testa e
    // cerchio nero a terra (SbirroMarker). SbirroGroundAlign appoggia i piedi del modello sul
    // pavimento, uguale per tutti i tipi, così il cerchio sta proprio sotto i piedi.
    void Start()
    {
        // CyberGiorgia no: arriva dal cielo (vedi CyberGiorgiaBoss), e la pianta dei piedi misurata
        // mentre cade verrebbe sballata. Il prefab ha già i piedi a terra, e la si riconosce dalla
        // barra della vita senza bisogno di lampeggiante e cerchio.
        if (GetComponent<CyberGiorgiaBoss>() != null)
        {
            return;
        }

        if (GetComponent<SbirroGroundAlign>() == null)
        {
            gameObject.AddComponent<SbirroGroundAlign>();
        }
        if (GetComponent<SbirroMarker>() == null)
        {
            gameObject.AddComponent<SbirroMarker>();
        }
    }

    public void TakeDamage(float amount, Vector3 sourcePosition)
    {
        currentHealth -= amount;
        if (currentHealth <= 0f)
        {
            // Colpito da lontano: lo schizzo vola via dalla parte opposta a chi ha sparato,
            // come se il colpo se lo portasse dietro.
            Die(MeasureBlastDirection(sourcePosition));
        }
    }

    // Chiamato dalle onde serotonina (SoundWaveProjectile.MakeSerotonin): stesso danno delle onde
    // normali, ma il colpo che lo ucciderebbe lo converte invece (vedi SerotoninBrawl): torna a
    // vita piena e passa dalla parte del Player. Chi non può essere convertito (IdroSbirro) non
    // subisce niente.
    public void TakeSerotoninDamage(float amount, Vector3 sourcePosition)
    {
        if (IsSwelling || !SerotoninBrawl.CanBrawl(this))
        {
            return;
        }

        if (currentHealth - amount > 0f)
        {
            TakeDamage(amount, sourcePosition);
            return;
        }

        currentHealth = maxHealth;
        SerotoninBrawl.Charm(this);
        HitEvents.RaiseEnemyConverted(gameObject);
    }

    // true se è passato dalla parte del Player (vedi SerotoninBrawl): le onde del Player e i
    // Raver lo lasciano stare.
    public bool IsCharmed => TryGetComponent(out SerotoninBrawl brawl) && brawl.IsCharmed;

    // Chiamato da PlayerMovement.MoveDashThroughEnemies quando il Player attraversa questo
    // Sbirro durante uno scatto: muore sul colpo, qualunque vita gli restasse.
    //
    // Qui lo schizzo va AL CONTRARIO rispetto a tutti gli altri modi di morire: torna indietro
    // lungo lo scatto, verso il punto da cui il Player è partito, invece di volare in avanti.
    // È quello che si vede quando si passa attraverso qualcosa invece di colpirla da lontano:
    // il corpo sta fermo ed è il Player a passarci dentro, quindi quello che schizza resta
    // indietro, nella scia. Sparato in avanti sembrerebbe che il Player se lo trascini appresso.
    //
    // Prende la direzione dello scatto e non il punto da cui è partito perché a questo punto il
    // Player è già arrivato addosso allo Sbirro: fra le due posizioni non c'è quasi differenza,
    // e MeasureBlastDirection ripiegherebbe su una direzione a caso. È esattamente quello che
    // succedeva prima, quando qui passava la posizione del Player: lo schizzo delle uccisioni
    // di scatto partiva per i fatti suoi, diverso a ogni morte.
    public void TakeDashHit(Vector3 dashDirection)
    {
        currentHealth = 0f;
        Die(FlattenOrRandom(-dashDirection));
    }

    // Chiamato dall'Hoffman (PlayerHoffman): muore sul colpo ed esplode in colori psichedelici
    // (PsychedelicExplosion) invece di schizzare via in polvere e occhi. I boss non esplodono:
    // si abbattono solo a colpi.
    public void HoffmanExplode()
    {
        if (currentHealth <= 0f || IsBoss || IsSwelling)
        {
            return;
        }

        // Prima si gonfia (HoffmanSwell), poi scoppia in bolle psichedeliche.
        currentHealth = 0f;
        IsSwelling = true;
        HoffmanSwell.Begin(gameObject, () =>
        {
            PsychedelicExplosion.Spawn(gameObject);
            HitEvents.RaiseEnemyKilled(gameObject);
            Destroy(gameObject);
        });
    }

    // true mentre si gonfia per l'Hoffman: è già morto, ogni altro colpo o morte va ignorato.
    public bool IsSwelling { get; private set; }

    // Chiamato da CamperVehicle quando il mezzo guidato tocca questo Sbirro: una passata lo
    // uccide. La morte passa però da TakeDamage come tutte le altre, quindi valgono la polvere,
    // gli occhi e l'ossicino, e l'IdroSbirro ne resta fuori perché è CamperVehicle a non
    // chiamarci nemmeno (vedi il suo OnTriggerEnter).
    //
    // Il danno è una frazione della vita massima e non un numero di punti fisso. Prima era 100,
    // il quadruplo di un colpo di Raver (RaverAttack.damage = 25), e bastava alzare la vita di
    // uno Sbirro perché il mezzo smettesse di ucciderlo senza che niente lo segnalasse: è
    // proprio quello che era successo a Sbirro2, 225 di vita, che veniva investito e se ne
    // andava con 125. E non era nemmeno il caso di contare su più colpi, perché il Camper
    // colpisce una volta sola per contatto (CamperVehicle.OnTriggerEnter, non c'è nessun
    // OnTriggerStay): uno Sbirro che resta sotto la carrozzeria non prende un secondo colpo.
    //
    // Il boss fa storia a sé e non usa questa classe: RobosbirroHealth.TakeCamperHit tiene
    // volutamente il danno fisso, altrimenti il Camper sarebbe troppo forte contro di lui.
    public void TakeCamperHit(Vector3 sourcePosition)
    {
        TakeDamage(maxHealth * camperDamagePercent, sourcePosition);
    }

    // L'IdroSbirro usa anche lui EnemyHealth (per essere danneggiabile come gli altri Sbirro,
    // vedi IdroSbirro) ma non deve sparire in polvere: troppo grosso e fisso per
    // avere senso visivamente, resta semplicemente distrutto come prima di questa feature.
    //
    // blastDirection è la direzione in cui vola via lo schizzo rosso, e con lui gli occhi
    // (vedi SpawnDeathEyes), già decisa da chi chiama: in
    // avanti rispetto al colpo per le morti normali (TakeDamage), all'indietro per quelle di
    // scatto (TakeDashHit).
    private void Die(Vector3 blastDirection)
    {
        if (IsSwelling)
        {
            return;
        }

        if (GetComponent<IdroSbirro>() == null)
        {
            SpawnDeathDust(blastDirection);
            SpawnDeathEyes(MeasureHeadPoint(), blastDirection);
        }

        HitEvents.RaiseEnemyKilled(gameObject);
        Destroy(gameObject);
    }

    // Da che parte vola via tutto quanto quando il colpo arriva da lontano: dalla parte opposta
    // a chi ha sparato.
    private Vector3 MeasureBlastDirection(Vector3 sourcePosition)
    {
        return FlattenOrRandom(transform.position - sourcePosition);
    }

    // Appiattisce la direzione sull'orizzontale (lo schizzo non deve mai puntare in su o in giù)
    // e la normalizza. Se quello che arriva è un vettore nullo — non dovrebbe, ma è meglio non
    // dividere per zero — si ripiega su una direzione a caso.
    private static Vector3 FlattenOrRandom(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = Random.insideUnitSphere;
            direction.y = 0f;
        }

        return direction.normalized;
    }

    // Un GameObject a sé con un ParticleSystem creato a runtime (nessun prefab da collegare
    // a mano, come si faceva per il getto dell'IdroSbirro): la polvere vola
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

        ConfigureDustMotion(particles);
        ParticleSystem.MainModule main = particles.main;
        main.startColor = deathDustColor;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, deathDustParticleCount) });

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = deathDustSpreadAngle;

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

    // Come si muove una particella di polvere: durata, velocità, misura e freno. A parte perché
    // serve identico anche a MeasureDustReach, che ne simula una per sapere fin dove arriva.
    private void ConfigureDustMotion(ParticleSystem particles)
    {
        ParticleSystem.MainModule main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = deathDustLifetime;
        main.startLifetime = deathDustLifetime;
        main.startSpeed = deathDustSpeed;
        main.startSize = deathDustSize;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

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
    }

    // Fin dove arriva un getto rosso, in metri: il freno di Unity dipende anche dalla velocità
    // e dalla misura della particella e non ha una formula da rifare a mano, quindi lo si
    // misura. Una particella di polvere invisibile, simulata per tutta la sua vita in un colpo
    // solo e buttata via; il risultato resta in cache finché i parametri della polvere non
    // cambiano (sono gli stessi per tutti gli Sbirro dello stesso tipo).
    private static float cachedDustReach = -1f;
    private static Vector3 cachedDustReachKey;

    private float MeasureDustReach()
    {
        Vector3 key = new Vector3(deathDustSpeed, deathDustSize, deathDustLifetime);
        if (cachedDustReach > 0f && key == cachedDustReachKey)
        {
            return cachedDustReach;
        }

        GameObject probeObject = new GameObject("DustReachProbe");
        try
        {
            ParticleSystem probe = probeObject.AddComponent<ParticleSystem>();
            probe.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            probeObject.GetComponent<ParticleSystemRenderer>().enabled = false;
            ConfigureDustMotion(probe);
            ParticleSystem.EmissionModule emission = probe.emission;
            emission.enabled = false;

            probe.Emit(new ParticleSystem.EmitParams
            {
                position = Vector3.zero,
                velocity = Vector3.forward * deathDustSpeed,
                startLifetime = deathDustLifetime * 2f, // non deve morire prima della misura
            }, 1);
            probe.Simulate(deathDustLifetime, true, false, true);

            ParticleSystem.Particle[] particle = new ParticleSystem.Particle[1];
            if (probe.GetParticles(particle) == 1)
            {
                cachedDustReach = particle[0].position.magnitude;
                cachedDustReachKey = key;
                return cachedDustReach;
            }
        }
        finally
        {
            Destroy(probeObject);
        }

        // Non dovrebbe succedere; nel dubbio, un terzo del tragitto senza freno.
        return deathDustSpeed * deathDustLifetime / 3f;
    }

    // I due occhi (EyeTexture) che saltano via dalla testa insieme alla polvere. Sono particelle
    // e basta, disegnate in billboard, quindi guardano sempre la camera — che per un occhio è
    // tutto quello che serve, perché la pupilla si legge sempre.
    //
    // Volano insieme ai getti rossi: stessa direzione (blastDirection, dentro il suo cono) e
    // più o meno la stessa strada (deathEyeReach, rispetto a MeasureDustReach). Niente
    // gravità: in un terzo di secondo una caduta non si vedrebbe comunque.
    //
    // Le particelle le spariamo a mano con Emit invece di lasciarle a una forma di emissione,
    // per poter dare a ciascun occhio la sua direzione.
    private void SpawnDeathEyes(Vector3 origin, Vector3 blastDirection)
    {
        const int count = 2;
        float size = deathEyeSize;

        GameObject debrisObject = new GameObject("SbirroDeathEyes");
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

        // Scatto secco e poi stop, come la polvere, ma con una curva di velocità invece del freno
        // di LimitVelocity: quello Unity lo pesa anche sulla velocità e sulla misura della
        // particella, e la distanza che ne usciva non si poteva prevedere (gli occhi restavano a
        // meno di un metro dalla testa comunque lo si tarasse). Con la curva, quanto fa un occhio
        // è velocità × durata × area sotto la curva, e la velocità la ricaviamo dalla strada voluta.
        AnimationCurve slowDown = new AnimationCurve(
            new Keyframe(0f, 1f, 0f, -4f),
            new Keyframe(0.3f, 0.2f, -0.5f, -0.5f), // tangente dolce: più ripida la curva scende sotto zero e l'occhio torna indietro
            new Keyframe(1f, 0f, 0f, 0f));
        ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.x = new ParticleSystem.MinMaxCurve(0f);
        velocity.y = new ParticleSystem.MinMaxCurve(0f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f);
        velocity.speedModifier = new ParticleSystem.MinMaxCurve(1f, slowDown);
        float metersPerUnitSpeed = deathDebrisLifetime * AreaUnder(slowDown);

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
        particleRenderer.material.mainTexture = EyeTexture.Get();
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        // Davanti agli schizzi rossi, che sono tanti e partono dallo stesso punto: senza, un
        // occhio può finirci dietro e non vedersi.
        particleRenderer.sortingFudge = -10f;
        particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        particles.Play();

        // Dentro il cono dei getti rossi, uno per lato rispetto al suo centro: volano insieme
        // allo schizzo ma si aprono un po' fra loro. Quanto si aprono e quanta strada fanno lo
        // tira a sorte ogni occhio, così nessuna morte è uguale a un'altra.
        float reach = MeasureDustReach();
        float[] angles =
        {
            -Random.Range(deathDustSpreadAngle * 0.3f, deathDustSpreadAngle),
            Random.Range(deathDustSpreadAngle * 0.3f, deathDustSpreadAngle),
        };

        for (int i = 0; i < count; i++)
        {
            Vector3 direction = Quaternion.Euler(0f, angles[i], 0f) * blastDirection;
            float distance = reach * Random.Range(deathEyeReach.x, deathEyeReach.y);

            particles.Emit(
                new ParticleSystem.EmitParams
                {
                    // Un filo staccati dal centro lungo la propria direzione, così i due occhi
                    // non partono uno sopra l'altro.
                    position = origin + direction * (size * 0.3f),
                    velocity = direction * (distance / metersPerUnitSpeed),
                    startLifetime = deathDebrisLifetime,
                    startSize = size,
                    startColor = Color.white,
                },
                1);
        }

        Destroy(debrisObject, deathDebrisLifetime + 0.1f);
    }

    // Area sotto una curva definita fra 0 e 1, a campioni: quanto spazio fa una particella
    // (per unità di velocità e di durata) se la sua velocità segue quella curva.
    private static float AreaUnder(AnimationCurve curve)
    {
        const int steps = 64;
        float area = 0f;
        for (int i = 0; i < steps; i++)
        {
            area += curve.Evaluate((i + 0.5f) / steps);
        }

        return area / steps;
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
