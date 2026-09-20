using System.Collections.Generic;
using UnityEngine;

// Da mettere sul Camper (ce lo mette da sé CamperVehicle): fa uscire un filo di fumo dalle bocche
// di scarico mentre il mezzo è in moto, e il fumo resta indietro rispetto alla marcia.
//
// Non ha bisogno di sapere niente da nessuno: si accorge da solo se il mezzo si sta muovendo e in
// che direzione, guardando di quanto si è spostato da un fotogramma all'altro. Quindi funziona
// comunque sia fatta la guida, e a mezzo fermo smette senza che nessuno glielo dica.
//
// Le particelle sono simulate in coordinate mondo: appena nate smettono di seguire il mezzo e
// restano dove sono, che è già metà dell'effetto "il fumo rimane indietro". L'altra metà è la
// bocca che soffia all'indietro, cioè verso il lato opposto alla direzione di marcia.
//
// Tutto procedurale, nessun prefab da collegare: stesso approccio di CollectibleShine e
// EnemyHealth.SpawnDeathDust, con la nuvoletta tonda di RoundParticleTexture.
public class ExhaustSmoke : MonoBehaviour
{
    [Header("Bocche di scarico")]
    [Tooltip("Da dove esce il fumo. Se lo lasci vuoto le bocche vengono piazzate da sole sul davanti del mezzo, in alto e simmetriche: per metterle esatte, crea due oggetti vuoti sulle bocche del modello e trascinali qui.")]
    [SerializeField] private Transform[] exhaustPoints;
    [Tooltip("In mancanza di oggetti trascinati sopra, vengono usati i figli del modello che hanno questo pezzo di nome. Se non ne trova nemmeno uno, si passa al piazzamento automatico.")]
    [SerializeField] private string exhaustNameFilter = "scarico";
    [Tooltip("Piazzamento automatico (usato solo se non ci sono bocche trascinate né oggetti 'scarico' nel modello): di quanto le bocche si spostano dal centro dell'ingombro verso il muso, in frazione della metà lunghezza del mezzo. Vicino a 0 = verso il centro, altezza del pilota; vicino a 1 = punta del muso.")]
    [SerializeField] private float autoForwardFraction = 0.2f;
    [Tooltip("Piazzamento automatico: di quanto le bocche si allargano dal centro verso le fiancate, in frazione della metà larghezza del mezzo. Vicino a 0 = appaiate sull'asse centrale; vicino a 1 = a filo delle fiancate.")]
    [SerializeField] private float autoSideFraction = 0.15f;
    [Tooltip("Piazzamento automatico: altezza delle bocche, in frazione della metà altezza del mezzo. 0 = a livello del pianale, 1 = al tetto/altezza massima del modello.")]
    [SerializeField] private float autoHeightFraction = 0.85f;

    [Header("Fumo")]
    [Tooltip("Quanti sbuffi al secondo per ogni bocca. Alzandolo il fumo diventa più denso: oltre un certo punto però diventa una cortina, e per farlo notare di più conviene prima intervenire sul colore e sulla durata.")]
    [SerializeField] private float smokeRate = 30f;
    [Tooltip("Quanto vive ogni sbuffo, in secondi: più lungo = scia di fumo più lunga dietro al mezzo.")]
    [SerializeField] private float smokeLifetime = 1.4f;
    [Tooltip("Quanto è grande uno sbuffo appena nato. Crescono mentre si dissolvono, quindi tienilo piccolo.")]
    [SerializeField] private float smokeSize = 0.55f;
    [Tooltip("Quanto forte viene soffiato fuori il fumo, in unità al secondo.")]
    [SerializeField] private float smokeSpeed = 1.6f;
    [Tooltip("Colore del fumo. Chiaro si stacca dal terreno e dal mezzo, scuro sparisce: è la cosa che più di tutte decide quanto il fumo si vede. L'alpha qui è il massimo che raggiunge, poi la dissolvenza lungo la vita fa il resto.")]
    [SerializeField] private Color smokeColor = new Color(0.92f, 0.91f, 0.89f, 0.9f);
    [Tooltip("Sotto questa velocità (unità al secondo) il mezzo è considerato fermo e il fumo si spegne.")]
    [SerializeField] private float minSpeed = 0.5f;

    private readonly List<ParticleSystem> pipes = new List<ParticleSystem>();
    private Vector3 lastPosition;

    void Start()
    {
        lastPosition = transform.position;

        foreach (Transform point in FindExhaustPoints())
        {
            pipes.Add(CreateSmoke(point));
        }
    }

    void Update()
    {
        if (pipes.Count == 0)
        {
            return;
        }

        // Spostamento di questo fotogramma: dice sia se il mezzo è in moto sia dove sta andando,
        // senza chiedere niente a chi lo guida.
        Vector3 travel = transform.position - lastPosition;
        lastPosition = transform.position;

        float speed = travel.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
        bool moving = speed >= minSpeed;

        // Le bocche soffiano dalla parte opposta alla marcia, e un po' verso l'alto come fa il
        // fumo vero. Unity emette lungo l'asse in avanti della bocca, quindi basta girarla.
        if (moving)
        {
            Vector3 backwards = (-travel.normalized + Vector3.up * 0.35f).normalized;
            foreach (ParticleSystem pipe in pipes)
            {
                pipe.transform.rotation = Quaternion.LookRotation(backwards, Vector3.up);
            }
        }

        foreach (ParticleSystem pipe in pipes)
        {
            ParticleSystem.EmissionModule emission = pipe.emission;
            emission.enabled = moving;
        }
    }

