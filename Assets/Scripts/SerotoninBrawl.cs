using System.Collections.Generic;
using UnityEngine;

// Le risse della serotonina. Uno Sbirro che riceve il colpo di grazia da un'onda serotonina
// (PlayerSerotonin, vedi EnemyHealth.TakeSerotoninDamage) non muore: torna a vita piena, si
// accende di un'aura rossa con i cuoricini e passa dalla parte del Player ("convertito"). Da lì attacca il
// nemico più vicino e non lo molla finché non l'ha ucciso, poi passa al prossimo. Il nemico
// attaccato è costretto a rispondergli: anche lui riceve questo componente ("provocato"), con il
// convertito come unico avversario, e torna normale appena il duello finisce (avversario morto).
//
// Lo stesso componente fa entrambe le parti: per tutta la durata spegne l'AI del nemico
// (EnemyChase, EnemyAttack, EnemyRangedAttack) e ne fa le veci in versione semplice. Si
// cammina dritti verso l'avversario e, una volta a contatto, si carica un colpo che va a segno
// se a fine carica è ancora lì, come EnemyAttack. Vale uguale per lo Sbirro col manganello e per
// quello col fumogeno, che in rissa fa a botte anche lui. Niente NavMesh: con un avversario che
// a sua volta viene incontro, la strada è quasi sempre libera.
//
// Il convertito non lo toccano né le onde del Player (né danno né cura, vedi
// SoundWaveProjectile) né i Raver (RaverAttack, RaverChase). L'IdroSbirro resta fuori da
// tutto: non si converte e non viene scelto come avversario (vedi CanBrawl).
//
// Si aggiunge da solo ai nemici a runtime: i valori qui sotto sono quelli di partenza, ritoccabili
// dall'Inspector in Play.
public class SerotoninBrawl : MonoBehaviour
{
    [Header("Rissa")]
    [SerializeField] private float moveSpeed = 4.5f; // come EnemyChase
    [Tooltip("Danno di ogni colpo andato a segno, in punti vita (EnemyHealth).")]
    [SerializeField] private float hitDamage = 25f;
    [Tooltip("Quanto dura la carica prima che il colpo vada a segno.")]
    [SerializeField] private float windupDuration = 0.8f;
    [Tooltip("Pausa dopo un colpo, prima di caricare il prossimo.")]
    [SerializeField] private float cooldown = 0.7f;
    [Tooltip("Margine oltre il contatto fra i due collider entro cui il colpo arriva.")]
    [SerializeField] private float reachPadding = 0.4f;
    [Tooltip("Ogni quanto il convertito senza avversario ricontrolla se c'è qualcuno da attaccare.")]
    [SerializeField] private float searchInterval = 0.5f;

    [Header("Aura del convertito")]
    [Tooltip("Colore dell'alone sfumato attorno al corpo e della luce che getta intorno.")]
    [SerializeField] private Color auraColor = new Color(1f, 0.08f, 0.12f, 0.2f);
    [Tooltip("Quante macchie d'alone al secondo: più alto = aura più fitta.")]
    [SerializeField] private float auraRate = 14f;
    [Tooltip("Intensità della luce rossa attorno al convertito (0 = niente luce).")]
    [SerializeField] private float auraLightIntensity = 2.5f;
    [SerializeField] private float auraLightRange = 3.5f;
    [SerializeField] private Color heartColor = new Color(1f, 0.25f, 0.4f, 1f);
    [Tooltip("Cuoricini al secondo che escono dall'aura e salgono.")]
    [SerializeField] private float heartRate = 5f;

    private const string AttackTrigger = "Attack";
    private const string MovingParameter = "Moving";

    private EnemyHealth health;
    private EnemyHealth opponent;
    private Rigidbody rb;
    private Animator animator;
    private bool hasAttackTrigger;
    private bool hasMovingParameter;
    private float ownRadius;
    private readonly List<Behaviour> pausedAI = new List<Behaviour>();

    private float windupTimer = -1f; // >= 0 mentre il colpo è in carica
    private int preSwingState; // stato dell'Animator a inizio carica, per annullare il colpo (vedi CancelSwing)
    private float cooldownTimer;
    private float searchTimer;
    private GameObject aura; // alone, luce e cuoricini: tutto sotto un unico oggetto figlio
    private static Texture2D heartTexture;

    public bool IsCharmed { get; private set; }

    // Nemici che possono entrare in una rissa: solo gli Sbirro con EnemyHealth, IdroSbirro escluso
    // (enorme e piantato sul posto, non ha senso farlo camminare né convertirlo).
    public static bool CanBrawl(EnemyHealth enemy)
    {
        return enemy != null && !enemy.IsBoss;
    }

