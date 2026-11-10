using System.Collections;
using UnityEngine;

// Da mettere sul GameObject "sound system" in scena (Assets/Prefabs/sound system.prefab),
// quello composto dalle 4 casse (i figli diretti muro1..muro4). Resta silenzioso finché
// non viene attivato da SoundSystemAmmoPickup; da quel momento spara automaticamente
// un'onda sonora da OGNI cassa, ciascuna davanti a sé (nella propria transform.forward),
// con lo stesso ritmo di PlayerShooting e un cono il 50% più lungo e più ampio di quello
// del Player con Area e Deep power-up attivi insieme (vedi SoundWaveProjectile.ApplyPowerUps).
// bonusConeAngle/bonusMaxRadius qui sotto sono quindi 1.5x i valori corrispondenti di
// AreaSoundWavePowerUpPickup.bonusConeAngle e DeepSoundWavePowerUpPickup.bonusMaxRadius
// (sommati alla base di SoundWave.prefab: coneAngle 60, maxRadius 12), non più allineati:
// il buff extra è voluto solo per il sound system, non per il Player.
//
// Bersaglio prioritario della polizia (vedi EnemyChase/EnemyRangedAttack): ha una vita
// propria, colpita a "colpi" come il Player (IEnemyAttackTarget.TakeHit()); la sua
// distruzione pone fine alla partita in sconfitta (vedi GameOverState), come la morte
// del Player o della Console/DJ.
public class SoundSystem : MonoBehaviour, IEnemyAttackTarget
{
    [Header("Vita")]
    [SerializeField] private int maxHits = 15; // quanti colpi di Sbirro può subire prima di essere distrutto

    [Header("Feedback danno")]
    [Tooltip("Se vuoto vengono cercati tutti i Renderer nei figli (le 4 casse): lampeggiano tutte insieme, non solo una, altrimenti il colpo passa facilmente inosservato.")]
    [SerializeField] private Renderer[] targetRenderers;
    [SerializeField] private Color flashColor = Color.red;
    [SerializeField] private float flashDuration = 0.15f; // durata del lampo di colore

    [Header("Sconfitta")]
    [Tooltip("Nomi dei GameObject in scena che saltano in aria insieme al SoundSystem, ognuno con la sua esplosione.")]
    [SerializeField] private string[] alsoExplodeNames =
    {
        "LEFT_DECO",
        "RIGHT_DECO",
        "CLOWN-DECO",
        "Meshy_AI_Blue_Octopus_Tentacle_0913183028_texture",
    };
    [Tooltip("Metri di cui la camera si ferma più avanti del SoundSystem, verso il centro della pista (dietro c'è il mare).")]
    [SerializeField] private float defeatCameraShift = 2.5f;

    private int hitsTaken;
    private bool isDestroyed;
    private Material[] materials;
    private Color[] originalColors;
    private Coroutine flashRoutine;

    // Usato dalla minimappa (vedi MinimapUI) per mostrare quanta vita resta.
    public float HealthFraction => Mathf.Clamp01(1f - (float)hitsTaken / maxHits);
    public int RemainingHits => Mathf.Max(0, maxHits - hitsTaken);
    public int MaxHits => maxHits;

    [Header("Sparo")]
    [SerializeField] private GameObject projectilePrefab; // stesso prefab dell'onda sonora usato da PlayerShooting.projectilePrefab
    [Tooltip("Battiti al minuto: un'onda sparata da ogni cassa ad ogni battito, come PlayerShooting.")]
    [SerializeField] private float bpm = 172f;

    [Header("Cono (= Player con Area + Deep power-up attivi insieme, +50%)")]
    [SerializeField] private float bonusConeAngle = 75f; // risultato: coneAngle totale 135 (base 60 + 75), il 50% in più dei 90 del Player con Area+Deep
    [SerializeField] private float bonusMaxRadius = 21f; // risultato: maxRadius totale 33 (base 12 + 21), il 50% in più dei 22 del Player con Area+Deep

    [Header("Durata")]
    [SerializeField] private float activeDuration = 15f; // N: secondi di sparo dopo la raccolta dell'ammo, poi si rispegne

