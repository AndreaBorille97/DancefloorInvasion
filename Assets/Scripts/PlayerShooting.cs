using UnityEngine;
using UnityEngine.Serialization;

// Da mettere sul Player. Spara da solo, sempre, un'onda sonora ad ogni battito di musica, nella
// direzione in cui il Player è rivolto. Niente grilletto e niente risorse da consumare: si pensa
// solo a muoversi e a mirare.
public class PlayerShooting : MonoBehaviour
{
    [Header("Sparo")]
    [SerializeField] private GameObject projectilePrefab; // prefab dell'onda sonora (SoundWaveProjectile)
    [Tooltip("Battiti al minuto: parte un'onda ad ogni battito. Es. 172 per seguire il tempo di una traccia a 172 BPM.")]
    [FormerlySerializedAs("fireRate")]
    [SerializeField] private float bpm = 172f;
    [SerializeField] private Transform firePoint; // punto di spawn del proiettile; se vuoto usa la posizione del Player
    [Tooltip("Casse del modello del Player: i due anelli di ogni onda partono da qui. Se vuote si cercano Muzzle_L e Muzzle_R nel modello (PLAYER.fbx); se non ci sono, gli anelli partono attorno al punto di sparo come prima.")]
    [SerializeField] private Transform leftSpeaker;
    [SerializeField] private Transform rightSpeaker;

    private float timer;
    private PlayerSoundWavePowerUps powerUps; // opzionale: se presente sul Player, i suoi bonus attivi vengono applicati ad ogni onda sparata
    private PlayerBerserk berserk; // a modalità attiva le onde diventano berserk (vedi SpawnWave)
    private PlayerSerotonin serotonin; // a potere attivo le onde diventano serotonina (vedi SpawnWave)

    void Awake()
    {
        powerUps = GetComponent<PlayerSoundWavePowerUps>();
        if (leftSpeaker == null || rightSpeaker == null)
        {
            leftSpeaker = FindChild("Muzzle_L");
            rightSpeaker = FindChild("Muzzle_R");
        }

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

        timer += Time.deltaTime;
        if (timer < fireInterval)
        {
            return;
        }

        timer -= fireInterval; // tiene il ritmo esatto dei BPM anche se un frame arriva in ritardo
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
        // Non è figlio del Player: gli anelli dell'onda volano dritti dal punto di sparo, e la
        // zona che fa danno (calcolata dal transform dell'onda) deve restare insieme a loro.
        // PlayerBerserk si aggiunge al Player a runtime, dopo il nostro Awake:
        // lo si cerca qui.
        if (berserk == null)
        {
            berserk = GetComponent<PlayerBerserk>();
        }
        if (serotonin == null)
        {
            serotonin = GetComponent<PlayerSerotonin>();
        }

        SpawnWave(spawnPoint.position, transform.rotation);
        if (berserk != null && berserk.IsActive)
        {
            berserk.OnShot(); // impulso dell'alone rosso ad ogni onda
        }
        if (serotonin != null && serotonin.IsActive)
        {
            serotonin.OnShot(spawnPoint); // fuochi d'artificio e cuoricini (CozyBeatFX)
        }
    }

    private SoundWaveProjectile SpawnWave(Vector3 position, Quaternion rotation)
    {
        GameObject projectile = Instantiate(projectilePrefab, position, rotation);
        SoundWaveProjectile wave = projectile.GetComponent<SoundWaveProjectile>();

        // Il busto del modello ruota verso la mira (TwinStickLocomotion), quindi le casse
        // guardano già dove va l'onda: gli anelli partono da loro.
        if (leftSpeaker != null && rightSpeaker != null)
        {
            wave?.LaunchFromSpeakers(leftSpeaker.position, rightSpeaker.position);
        }

        if (powerUps != null)
        {
            wave?.ApplyPowerUps(powerUps.AreaBonusConeAngle, powerUps.DeepBonusMaxRadius);
        }

        if (berserk != null && berserk.IsActive)
        {
            wave?.MakeBerserk(berserk.DamageMultiplier);
        }

        if (serotonin != null && serotonin.IsActive)
        {
            wave?.MakeSerotonin();
        }

        return wave;
    }

    private Transform FindChild(string childName)
    {
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child.name == childName)
            {
                return child;
            }
        }
        return null;
    }
}
