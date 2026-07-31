using System.Collections;
using UnityEngine;

// Da mettere sul prefab della console/DJ in scena. Bersaglio prioritario della polizia
// (vedi EnemyChase/EnemyRangedAttack), colpita a "colpi" esattamente come il Player
// (IEnemyAttackTarget.TakeHit(), niente quantità di danno): la sua distruzione pone fine
// alla partita in sconfitta (vedi GameOverState), come la morte del Player o del SoundSystem.
public class DJConsoleHealth : MonoBehaviour, IEnemyAttackTarget
{
    [Header("Vita")]
    [SerializeField] private int maxHits = 10; // quanti colpi di Sbirro può subire prima di essere distrutta

    [Header("Feedback danno")]
    [SerializeField] private Renderer targetRenderer; // se vuoto viene cercato su questo GameObject/figli
    [SerializeField] private Color flashColor = Color.red;
    [SerializeField] private float flashDuration = 0.15f; // durata del lampo di colore

    private int hitsTaken;
    private bool isDestroyed;
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
    }

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

    private void Die()
    {
        isDestroyed = true;
        GameOverState.Trigger();
        Destroy(gameObject);
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
