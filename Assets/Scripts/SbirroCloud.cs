using System.Collections.Generic;
using UnityEngine;

// Da mettere su un GameObject vuoto usato come prefab (vedi ParabolicProjectile.cloudPrefab).
// È la nuvoletta che la sfera dello Sbirro lascia a terra quando finisce la sua corsa
// (colpo diretto o atterraggio a vuoto): resta ferma per lifespan secondi, sfumando via
// via, e infligge un colpo (come un attacco corpo a corpo di Sbirro, vedi IEnemyAttackTarget)
// a chi la tocca — una sola volta per bersaglio, anche se ci resta dentro più a lungo.
// Console/DJ e SoundSystem sono ignorati anche se la toccano: il fumogeno non deve mai
// far danno a loro (vedi ParabolicProjectile/EnemyRangedAttack).
[RequireComponent(typeof(SphereCollider))]
public class SbirroCloud : MonoBehaviour
{
    [Header("Durata")]
    [SerializeField] private float lifespan = 3f; // quanto resta a terra prima di sparire

    [Header("Aspetto")]
    [Tooltip("Raggio base della nuvola: da qui si ricavano sia la dimensione/sparsità dei puffi visivi sia il raggio vero del trigger (vedi DamageRadius), che quindi restano sempre coerenti tra loro senza doverli ritarare a mano separatamente.")]
    [SerializeField] private float radius = 2f;
    [SerializeField] private Color cloudColor = new Color(1f, 1f, 1f, 0.35f);
    [Tooltip("Quanti puffi di fumo compongono la nuvola.")]
    [SerializeField] private int particleCount = 80;

    // Stessi moltiplicatori usati in CreateVisual per shape.radius e per la dimensione
    // massima dei puffi: il trigger deve arrivare fin dove il fumo si vede davvero (il bordo
    // di un puffo, non solo il suo punto d'origine), altrimenti si potrebbe camminarci dentro
    // senza subire danno mentre visivamente si è ancora in mezzo alla nuvola.
    private const float ShapeSpreadMultiplier = 1.3f;
    private const float MaxParticleSizeMultiplier = 1.8f;
    private float DamageRadius => radius * (ShapeSpreadMultiplier + MaxParticleSizeMultiplier * 0.5f);

    private readonly HashSet<Collider> alreadyHit = new HashSet<Collider>();
    private float elapsed;

    void Awake()
    {
        SphereCollider trigger = GetComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = DamageRadius;

        CreateVisual();
    }

    // Tiene il collider sincronizzato con DamageRadius anche in editor (non solo a runtime
    // via Awake): senza, modificando radius nell'Inspector il valore serializzato del
    // SphereCollider resta quello vecchio finché non si va in Play, disallineando hitbox
    // e raggio visivo nel frattempo (successo con SbirroNuvola: radius 1.5, m_Radius 2).
    void OnValidate()
    {
        SphereCollider trigger = GetComponent<SphereCollider>();
        if (trigger != null)
        {
            trigger.radius = DamageRadius;
        }
    }

    void Update()
    {
        elapsed += Time.deltaTime;

        if (elapsed >= lifespan)
        {
            Destroy(gameObject);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (alreadyHit.Contains(other))
        {
            return;
        }

        if (other.GetComponentInParent<DJConsoleHealth>() != null || other.GetComponentInParent<SoundSystem>() != null)
        {
            return;
        }

        IEnemyAttackTarget target = other.GetComponentInParent<IEnemyAttackTarget>();
        if (target == null)
        {
            return;
        }

        alreadyHit.Add(other);
        target.TakeHit();
    }

    // Un ParticleSystem invece della vecchia sfera semitrasparente piatta: quella, essendo
    // dello stesso grigio del pavimento placeholder, risultava quasi invisibile (si vedeva
    // solo come una leggera ombra). Puffi di fumo separati, con una lieve deriva verso l'alto,
    // si notano molto di più e sfumano da soli via ColorOverLifetime invece che aggiornando
    // ogni frame un materiale condiviso (stesso approccio di EnemyHealth.SpawnDeathDust, qui
    // però senza burst direzionale: i puffi restano fermi sull'area colpita).
    private void CreateVisual()
    {
        GameObject visualObject = new GameObject("CloudVisual");
        visualObject.transform.SetParent(transform, false);

        ParticleSystem particles = visualObject.AddComponent<ParticleSystem>();

        // AddComponent lo fa partire subito (Play On Awake di default): senza fermarlo prima,
        // il main module sotto risulterebbe "in play" e Unity rifiuterebbe di cambiargli la
        // durata ("Setting the duration while system is still playing is not supported").
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = lifespan;
        main.startLifetime = lifespan;
        main.startSpeed = 0.15f; // deriva lentissima: devono restare ammassati, non allargarsi
        main.startSize = new ParticleSystem.MinMaxCurve(radius * 1.1f, radius * MaxParticleSizeMultiplier); // grosse e sovrapposte, non puntini separati
        main.startColor = cloudColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -0.01f; // sale appena, resta bassa invece di alzarsi

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, particleCount) });

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        // Origine sparsa su gran parte del raggio della nuvola (non più ammassata al centro):
        // essendo le particelle già grosse e sovrapposte (vedi startSize sopra), restano
        // comunque un'unica massa ma coprono un'area più estesa invece di un ammasso piccolo.
        shape.radius = radius * ShapeSpreadMultiplier;
        // Schiacciata sull'asse verticale: un'origine sferica piena farebbe partire dei puffi
        // anche parecchio sopra il pavimento, una nuvola bassa deve restare vicina a terra.
        shape.scale = new Vector3(1f, 0.3f, 1f);

        // Sfuma da sola nel tempo (alpha piena all'inizio, 0 a fine vita): stessa curva usata
        // prima manualmente su Update(), ma gestita per particella invece che sul materiale.
        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(cloudColor, 0f), new GradientColorKey(cloudColor, 1f) },
            new[] { new GradientAlphaKey(cloudColor.a, 0f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = gradient;

        ParticleSystemRenderer particleRenderer = visualObject.GetComponent<ParticleSystemRenderer>();
        particleRenderer.material = new Material(Shader.Find("Sprites/Default"));
        // Bordo sfumato invece del disco secco di RoundParticleTexture (quello, con un bordo
        // netto, faceva sembrare ogni puffo una bolla di sapone invece che fumo soffice).
        particleRenderer.material.mainTexture = SoftParticleTexture.Get();
        particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        particles.Play();
    }
}
