using System.Collections;
using UnityEngine;

// Da mettere sul prefab del Robosbirro, il boss unico spawnato una sola volta da
// EnemySpawner (vedi robosbirroPrefab/robosbirroSpawnTime). Niente EnemyHealth su questo
// prefab: è la scelta che lo rende immune al Player, dato che SoundWaveProjectile cerca
// solo EnemyHealth sui bersagli colpiti. Può essere danneggiato solo da due fonti:
// - Raver (RaverAttack), tramite IDamageable.TakeDamage, come un colpo normale;
// - Camper (CamperVehicle, mentre un Raver lo pilota), che toglie il quadruplo del danno
//   di un colpo di Raver normale, vedi TakeCamperHit(). Stesso schema anche per gli Sbirro
//   normali (EnemyHealth.TakeCamperHit): il Camper non fa più insta-kill su nessuno dei due.
public class RobosbirroHealth : MonoBehaviour, IDamageable
{
    [Header("Vita")]
    [SerializeField] private float maxHealth = 2500; // punti vita massimi del boss (indicativamente 10x uno Sbirro normale)
    [Tooltip("Danno tolto da un colpo di Camper: il quadruplo di un colpo di Raver normale (RaverAttack.damage = 25), non più una frazione della vita massima, altrimenti è troppo forte contro il boss.")]
    [SerializeField] private float camperDamage = 100f;

    [Header("Vittoria")]
    [Tooltip("Pannello UI mostrato alla morte del boss; se vuoto il gioco si limita a mettersi in pausa.")]
    [SerializeField] private GameObject victoryPanel;

    [Header("Feedback danno")]
    [Tooltip("Stesso lampo di colore usato da PlayerHealth quando il Player subisce un colpo.")]
    [SerializeField] private Renderer targetRenderer; // se vuoto viene cercato su questo GameObject/figli
    [SerializeField] private Color flashColor = Color.red;
    [SerializeField] private float flashDuration = 0.15f; // durata del lampo di colore

    private float currentHealth;
    private Material material;
    private Color originalColor;
    private Coroutine flashRoutine;

    void Awake()
    {
        currentHealth = maxHealth;

        if (targetRenderer == null)
        {
            targetRenderer = GetComponentInChildren<Renderer>();
        }

        if (targetRenderer != null)
        {
            material = targetRenderer.material; // istanza dedicata: non modifica l'asset condiviso
            originalColor = material.color;
        }
    }

    public void TakeDamage(float amount)
    {
        currentHealth -= amount;
        PlayHitFlash();

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    // Chiamato da CamperVehicle quando lo tocca da guidato.
    public void TakeCamperHit()
    {
        TakeDamage(camperDamage);
    }

    private void Die()
    {
        Destroy(gameObject);

        // Da qui in poi gli Sbirro rimasti scappano invece di attaccare e non ne spawnano
        // più (vedi RoutState, controllato da EnemyChase/EnemyRangedAttack/EnemySpawner).
        // La vittoria (pausa + pannello) scatta solo quando l'ultimo sparisce: quell'attesa
        // va affidata a un oggetto che sopravvive alla distruzione di questo GameObject.
        RoutState.Activate();

        GameObject sequenceObject = new GameObject("BossDefeatedSequence");
        sequenceObject.AddComponent<BossDefeatedSequence>().Init(victoryPanel);
    }

    private void PlayHitFlash()
    {
        if (material == null)
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
        material.color = flashColor;
        yield return new WaitForSeconds(flashDuration);
        material.color = originalColor;
        flashRoutine = null;
    }
}
