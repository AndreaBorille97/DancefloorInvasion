using System.Collections;
using UnityEngine;

// Da mettere sul prefab del nemico, insieme a EnemyChase.
// Quando il nemico tocca un bersaglio attaccabile (Player o Raver, vedi
// IEnemyAttackTarget) parte una "carica" di un secondo: se il bersaglio resta a
// contatto per tutta la carica subisce il colpo, se si allontana prima che scada lo
// evita. Gestito interamente a livello di collider fisico (OnCollisionEnter/Stay/Exit)
// grazie ai Rigidbody già presenti sui GameObject coinvolti, niente OverlapSphere o
// controlli a distanza.
public class EnemyAttack : MonoBehaviour
{
    [Header("Attacco")]
    [Tooltip("Quanto dura la carica prima che il colpo vada a segno: il bersaglio ha questo tempo per allontanarsi ed evitarlo.")]
    [SerializeField] private float windupDuration = 1f;
    [SerializeField] private float cooldown = 1f; // pausa dopo un colpo andato a segno, prima di poter caricare un nuovo attacco

    private Coroutine attackRoutine;
    private IEnemyAttackTarget currentTarget;
    private bool onCooldown;

    void OnCollisionEnter(Collision collision)
    {
        TryStartAttack(collision.collider);
    }

    void OnCollisionStay(Collision collision)
    {
        // Copre il caso in cui nemico e bersaglio restano a contatto senza mai separarsi:
        // appena finisce il cooldown del colpo precedente, ne parte subito uno nuovo.
        TryStartAttack(collision.collider);
    }

    void OnCollisionExit(Collision collision)
    {
        IEnemyAttackTarget target = collision.collider.GetComponent<IEnemyAttackTarget>();
        if (target == null || target != currentTarget)
        {
            return;
        }

        // Il bersaglio si è allontanato durante la carica: colpo evitato, nessun danno.
        StopCoroutine(attackRoutine);
        attackRoutine = null;
        currentTarget = null;
    }

    private void TryStartAttack(Collider other)
    {
        if (attackRoutine != null || onCooldown)
        {
            return; // carica già in corso, o in pausa dopo l'ultimo colpo
        }

        IEnemyAttackTarget target = other.GetComponent<IEnemyAttackTarget>();
        if (target == null)
        {
            return;
        }

        currentTarget = target;
        attackRoutine = StartCoroutine(AttackRoutine());
    }

    private IEnumerator AttackRoutine()
    {
        yield return new WaitForSeconds(windupDuration);

        currentTarget.TakeHit();
        attackRoutine = null;
        currentTarget = null;

        onCooldown = true;
        yield return new WaitForSeconds(cooldown);
        onCooldown = false;
    }
}
