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
public class SoundSystem : MonoBehaviour
{
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
}
