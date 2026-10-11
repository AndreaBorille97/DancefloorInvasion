using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

// Da mettere sul Player. Tenendo premuto il grilletto destro (R2/RT) spara onde sonore a tempo
// di musica, nella direzione in cui il Player è rivolto. Ogni onda consuma un po' di stamina
// (PlayerStamina): a stamina finita il grilletto non spara più finché non si raccoglie una
// scatola ammo.
public class PlayerShooting : MonoBehaviour
{
    [Header("Sparo")]
    [SerializeField] private GameObject projectilePrefab; // prefab dell'onda sonora (SoundWaveProjectile)
    [Tooltip("Battiti al minuto: tenendo premuto R2 parte un'onda ad ogni battito. Es. 172 per seguire il tempo di una traccia a 172 BPM.")]
    [FormerlySerializedAs("fireRate")]
    [SerializeField] private float bpm = 172f;
    [SerializeField] private Transform firePoint; // punto di spawn del proiettile; se vuoto usa la posizione del Player
    [Tooltip("Stamina consumata da ogni onda. Poca: con 100 di stamina e 172 BPM, 0.75 vuol dire circa 45 secondi di fuoco continuo.")]
    [SerializeField] private float staminaPerShot = 0.75f;

    private float timer;
    private PlayerStamina stamina; // se manca sul Player, si spara senza consumare niente
    private PlayerSoundWavePowerUps powerUps; // opzionale: se presente sul Player, i suoi bonus attivi vengono applicati ad ogni onda sparata

    void Awake()
    {
        stamina = GetComponent<PlayerStamina>();
        powerUps = GetComponent<PlayerSoundWavePowerUps>();

#if UNITY_EDITOR
        // Errore comune: trascinare l'oggetto dalla Hierarchy della scena invece del vero
        // Prefab asset dalla cartella Project. In quel caso projectilePrefab punta a
        // un'istanza viva che si autodistrugge da sola (ha il suo Update()), il riferimento
        // diventa "Missing" e Fire() smette di sparare dopo il primo colpo.
        if (projectilePrefab != null && !UnityEditor.EditorUtility.IsPersistent(projectilePrefab))
        {
            Debug.LogError($"{nameof(PlayerShooting)}: '{nameof(projectilePrefab)}' punta a un'istanza nella scena, non a un Prefab asset. Trascina il prefab dalla cartella Project (Assets), non l'oggetto dalla Hierarchy.", this);
        }
#endif
    }

    void Update()
    {
        float fireInterval = 60f / Mathf.Max(bpm, 0.01f); // evita una divisione per zero (o un intervallo infinito) se bpm viene lasciato a 0

        // Il timer si ferma a un intervallo pieno invece di crescere all'infinito: così, dopo
        // una pausa, la prima pressione di R2 spara subito, ma tenendolo premuto il ritmo resta
        // quello dei BPM.
        timer = Mathf.Min(timer + Time.deltaTime, fireInterval);

        Gamepad pad = Gamepad.current;
        if (pad == null || !pad.rightTrigger.isPressed || timer < fireInterval)
        {
            return;
        }

        if (stamina != null && !stamina.TrySpend(staminaPerShot))
        {
            return;
        }

        timer = 0f;
        Fire();
    }

    private void Fire()
    {
        if (projectilePrefab == null)
        {
            return;
        }

        Transform spawnPoint = firePoint != null ? firePoint : transform;
        // La rotazione del Player segue già la direzione di mira (PlayerMovement.Rotate),
        // quindi il proiettile parte semplicemente lungo il suo transform.forward.
        // Parentato a spawnPoint: finché l'onda è ancora attiva (si sta espandendo) resta
        // agganciata al Player e alla sua direzione di mira invece di restare ferma nel
        // punto in cui è stata sparata.
        GameObject projectile = Instantiate(projectilePrefab, spawnPoint.position, transform.rotation, spawnPoint);

        if (powerUps != null)
        {
            projectile.GetComponent<SoundWaveProjectile>()?.ApplyPowerUps(powerUps.AreaBonusConeAngle, powerUps.DeepBonusMaxRadius);
        }
    }
}
