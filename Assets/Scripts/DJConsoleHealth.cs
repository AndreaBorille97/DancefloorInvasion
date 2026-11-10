using System.Collections;
using UnityEngine;

// Da mettere sul prefab della console/DJ in scena. Bersaglio prioritario della polizia
// (vedi EnemyChase/EnemyRangedAttack), colpita a "colpi" esattamente come il Player
// (IEnemyAttackTarget.TakeHit(), niente quantità di danno): la sua distruzione pone fine
// alla partita in sconfitta (vedi GameOverState), come la morte del Player o del SoundSystem.
public class DJConsoleHealth : MonoBehaviour, IEnemyAttackTarget
{
    [Header("Vita")]
    [SerializeField] private int maxHits = 15; // quanti colpi di Sbirro può subire prima di essere distrutta

    [Header("Feedback danno")]
    [SerializeField] private Renderer targetRenderer; // se vuoto viene cercato su questo GameObject/figli
    [SerializeField] private Color flashColor = Color.red;
    [SerializeField] private float flashDuration = 0.15f; // durata del lampo di colore

    [Header("DJ")]
    [Tooltip("Nome del GameObject del DJ in scena: sparisce insieme alla Console quando salta in aria.")]
    [SerializeField] private string djName = "DJ";

    private int hitsTaken;
    private bool isDestroyed;
    private Material material;
    private Color originalColor;
    private Coroutine flashRoutine;

    // Usato dalla minimappa (vedi MinimapUI) per mostrare quanta vita resta.
    public float HealthFraction => Mathf.Clamp01(1f - (float)hitsTaken / maxHits);
    public int RemainingHits => Mathf.Max(0, maxHits - hitsTaken);
    public int MaxHits => maxHits;

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
    }

    // IEnemyAttackTarget: una Console già distrutta non va più toccata. Nella pratica a quel
    // punto la partita è finita (Die chiama GameOverState.Trigger e distrugge il GameObject),
    // ma vale la stessa regola degli altri bersagli: chi attacca non spreca una carica su
    // qualcosa che non può più subire danno.
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
    // va, e fino ad allora la Console resta in scena, già non più attaccabile.
    private void Die()
    {
        isDestroyed = true;

        // Il DJ è un modello a parte in scena (Prefabs/models/RAVERS/dj/DJ.fbx): sparisce con la
        // Console nel momento dell'esplosione, senza spostarne il centro (e quindi l'inquadratura).
        GameObject dj = string.IsNullOrEmpty(djName) ? null : GameObject.Find(djName);
        DefeatSequence.Play(gameObject, dj != gameObject ? dj : null);
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
