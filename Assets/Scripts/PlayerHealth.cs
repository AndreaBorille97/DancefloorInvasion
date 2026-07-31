using System.Collections;
using UnityEngine;

// Da mettere sul Player. Il Player muore al terzo colpo subito, a prescindere dal
// danno del singolo attacco (non è un sistema a punti vita). Mostra anche un rapido
// lampo di colore quando subisce un colpo: non c'è ancora un'animazione di hit,
// questo basta a farlo percepire finché non verrà sostituito da quella vera.
public class PlayerHealth : MonoBehaviour, IEnemyAttackTarget
{
    [Header("Vita")]
    [SerializeField] private int maxHits = 3; // quanti colpi può subire prima di morire

    [Header("Feedback danno")]
    [SerializeField] private Renderer targetRenderer; // se vuoto viene cercato su questo GameObject/figli
    [SerializeField] private Color flashColor = Color.red;
    [SerializeField] private float flashDuration = 0.15f; // durata del lampo di colore

    [Header("UI")]
    [SerializeField] private FuelGaugeHUD hud; // barra vita (8 segmenti): se vuoto non aggiorna nulla

    private int hitsTaken;
    private bool isDead;
    private Material material;
    private Color originalColor;
    private Coroutine flashRoutine;

    void Awake()
    {
        if (targetRenderer == null)
        {
            targetRenderer = GetComponentInChildren<Renderer>();
        }

        if (targetRenderer != null)
        {
            material = targetRenderer.material; // istanza dedicata: non modifica l'asset condiviso
            originalColor = material.color;
        }

        UpdateHud();
    }

    public void TakeHit()
    {
        if (isDead)
        {
            return;
        }

        hitsTaken++;
        PlayHitFlash();
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

    private void Die()
    {
        isDead = true;
        Destroy(gameObject);
    }

    private void UpdateHud()
    {
        if (hud != null)
        {
            hud.SetLife(maxHits - hitsTaken, maxHits);
        }
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