    private Transform[] crates;
    private BackgroundMusicBoost musicBoost; // opzionale: se presente in scena, il volume/bassi della traccia salgono insieme allo sparo
    private float timer;
    private bool isActive;
    private float activeTimer;

    void Awake()
    {
        // Ogni figlio diretto è una cassa: ognuna spara davanti a sé, nella propria
        // transform.forward, indipendentemente da quante/come sono orientate.
        crates = new Transform[transform.childCount];
        for (int i = 0; i < transform.childCount; i++)
        {
            crates[i] = transform.GetChild(i);
        }

        musicBoost = FindAnyObjectByType<BackgroundMusicBoost>();

        if (targetRenderers == null || targetRenderers.Length == 0)
        {
            targetRenderers = GetComponentsInChildren<Renderer>();
        }

        materials = new Material[targetRenderers.Length];
        originalColors = new Color[targetRenderers.Length];
        for (int i = 0; i < targetRenderers.Length; i++)
        {
            materials[i] = targetRenderers[i].material; // istanza dedicata: non modifica l'asset condiviso
            originalColors[i] = materials[i].color;
        }

#if UNITY_EDITOR
        // Stesso errore comune di PlayerShooting.projectilePrefab: trascinare l'oggetto
        // dalla Hierarchy invece del vero Prefab asset dalla cartella Project.
        if (projectilePrefab != null && !UnityEditor.EditorUtility.IsPersistent(projectilePrefab))
        {
            Debug.LogError($"{nameof(SoundSystem)}: '{nameof(projectilePrefab)}' punta a un'istanza nella scena, non a un Prefab asset. Trascina il prefab dalla cartella Project (Assets), non l'oggetto dalla Hierarchy.", this);
        }
#endif
    }

    void Update()
    {
        if (!isActive)
        {
            return;
        }

        activeTimer -= Time.deltaTime;
        if (activeTimer <= 0f)
        {
            isActive = false;
            return;
        }

        timer += Time.deltaTime;

        float fireInterval = 60f / Mathf.Max(bpm, 0.01f); // evita una divisione per zero (o un intervallo infinito) se bpm viene lasciato a 0
        if (timer < fireInterval)
        {
            return;
        }

        timer = 0f;
        Fire();
    }

    // Chiamato da SoundSystemAmmoPickup quando il Player raccoglie l'ammo: da qui in
    // avanti il sound system spara ad ogni battito per activeDuration secondi, poi si
    // rispegne. Raccogliendo di nuovo l'ammo mentre è già attivo, il timer si rinnova
    // (non si somma alla durata rimasta), stesso comportamento di PlayerSoundWavePowerUps.
    public void Activate()
    {
        if (isDestroyed)
        {
            return;
        }

        isActive = true;
        activeTimer = activeDuration;
        musicBoost?.Boost(activeDuration);
    }

    private void Fire()
    {
        if (projectilePrefab == null)
        {
            return;
        }

        foreach (Transform crate in crates)
        {
            // Non parentato alla cassa: le casse sono mesh di muri riadattate con scale
            // non uniforme, che deformerebbe lo spicchio visivo di SoundWaveProjectile
            // se ereditato come parent (vedi CreateVisual/UpdateVisual).
            GameObject projectile = Instantiate(projectilePrefab, crate.position, crate.rotation);
            projectile.GetComponent<SoundWaveProjectile>()?.ApplyPowerUps(bonusConeAngle, bonusMaxRadius);
        }
    }

    // Il SoundSystem si attacca SOLO da davanti, cioè dal lato verso cui spara ogni cassa
    // (transform.forward, vedi Fire): i fianchi, il retro e i varchi tra una cassa e l'altra
    // non contano. Senza questo vincolo i nemici puntavano il punto più vicino di una cassa
    // qualsiasi, spesso un fianco, e finivano infilati tra le casse o dietro l'installazione.
    //
    // Punto davanti alla faccia frontale della cassa di part (uno dei collider "muro1..muro4"),
    // all'altezza di from e a clearance di distanza dalla faccia: è lì che si ferma un nemico
    // che attacca questa cassa (vedi EnemyChase), senza toccarla.
    public Vector3 GetFrontPoint(Collider part, Vector3 from, float clearance)
    {
        Vector3 forward = GetCrateForward(part);
        Vector3 point = part.ClosestPoint(from);
        float depth = GetFrontDepth(part, forward);
        return point + forward * (depth + clearance - Vector3.Dot(point - part.bounds.center, forward));
    }

