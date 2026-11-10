using UnityEngine;

// Da mettere sul GameObject "Fuocherello" creato dal comando Tools/Fuoco/Crea un fuocherello.
// La fiamma in sé la disegna lo shader (Assets/Shaders/FuocoCartoon.shader); qui c'è quello che
// uno shader non può fare, perché non riguarda dei pixel ma la scena intorno:
//
//  - il respiro e il crepitio della luce. Una fiamma che guizza mentre la luce che getta è
//    fissa si stacca subito: è la luce sulle cose attorno a dire che il fuoco è vero. Il
//    respiro sono onde che non vanno a tempo fra loro; il crepitio invece è fatto di scatti a
//    intervalli irregolari, perché con le sole onde resterebbe prevedibile (vedi UpdateCrackle);
//
//  - le scintille che salgono, un pugno di particelle tonde e brevi (stesso approccio di
//    polvere di EnemyHealth, con la nuvoletta di RoundParticleTexture).
//
// La luce se la cerca da sé fra i figli: se non ce n'è una, il fuoco resta comunque acceso come
// disegno e non succede niente di male.
public class CartoonFire : MonoBehaviour
{
    [Header("Respiro della luce")]
    [Tooltip("Intensità media della luce. Respiro e crepitio la fanno oscillare attorno a questo valore: se vuoi un fuoco più forte alza questo, non le ampiezze.")]
    [SerializeField] private float lightIntensity = 3.2f;
    [Tooltip("Ampiezza dell'oscillazione lenta e regolare: 0 = luce fissa, 0.2 = va dal 80% al 120%. È il respiro di fondo, non il crepitio.")]
    [Range(0f, 0.6f)] [SerializeField] private float flickerAmount = 0.18f;
    [Tooltip("Quanto è veloce il respiro, in battiti al secondo circa.")]
    [SerializeField] private float flickerSpeed = 3.4f;

    [Header("Crepitio")]
    [Tooltip("Ampiezza degli scatti irregolari, quelli che fanno 'crepitare' la luce. È questo, non flickerAmount, a dare l'idea della legna che brucia.")]
    [Range(0f, 0.8f)] [SerializeField] private float crackleAmount = 0.3f;
    [Tooltip("Ogni quanto scatta, minimo e massimo in secondi. La forbice larga è il punto: intervalli tutti uguali si sentirebbero come un ritmo.")]
    [SerializeField] private Vector2 crackleInterval = new Vector2(0.03f, 0.18f);
    [Tooltip("Quanto in fretta raggiunge il nuovo valore. Alto = scatti secchi, basso = passaggi morbidi.")]
    [SerializeField] private float crackleSnap = 9f;

    [Header("Scintille")]
    [Tooltip("Quante scintille al secondo. Zero le spegne.")]
    [SerializeField] private float sparkRate = 26f;
    [Tooltip("Quanto salgono, in unità al secondo.")]
    [SerializeField] private float sparkSpeed = 1.6f;
    [Tooltip("Quanto vivono: brevi, devono spegnersi salendo.")]
    [SerializeField] private float sparkLifetime = 1.1f;
    [Tooltip("Colore delle scintille. Sopra 1 (HDR) le fa prendere dal Bloom come la fiamma.")]
    [SerializeField] private Color sparkColor = new Color(4f, 1.6f, 0.3f, 1f);

    // Nome del figlio da cui escono le scintille: lo cerca CartoonFire e lo crea il comando
    // Tools/Fuoco, e deve combaciare fra i due.
    public const string SparkChildName = "Scintille";

    private Light fireLight;

    // Sfasamento di partenza personale: due fuochi accesi nello stesso momento tremolerebbero
    // all'unisono, e si vedrebbe che è finto.
    private float phase;

    // Stato del crepitio: dove sta adesso, dove sta andando, e quando sceglierà la prossima meta.
    private float crackle;
    private float crackleTarget;
    private float nextCrackleTime;

    void Awake()
    {
        fireLight = GetComponentInChildren<Light>();
        phase = Random.Range(0f, 100f);
        SetUpSparks();
    }