    // Chiamato da EnemyHealth.TakeSerotoninDamage al posto della morte.
    public static void Charm(EnemyHealth enemy)
    {
        SerotoninBrawl brawl = GetOrAdd(enemy);
        brawl.IsCharmed = true;
        brawl.opponent = null; // se era un provocato, il vecchio avversario era un convertito: ora sono dalla stessa parte
        brawl.searchTimer = 0f;
        brawl.CancelSwing();
        brawl.CreateAura();
    }

    // Il convertito che inizia ad attaccare enemy lo costringe a rispondergli. Se enemy è già in
    // un duello con un altro convertito resta su quello: un avversario alla volta.
    private static void Provoke(EnemyHealth enemy, EnemyHealth by)
    {
        SerotoninBrawl brawl = GetOrAdd(enemy);
        if (brawl.opponent == null)
        {
            brawl.opponent = by;
        }
    }

    private static SerotoninBrawl GetOrAdd(EnemyHealth enemy)
    {
        SerotoninBrawl brawl = enemy.GetComponent<SerotoninBrawl>();
        return brawl != null ? brawl : enemy.gameObject.AddComponent<SerotoninBrawl>();
    }

    void Awake()
    {
        health = GetComponent<EnemyHealth>();
        rb = GetComponent<Rigidbody>();

        Collider ownCollider = GetComponentInChildren<Collider>();
        ownRadius = MeasureRadius(ownCollider);

        animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            // Come EnemyRangedAttack: un SetTrigger su un parametro che il controller non ha
            // riempirebbe la Console di avvisi.
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.name == AttackTrigger && parameter.type == AnimatorControllerParameterType.Trigger)
                {
                    hasAttackTrigger = true;
                }
                if (parameter.name == MovingParameter && parameter.type == AnimatorControllerParameterType.Bool)
                {
                    hasMovingParameter = true;
                }
            }
        }

        PauseAI();
    }

    void OnDestroy()
    {
        if (aura != null)
        {
            Destroy(aura);
        }
    }

    void FixedUpdate()
    {
        if (rb != null)
        {
            // Stesso motivo di EnemyChase: il movimento è tutto MovePosition, una velocità
            // rimasta da un urto lo farebbe scivolare.
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Avversario morto (o passato anche lui dalla parte del Player).
        if (opponent == null || (IsCharmed && opponent.IsCharmed))
        {
            opponent = null;
            CancelSwing();

            if (!IsCharmed)
            {
                EndBrawl();
                return;
            }

            searchTimer -= Time.fixedDeltaTime;
            if (searchTimer <= 0f)
            {
                searchTimer = searchInterval;
                opponent = FindNearestEnemy();
                if (opponent != null)
                {
                    Provoke(opponent, health);
                }
            }

            if (opponent == null)
            {
                SetMoving(false);
                return; // nessuno da attaccare: resta lì
            }
        }

        Vector3 toOpponent = opponent.transform.position - Position;
        toOpponent.y = 0f;
        Face(toOpponent);

        float reach = ownRadius + MeasureRadius(opponent.GetComponentInChildren<Collider>()) + reachPadding;
        bool inReach = toOpponent.magnitude <= reach;

        // Durante la carica resta fermo, come EnemyChase a metà attacco.
        if (!inReach && windupTimer < 0f)
        {
            Vector3 next = Position + toOpponent.normalized * moveSpeed * Time.fixedDeltaTime;
            if (rb != null)
            {
                rb.MovePosition(next);
            }
            else
            {
                transform.position = next;
            }
            SetMoving(true);
        }
        else
        {
            SetMoving(false);
        }

        UpdateSwing(inReach);
    }

    private void UpdateSwing(bool inReach)
    {
        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.fixedDeltaTime;
            return;
        }

        if (windupTimer < 0f)
        {
            if (inReach)
            {
                windupTimer = 0f;
                if (hasAttackTrigger)
                {
                    preSwingState = animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
                    animator.SetTrigger(AttackTrigger);
                }
            }
            return;
        }

        windupTimer += Time.fixedDeltaTime;
        if (windupTimer < windupDuration)
        {
            return;
        }

        // Come EnemyAttack: se nel frattempo l'avversario si è staccato, il colpo va a vuoto. Un
        // convertito non colpisce mai un altro convertito, anche se è appena passato di parte.
        windupTimer = -1f;
        if (inReach && !(IsCharmed && opponent.IsCharmed))
        {
            // Col berserk attivo (PlayerBerserk) i convertiti picchiano più forte; i provocati no.
            float damage = IsCharmed ? hitDamage * PlayerBerserk.BrawlDamageMultiplier : hitDamage;
            opponent.TakeDamage(damage, transform.position);
            cooldownTimer = cooldown;
        }
    }

    // Annulla il colpo in carica, animazione compresa: il trigger "Attack" parte all'inizio della
    // carica e, se non lo si toglie, l'animazione del colpo parte lo stesso. Succedeva quando
    // l'avversario veniva convertito a metà carica (capita spesso, è il più vicino e finisce
    // anche lui sotto le onde): il convertito tirava il manganello contro un altro convertito.
    // Togliere il trigger però non basta se l'animazione del colpo è già partita (parte appena
    // inizia la carica): in quel caso si torna allo stato di prima della carica.
    private void CancelSwing()
    {
        bool wasSwinging = windupTimer >= 0f;
        windupTimer = -1f;
        if (hasAttackTrigger)
        {
            animator.ResetTrigger(AttackTrigger);
            if (wasSwinging && animator.GetCurrentAnimatorStateInfo(0).fullPathHash != preSwingState)
            {
                animator.CrossFadeInFixedTime(preSwingState, 0.1f, 0);
            }
        }
    }

    // Il nemico più vicino che può fare a botte e non è già convertito. Solo quelli già dentro
    // l'arena: chi sta ancora camminando da fuori, oltre i muri, non va inseguito là.
    private EnemyHealth FindNearestEnemy()
    {
        EnemyHealth nearest = null;
        float nearestSqrDistance = float.MaxValue;
        Bounds? arena = FloorBounds.Instance != null ? FloorBounds.Instance.Bounds : (Bounds?)null;

        foreach (EnemyHealth enemy in FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None))
        {
            if (enemy == health || enemy.IsCharmed || !CanBrawl(enemy))
            {
                continue;
            }

            Vector3 position = enemy.transform.position;
            if (arena.HasValue)
            {
                Bounds b = arena.Value;
                if (position.x < b.min.x || position.x > b.max.x || position.z < b.min.z || position.z > b.max.z)
                {
                    continue;
                }
            }

            float sqrDistance = (position - Position).sqrMagnitude;
            if (sqrDistance < nearestSqrDistance)
            {
                nearest = enemy;
                nearestSqrDistance = sqrDistance;
            }
        }

        return nearest;
    }

    private void PauseAI()
    {
        foreach (Behaviour ai in new Behaviour[] { GetComponent<EnemyChase>(), GetComponent<EnemyAttack>(), GetComponent<EnemyRangedAttack>() })
        {
            if (ai != null && ai.enabled)
            {
                ai.enabled = false;
                pausedAI.Add(ai);
            }
        }
    }

    // Solo per i provocati: il convertito resta in rissa per sempre (finché non muore).
    private void EndBrawl()
    {
        foreach (Behaviour ai in pausedAI)
        {
            if (ai != null)
            {
                ai.enabled = true;
            }
        }
        pausedAI.Clear();
        SetMoving(false);
        Destroy(this);
    }

    private Vector3 Position => rb != null ? rb.position : transform.position;

    private void Face(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Quaternion rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        if (rb != null)
        {
            rb.MoveRotation(rotation);
        }
        else
        {
            transform.rotation = rotation;
        }
    }

    private void SetMoving(bool moving)
    {
        if (hasMovingParameter)
        {
            animator.SetBool(MovingParameter, moving);
        }
    }

    // Come EnemyChase: l'AABB in world space ha già dentro la scala del prefab.
    private static float MeasureRadius(Collider collider)
    {
        return collider != null ? Mathf.Max(collider.bounds.extents.x, collider.bounds.extents.z) : 0.5f;
    }

    // Aura rossa con i cuoricini, per tutto il tempo: così si riconosce a colpo d'occhio chi sta
    // dalla parte del Player. Tre pezzi sotto un unico oggetto figlio, centrato sul corpo:
    // - l'alone: macchie rosse sfumate (SoftParticleTexture) che nascono attorno al corpo e lo
    //   seguono (simulate in coordinate locali), così fanno un bagliore attaccato al modello;
    // - una luce rossa, che tinge il pavimento e i vicini;
    // - i cuoricini, che nascono dentro l'aura e salgono lasciandosela dietro (coordinate mondo).
    // Costruiti a runtime come le scintille delle ammo (CollectibleShine); il cuore è disegnato
    // qui (stessa formula di CozyBeatFX, dal pacchetto Onde Serotonina).
    private void CreateAura()
    {
        if (aura != null)
        {
            return;
        }

        // Ingombro del modello: l'alone lo avvolge, i cuoricini partono dalla metà in su.
        Bounds body = new Bounds(transform.position + Vector3.up, Vector3.one);
        bool found = false;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r.localBounds.size.sqrMagnitude <= 0.000001f)
            {
                continue;
            }
            if (!found)
            {
                body = r.bounds;
                found = true;
            }
            else
            {
                body.Encapsulate(r.bounds);
            }
        }

        aura = new GameObject("SerotoninAura");
        aura.transform.SetParent(transform, false);
        aura.transform.position = body.center;

        float halfWidth = Mathf.Max(body.extents.x, body.extents.z);
        CreateGlow(aura.transform, halfWidth, body.extents.y);
        CreateHeartParticles(aura.transform, halfWidth, body.extents.y);

        if (auraLightIntensity > 0f)
        {
            Light light = aura.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(auraColor.r, auraColor.g, auraColor.b, 1f);
            light.intensity = auraLightIntensity;
            light.range = auraLightRange;
            light.shadows = LightShadows.None;
        }
    }

    private void CreateGlow(Transform parent, float halfWidth, float halfHeight)
    {
        ParticleSystem glow = CreateParticles("Alone", parent, SoftParticleTexture.Get());

        ParticleSystem.MainModule main = glow.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.15f);
        main.startSize = new ParticleSystem.MinMaxCurve(halfWidth * 1.6f, halfWidth * 2.2f);
        main.startColor = auraColor;
        main.simulationSpace = ParticleSystemSimulationSpace.Local; // l'alone resta attaccato al corpo

        ParticleSystem.EmissionModule emission = glow.emission;
        emission.rateOverTime = auraRate;

        // Un ellissoide alto quanto il corpo, che lo avvolge.
        ParticleSystem.ShapeModule shape = glow.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = halfWidth * 0.9f;
        shape.radiusThickness = 0f; // dalla superficie: attorno al corpo, non tutte ammucchiate davanti
        shape.scale = new Vector3(1f, Mathf.Max(1f, halfHeight / Mathf.Max(halfWidth, 0.01f)), 1f);

        SetFadeInOut(glow);
        glow.Play();
    }

    private void CreateHeartParticles(Transform parent, float halfWidth, float halfHeight)
    {
        ParticleSystem hearts = CreateParticles("Cuoricini", parent, GetHeartTexture());
        hearts.transform.localPosition = Vector3.up * halfHeight * 0.3f;

        ParticleSystem.MainModule main = hearts.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.4f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
        main.startColor = heartColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        ParticleSystem.EmissionModule emission = hearts.emission;
        emission.rateOverTime = heartRate;

        ParticleSystem.ShapeModule shape = hearts.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = halfWidth * 0.8f;

        // Salgono dritti, con un filo di oscillazione di lato.
        ParticleSystem.VelocityOverLifetimeModule velocity = hearts.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.8f, 1.3f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);

        SetFadeInOut(hearts);
        hearts.Play();
    }

    private static ParticleSystem CreateParticles(string name, Transform parent, Texture2D texture)
    {
        GameObject particleObject = new GameObject(name);
        particleObject.transform.SetParent(parent, false);

        ParticleSystem particles = particleObject.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.playOnAwake = false;
        main.loop = true;

        ParticleSystemRenderer particleRenderer = particleObject.GetComponent<ParticleSystemRenderer>();
        // Sprites/Default come gli altri effetti del progetto: c'è di sicuro anche in URP.
        particleRenderer.material = new Material(Shader.Find("Sprites/Default"));
        particleRenderer.material.mainTexture = texture;
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        particleRenderer.receiveShadows = false;
        return particles;
    }

    private static void SetFadeInOut(ParticleSystem particles)
    {
        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) },
        });
    }

    private static Texture2D GetHeartTexture()
    {
        if (heartTexture != null)
        {
            return heartTexture;
        }

        const int N = 64;
        heartTexture = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SerotoninHeart" };
        Color32[] pixels = new Color32[N * N];
        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                // Curva del cuore (x² + y² - 1)³ - x²y³ <= 0, con 4 campioni per pixel per i bordi morbidi.
                float coverage = 0f;
                for (int s = 0; s < 4; s++)
                {
                    float u = ((x + (s & 1) * 0.5f + 0.25f) / N - 0.5f) * 2.6f;
                    float v = ((y + (s >> 1) * 0.5f + 0.25f) / N - 0.45f) * 2.6f;
                    float a = u * u + v * v - 1f;
                    if (a * a * a - u * u * v * v * v <= 0f)
                    {
                        coverage += 0.25f;
                    }
                }
                pixels[y * N + x] = new Color32(255, 255, 255, (byte)(coverage * 255));
            }
        }
        heartTexture.SetPixels32(pixels);
        heartTexture.Apply();
        return heartTexture;
    }
}
