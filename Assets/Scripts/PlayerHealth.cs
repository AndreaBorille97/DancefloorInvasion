using System.Collections;
using UnityEngine;

// Da mettere sul Player. Il Player muore dopo maxHits colpi subiti (8, uno per ogni segmento
// della barra vita nell'HUD), a prescindere dal danno del singolo attacco: non è un sistema a
// punti vita. Mostra anche un rapido lampo di colore quando subisce un colpo: non c'è ancora
// un'animazione di hit, questo basta a farlo percepire finché non verrà sostituito da quella.
public class PlayerHealth : MonoBehaviour, IEnemyAttackTarget
{
    [Header("Vita")]
    [Tooltip("Quanti colpi può subire prima di morire. Tienilo uguale al numero di segmenti della barra vita nell'HUD (8): così ogni colpo ne spegne esattamente uno.")]
    [SerializeField] private int maxHits = 8;

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

        // Il riferimento serializzato punta a un oggetto di scena (l'HUD non è nel prefab del
        // Player): se un rebuild del prefab l'ha perso, riagganciamo l'unico FuelGaugeHUD in scena.
        if (hud == null)
        {
            hud = FindAnyObjectByType<FuelGaugeHUD>();
        }

        UpdateHud();
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
        GameOverState.Trigger();
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