    // true se position sta davanti alla faccia frontale della cassa di part: usato da
    // EnemyChase per capire se prima di attaccare deve girare attorno alla cassa.
    public bool IsInFront(Collider part, Vector3 position)
    {
        Vector3 forward = GetCrateForward(part);
        return Vector3.Dot(position - part.bounds.center, forward) >= GetFrontDepth(part, forward);
    }

    // Punto appena fuori dallo spigolo anteriore della cassa di part, dal lato in cui si
    // trova position, a clearance di distanza dalla sagoma: tappa intermedia per chi è di
    // fianco o dietro, così gira attorno alla cassa invece di spingerci contro dal retro.
    public Vector3 GetFrontCornerPoint(Collider part, Vector3 position, float clearance)
    {
        Vector3 forward = GetCrateForward(part);
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 center = part.bounds.center;
        float side = Vector3.Dot(position - center, right) >= 0f ? 1f : -1f;
        float halfWidth = GetFrontDepth(part, right);
        return center
            + right * side * (halfWidth + clearance)
            + forward * (GetFrontDepth(part, forward) + clearance);
    }

    // Direzione "davanti" della cassa a cui appartiene part (il figlio diretto che lo
    // contiene, vedi crates), sul piano orizzontale.
    private Vector3 GetCrateForward(Collider part)
    {
        Transform crate = part.transform;
        while (crate.parent != null && crate.parent != transform)
        {
            crate = crate.parent;
        }

        Vector3 forward = crate.forward;
        forward.y = 0f;
        return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
    }

    // Semi-spessore della sagoma di part lungo direction (orizzontale): distanza dal centro
    // alla faccia su quel lato. I bounds sono allineati agli assi del mondo, ma le casse sono
    // ruotate solo attorno a Y e di multipli di 90°, quindi coincidono con la sagoma reale.
    private static float GetFrontDepth(Collider part, Vector3 direction)
    {
        Vector3 extents = part.bounds.extents;
        return Mathf.Abs(direction.x) * extents.x + Mathf.Abs(direction.z) * extents.z;
    }

    // IEnemyAttackTarget: un SoundSystem già distrutto non va più toccato, stesso discorso
    // della Console (vedi DJConsoleHealth.CanBeAttacked).
    public bool CanBeAttacked => !isDestroyed;

    public void TakeHit()
    {
        if (isDestroyed)
        {
            return;
        }

        hitsTaken++;
        PlayHitFlash();

        if (hitsTaken >= maxHits)
        {
            Die();
        }
    }

    // Salta in aria come il Player (DefeatSequence): se è fuori dallo schermo la camera prima ci
    // va, e fino ad allora resta in scena, già non più attaccabile e senza più sparare.
    private void Die()
    {
        isDestroyed = true;
        isActive = false;

        // Deco e tentacolo sono modelli a parte in scena, come il DJ per la Console.
        GameObject[] extras = new GameObject[alsoExplodeNames.Length];
        for (int i = 0; i < alsoExplodeNames.Length; i++)
        {
            extras[i] = string.IsNullOrEmpty(alsoExplodeNames[i]) ? null : GameObject.Find(alsoExplodeNames[i]);
        }
        DefeatSequence.Play(gameObject, null, extras, defeatCameraShift);
    }

    private void PlayHitFlash()
    {
        if (materials == null || materials.Length == 0)
        {
            return;
        }

        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
        }

        flashRoutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        foreach (Material m in materials)
        {
            m.color = flashColor;
        }

        yield return new WaitForSeconds(flashDuration);

        for (int i = 0; i < materials.Length; i++)
        {
            materials[i].color = originalColors[i];
        }
        flashRoutine = null;
    }
}
