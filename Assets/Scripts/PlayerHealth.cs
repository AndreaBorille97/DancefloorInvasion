using System.Collections;
using UnityEngine;

// Da mettere sul Player. Il Player muore dopo maxHits colpi subiti (5 in normale, easyMaxHits in
// facile, vedi GameDifficulty), a prescindere dal danno del singolo attacco: non è un sistema a
// punti vita. La barra vita dell'HUD si accorcia in proporzione (FuelGaugeHUD.SetLife).
// Quando subisce un colpo fa lo stesso lampo rosso dei nemici (HitFlashFeedback.Flash).
public class PlayerHealth : MonoBehaviour, IEnemyAttackTarget
{
    [Header("Vita")]
    [Tooltip("Quanti colpi può subire prima di morire, in modalità normale.")]
    [SerializeField] private int maxHits = 5;
    [Tooltip("Quanti colpi può subire prima di morire, in modalità facile.")]
    [SerializeField] private int easyMaxHits = 10;

    // Questi valori valgono per tutte le sconfitte (DefeatSequence): anche Console e SoundSystem
    // saltano in aria con questa esplosione e questi suoni.
    [Header("Morte")]
    [Tooltip("Grandezza dell'esplosione alla morte, la stessa dei camion (TruckExplosion): a 1 la palla di fuoco è larga circa 5 m.")]
    [SerializeField] private float explosionScale = 1f;
    [Tooltip("MiniNukePuff.shader (particles/MiniNuke3D), come su IdroSbirro: assegnato qui finisce anche nelle build, dove Shader.Find non lo troverebbe.")]
    [SerializeField] private Shader explosionShader;
    [Tooltip("Tremolio dello schermo all'esplosione, da 0 a 1 (CameraShake).")]
    [Range(0f, 1f)]
    [SerializeField] private float explosionShake = 0.8f;
    [Tooltip("Secondi fra l'esplosione e il pannello di sconfitta (che mette in pausa il gioco): il tempo di vederla.")]
    [SerializeField] private float gameOverDelay = 1.5f;
    [Tooltip("Disco che salta (Audio/game_over): alla morte si ferma tutto l'audio in scena e parte questo, in 2D.")]
    [SerializeField] private AudioClip recordScratchClip;
    [Tooltip("Volume del disco che salta: sopra 1 lo si suona su più AudioSource sovrapposti, come i grugniti. Troppo alto può distorcere.")]
    [Range(0f, 4f)]
    [SerializeField] private float recordScratchVolume = 3f;
    [Tooltip("Grugniti (Audio/grugniti), subito dopo il disco che salta, in 2D: si sentono uguali ovunque sia la camera.")]
    [SerializeField] private AudioClip deathClip;
    [Tooltip("Volume dei grugniti: sopra 1 (il massimo di un AudioSource) li si suona su più AudioSource sovrapposti, che si sommano, come la voce di CyberGiorgia. Troppo alto può distorcere.")]
    [Range(0f, 4f)]
    [SerializeField] private float deathVolume = 3f;

    [Header("UI")]
    [SerializeField] private FuelGaugeHUD hud; // barra vita (8 segmenti): se vuoto non aggiorna nulla

    private int hitsTaken;
    private bool isDead;
    private PlayerKhole khole;

    void Awake()
    {
        // Il riferimento serializzato punta a un oggetto di scena (l'HUD non è nel prefab del
        // Player): se un rebuild del prefab l'ha perso, riagganciamo l'unico FuelGaugeHUD in scena.
        if (hud == null)
        {
            hud = FindAnyObjectByType<FuelGaugeHUD>();
        }

        // La modalità si sceglie dal menu dopo il nostro Awake (vedi DifficultyMenu).
        if (GameDifficulty.IsEasy)
        {
            maxHits = easyMaxHits;
        }
        else
        {
            GameDifficulty.Chosen += HandleDifficultyChosen;
        }

        UpdateHud();

        DefeatSequence.Current = new DefeatSequence.Settings
        {
            explosionScale = explosionScale,
            explosionShader = explosionShader,
            explosionShake = explosionShake,
            gameOverDelay = gameOverDelay,
            recordScratchClip = recordScratchClip,
            recordScratchVolume = recordScratchVolume,
            deathClip = deathClip,
            deathVolume = deathVolume,
        };

        // I due mp3 non hanno "Preload Audio Data": caricati solo al Play, il disco partirebbe
        // in ritardo e l'aggancio ai grugniti (PlayScheduled, vedi DefeatSequence) salterebbe.
        if (recordScratchClip != null)
        {
            recordScratchClip.LoadAudioData();
        }
        if (deathClip != null)
        {
            deathClip.LoadAudioData();
        }
    }

    private void HandleDifficultyChosen(GameDifficulty.Mode mode)
    {
        GameDifficulty.Chosen -= HandleDifficultyChosen;
        if (mode == GameDifficulty.Mode.Easy)
        {
            maxHits = easyMaxHits;
            UpdateHud();
        }
    }

    void OnDestroy()
    {
        GameDifficulty.Chosen -= HandleDifficultyChosen;
    }

    // IEnemyAttackTarget: un Player morto non va più toccato. Evita che gli Sbirro intorno
    // restino a caricare colpi a vuoto mentre parte la schermata di sconfitta.
    public bool CanBeAttacked => !isDead;

    public void TakeHit()
    {
        if (isDead)
        {
            return;
        }

        // Col potere khole attivo il Player è intoccabile: niente colpo da nessuna fonte
        // (corpo a corpo, proiettili, area del Robosbirro, nube). PlayerKhole si aggiunge al
        // Player a runtime, dopo il nostro Awake: lo si cerca qui.
        if (khole == null)
        {
            khole = GetComponent<PlayerKhole>();
        }

        if (khole != null && khole.IsActive)
        {
            return;
        }

        hitsTaken++;
        HitFlashFeedback.Flash(gameObject);
        UpdateHud();

        if (hitsTaken >= maxHits)
        {
            Die();
        }
    }

    // Da chiamare da un powerup vita (vedi LifeUpPowerUpAmmoPickup): annulla un colpo subito,
    // il massimo raggiungibile resta maxHits, cioè la vita con cui il Player parte.
    public void AddLife(int amount)
    {
        if (isDead)
        {
            return;
        }

        hitsTaken = Mathf.Max(0, hitsTaken - amount);
        UpdateHud();
    }

    // Esplosione, disco che salta, grugniti e game over: DefeatSequence, che poi spegne il
    // Player. Il Player è sempre inquadrato, quindi parte tutto subito.
    private void Die()
    {
        isDead = true;
        DefeatSequence.Play(gameObject);
    }

    private void UpdateHud()
    {
        if (hud != null)
        {
            hud.SetLife(maxHits - hitsTaken, maxHits);
        }
    }
}