    void Update()
    {
        if (fireLight == null)
        {
            return;
        }

        // Il respiro di fondo: tre onde di periodo volutamente non multiplo l'uno dell'altro, così
        // la loro somma non torna mai uguale a sé stessa e l'occhio non ci trova un motivo che si
        // ripete.
        float t = (Time.time + phase) * flickerSpeed;
        float wave = Mathf.Sin(t) * 0.55f + Mathf.Sin(t * 2.37f) * 0.3f + Mathf.Sin(t * 4.11f) * 0.15f;

        UpdateCrackle();

        // Le due cose si sommano ma restano regolabili a parte, perché fanno due lavori diversi:
        // il respiro è il fuoco che vive, il crepitio è la legna che scoppietta.
        float variation = wave * flickerAmount + crackle * crackleAmount;
        fireLight.intensity = Mathf.Max(0f, lightIntensity * (1f + variation));
    }

    // Il crepitio non si può fare con delle onde: per quanto le si mescoli, restano prevedibili.
    // E nemmeno con un numero a caso ad ogni fotogramma, che dà lo sfarfallio della lampadina
    // rotta e per giunta cambia aspetto al cambiare del frame rate.
    //
    // Qui la luce punta verso un valore estratto a sorte e ci va incontro a velocità finita; ogni
    // tanto — a intervalli anch'essi a sorte — cambia meta. Sono gli intervalli disuguali a fare
    // il crepitio: se fossero tutti uguali si sentirebbe il ritmo, che è proprio quello che un
    // fuoco non ha.
    private void UpdateCrackle()
    {
        if (Time.time >= nextCrackleTime)
        {
            // Di tanto in tanto uno scoppiettio forte, più spesso un fremito appena accennato:
            // anche l'ampiezza deve essere irregolare, non solo il momento.
            crackleTarget = Random.value < 0.25f
                ? Random.Range(-1f, 1f)
                : Random.Range(-0.35f, 0.35f);

            nextCrackleTime = Time.time + Random.Range(crackleInterval.x, crackleInterval.y);
        }

        crackle = Mathf.MoveTowards(crackle, crackleTarget, crackleSnap * Time.deltaTime);
    }

    // Le scintille escono dal figlio "Scintille", che è un oggetto vero della scena: creato dal
    // comando Tools/Fuoco, lo si può selezionare in Hierarchy e trascinare dove si vuole, e la
    // posizione resta. Qui NON viene mai spostato: si prende com'è messo in scena.
    //
    // Se quel figlio non c'è (fuochi creati prima che esistesse) se ne fa uno al volo alla base
    // della fiamma, così il fuoco funziona comunque — ma essendo creato a runtime quello sparisce
    // uscendo dal Play, e per poterlo spostare va creato a mano: un GameObject vuoto figlio del
    // fuoco, chiamato esattamente "Scintille".
    private void SetUpSparks()
    {
        if (sparkRate <= 0f)
        {
            return;
        }

        Transform existing = transform.Find(SparkChildName);
        GameObject sparkObject;
        if (existing != null)
        {
            sparkObject = existing.gameObject;
        }
        else
        {
            sparkObject = new GameObject(SparkChildName);
            sparkObject.transform.SetParent(transform, false);
        }

        ParticleSystem particles = sparkObject.GetComponent<ParticleSystem>();
        if (particles == null)
        {
            particles = sparkObject.AddComponent<ParticleSystem>();
        }

        // AddComponent lo fa partire subito (Play On Awake di default): da "in play" Unity non
        // lascia cambiare la durata.
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.playOnAwake = true;
        main.loop = true;
        main.duration = sparkLifetime;
        main.startLifetime = new ParticleSystem.MinMaxCurve(sparkLifetime * 0.5f, sparkLifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(sparkSpeed * 0.5f, sparkSpeed);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
        main.startColor = sparkColor;
        main.gravityModifier = -0.12f; // salgono: l'aria calda le spinge su
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = sparkRate;

        // Escono da un cerchietto alla base della fiamma, non da un punto solo.
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 18f;
        shape.radius = 0.18f;

        // Salgono zigzagando invece che in linea retta: è quello che fa una scintilla vera
        // nell'aria che si muove.
        ParticleSystem.NoiseModule noise = particles.noise;
        noise.enabled = true;
        noise.strength = 0.6f;
        noise.frequency = 1.3f;

        // Si spengono salendo, invece di sparire di colpo a mezz'aria.
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
                new GradientAlphaKey(1f, 0.15f),
                new GradientAlphaKey(0f, 1f),
            },
        });

        ParticleSystemRenderer particleRenderer = sparkObject.GetComponent<ParticleSystemRenderer>();
        particleRenderer.material = new Material(MaterialLibrary.SpriteDefault);
        particleRenderer.material.mainTexture = RoundParticleTexture.Get();
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        particles.Play();
    }
}