    // In ordine: le bocche trascinate a mano, poi eventuali oggetti del modello che si chiamano
    // come il filtro, e come ultima risorsa due bocche piazzate a occhio sul davanti in alto.
    private List<Transform> FindExhaustPoints()
    {
        List<Transform> points = new List<Transform>();

        if (exhaustPoints != null)
        {
            foreach (Transform point in exhaustPoints)
            {
                if (point != null)
                {
                    points.Add(point);
                }
            }
        }

        if (points.Count > 0)
        {
            return points;
        }

        if (!string.IsNullOrEmpty(exhaustNameFilter))
        {
            string filter = exhaustNameFilter.ToLowerInvariant();
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child != transform && child.name.ToLowerInvariant().Contains(filter))
                {
                    points.Add(child);
                }
            }
        }

        if (points.Count > 0)
        {
            return points;
        }

        return CreateDefaultPoints();
    }

    // Due bocche simmetriche sul davanti del mezzo, in alto: il modello non ha oggetti che si
    // chiamano "scarico", quindi l'unica informazione disponibile è il suo ingombro. Approssimato
    // per forza, ma è un punto di partenza che si vede subito in scena e si corregge trascinando
    // due oggetti vuoti nel campo qui sopra.
    private List<Transform> CreateDefaultPoints()
    {
        List<Transform> points = new List<Transform>();

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return points;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        // Il muso del mezzo lo sa il CamperVehicle, che l'ha misurato sulle ruote.
        CamperVehicle vehicle = GetComponentInParent<CamperVehicle>();
        Vector3 nose = vehicle != null ? vehicle.ParkedNose : transform.forward;
        nose.y = 0f;
        nose = nose.sqrMagnitude > 0.0001f ? nose.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, nose).normalized;

        float forwardReach = Vector3.Dot(bounds.extents, new Vector3(Mathf.Abs(nose.x), 0f, Mathf.Abs(nose.z))) * autoForwardFraction;
        float sideReach = Vector3.Dot(bounds.extents, new Vector3(Mathf.Abs(right.x), 0f, Mathf.Abs(right.z))) * autoSideFraction;

        for (int side = -1; side <= 1; side += 2)
        {
            GameObject point = new GameObject($"BoccaScarico{(side < 0 ? "Sx" : "Dx")}");
            point.transform.SetParent(transform, false);
            point.transform.position = bounds.center
                                       + nose * forwardReach
                                       + right * (sideReach * side)
                                       + Vector3.up * (bounds.extents.y * autoHeightFraction);
            points.Add(point.transform);
        }

        return points;
    }

    // Una bocca = un ParticleSystem su un oggetto a sé, figlio del mezzo perché la bocca deve
    // seguirlo, ma con le particelle simulate in coordinate mondo perché il fumo, quello, no.
    private ParticleSystem CreateSmoke(Transform point)
    {
        GameObject smokeObject = new GameObject("FumoScarico");
        smokeObject.transform.SetParent(point, false);

        ParticleSystem particles = smokeObject.AddComponent<ParticleSystem>();

        // AddComponent lo fa partire subito (Play On Awake di default): senza fermarlo prima, il
        // main module risulterebbe "in play" e Unity rifiuterebbe di cambiargli la durata.
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.playOnAwake = true;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(smokeLifetime * 0.6f, smokeLifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(smokeSpeed * 0.6f, smokeSpeed);
        main.startSize = new ParticleSystem.MinMaxCurve(smokeSize * 0.6f, smokeSize);
        main.startColor = smokeColor;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = -0.05f; // il fumo sale piano invece di cadere
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = smokeRate;
        emission.enabled = false; // a mezzo fermo non esce niente, vedi Update

        // Cono stretto: un getto che esce dalla bocca, non una nuvola che la circonda.
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = 0.05f;

        // Rallenta subito dopo essere stato soffiato fuori: il fumo perde spinta in fretta e resta
        // lì a diluirsi, ed è quello che lo fa sembrare fumo invece che spruzzo.
        ParticleSystem.LimitVelocityOverLifetimeModule limitVelocity = particles.limitVelocityOverLifetime;
        limitVelocity.enabled = true;
        limitVelocity.drag = 2.5f;
        limitVelocity.limit = smokeSpeed;

        // Cresce mentre si dissolve, come una nuvoletta che si allarga diluendosi.
        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.6f),
            new Keyframe(1f, 2.2f)));

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
                new GradientAlphaKey(0.35f, 0f),
                new GradientAlphaKey(1f, 0.15f), // monta subito: lo sbuffo deve "sbocciare", non comparire già pieno
                new GradientAlphaKey(0f, 1f),
            },
        });

        ParticleSystemRenderer particleRenderer = smokeObject.GetComponent<ParticleSystemRenderer>();
        particleRenderer.material = new Material(Shader.Find("Sprites/Default"));
        particleRenderer.material.mainTexture = RoundParticleTexture.Get();
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        particleRenderer.receiveShadows = false;

        particles.Play();
        return particles;
    }
}
