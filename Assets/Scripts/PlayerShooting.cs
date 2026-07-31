using UnityEngine;
using UnityEngine.Serialization;

// Da mettere sul Player. Spara automaticamente onde sonore a intervalli
// regolari, nella direzione in cui il Player è rivolto (nessun input richiesto).
public class PlayerShooting : MonoBehaviour
{
    [Header("Sparo")]
    [SerializeField] private GameObject projectilePrefab; // prefab dell'onda sonora (SoundWaveProjectile)
    [Tooltip("Battiti al minuto: un'onda sparata ad ogni battito. Es. 172 per seguire il tempo di una traccia a 172 BPM.")]
    [FormerlySerializedAs("fireRate")]
    [SerializeField] private float bpm = 172f;
    [SerializeField] private Transform firePoint; // punto di spawn del proiettile; se vuoto usa la posizione del Player

    private float timer;
    private PlayerSoundWavePowerUps powerUps; // opzionale: se presente sul Player, i suoi bonus attivi vengono applicati ad ogni onda sparata

    void Awake()
    {
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
        timer += Time.deltaTime;

        float fireInterval = 60f / Mathf.Max(bpm, 0.01f); // evita una divisione per zero (o un intervallo infinito) se bpm viene lasciato a 0
        if (timer < fireInterval)
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
