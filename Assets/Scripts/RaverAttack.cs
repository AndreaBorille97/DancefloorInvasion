using System.Collections;
using UnityEngine;

// Da mettere sul prefab del Raver, insieme a RaverChase. Stesso schema di EnemyAttack
// ma al contrario: quando il Raver tocca uno Sbirro (o il Robosbirro) parte una "carica"
// di windupDuration secondi, al termine della quale infligge danno tramite IDamageable
// (implementata sia da EnemyHealth che da RobosbirroHealth).
public class RaverAttack : MonoBehaviour
{
    [Header("Attacco")]
    [SerializeField] private float damage = 25f; // danno inflitto ad ogni colpo andato a segno
    [Tooltip("Quanto dura la carica prima che il colpo vada a segno: lo Sbirro ha questo tempo per allontanarsi ed evitarlo.")]
    [SerializeField] private float windupDuration = 0.25f;
    [SerializeField] private float cooldown = 0.25f; // pausa dopo un colpo andato a segno, prima di poter caricare un nuovo attacco

    [Header("Feedback attacco")]
    [Tooltip("Stesso schema di lampo di colore usato da RobosbirroHealth/PlayerHealth quando si subisce un colpo, qui sul Raver quando lo sferra.")]
    [SerializeField] private Renderer targetRenderer; // se vuoto viene cercato su questo GameObject/figli
    [SerializeField] private Color flashColor = Color.yellow;
    [SerializeField] private float flashDuration = 0.15f; // durata del lampo di colore

    private Coroutine attackRoutine;
    private IDamageable currentTarget;
    private bool onCooldown;
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

    // Usato da RaverDrugBuff per raddoppiare/ripristinare il danno.
    public void MultiplyDamage(float factor)
    {
        damage *= factor;
    }

    // Usato da RaverJuggler quando il Raver viene mandato a fare il giocoliere: impostare
    // enabled = false da solo NON basta a impedire un colpo già in carica, perché disabilitare
    // un componente non ferma una sua coroutine già avviata con StartCoroutine. StopAllCoroutines
    // (non solo StopCoroutine(attackRoutine)) copre anche la breve coda di cooldown dopo un
    // colpo appena andato a segno, dove AttackRoutine è ancora viva ma attackRoutine è già
    // stato azzerato a null, e ferma anche un eventuale FlashRoutine ancora in corso.
    public void CancelAttack()
    {
        StopAllCoroutines();
        attackRoutine = null;
        currentTarget = null;
        onCooldown = false;
        flashRoutine = null;

        if (material != null)
        {
            material.color = originalColor;
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        TryStartAttack(collision.collider);
    }

    void OnCollisionStay(Collision collision)
    {
        TryStartAttack(collision.collider);
    }

    void OnCollisionExit(Collision collision)
    {
        IDamageable damageable = collision.collider.GetComponent<IDamageable>();
        if (damageable == null || damageable != currentTarget)
        {
            return;
        }

        StopCoroutine(attackRoutine);
        attackRoutine = null;
        currentTarget = null;
    }

    private void TryStartAttack(Collider other)
    {
        // Disabilitare il componente (vedi RaverJuggler.BeginJuggling) non basta a impedire
        // che Unity continui a chiamare OnCollisionEnter/Stay mentre resta a contatto: senza
        // questo controllo esplicito, un Raver fermo in mezzo agli Sbirro (es. il giocoliere,
        // che li attira apposta) continuerebbe comunque a caricare e infliggere colpi.
        if (!enabled)
        {
            return;
        }

        if (attackRoutine != null || onCooldown)
        {
            return;
        }

        IDamageable damageable = other.GetComponent<IDamageable>();
        if (damageable == null)
        {
            return;
        }

        currentTarget = damageable;
        attackRoutine = StartCoroutine(AttackRoutine());
    }

    private IEnumerator AttackRoutine()
    {
        yield return new WaitForSeconds(windupDuration);

        // Il bersaglio potrebbe essere stato distrutto da un'altra fonte durante la carica
        // (es. il Player) senza che OnCollisionExit facesse in tempo a girare: currentTarget
        // è un'interfaccia, non un riferimento diretto a UnityEngine.Object, quindi va
        // controllato passando per Object per rilevare la distruzione nativa.
        if ((currentTarget as Object) != null)
        {
            currentTarget.TakeDamage(damage, transform.position);
            PlayAttackFlash();
        }
        attackRoutine = null;
        currentTarget = null;

        onCooldown = true;
        yield return new WaitForSeconds(cooldown);
        onCooldown = false;
    }

    private void PlayAttackFlash()
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
