using System.Collections;
using UnityEngine;

// Da mettere sul prefab del Robosbirro, insieme a EnemyRangedAttack: il boss continua a
// sparare/inseguire/fuggire esattamente come ora (questo script non tocca in alcun modo
// EnemyRangedAttack, condiviso con Sbirro3), ma se troppi Raver lo circondano entro
// swarmRadius, il primo contatto fisico scatena un fendente: dopo windupDuration secondi
// di carica, chiunque implementi IEnemyAttackTarget entro attackRadius (Raver e/o Player,
// non solo chi lo tocca fisicamente) subisce un colpo come quello dello Sbirro1
// (EnemyAttack, stesso schema windup/cooldown), moltiplicato hitMultiplier volte (di
// default 1, cioè stesso danno di un colpo Sbirro1). IEnemyAttackTarget.TakeHit() non
// accetta un valore di danno (Player perde una vita fissa, Raver una quantità fissa):
// l'unico modo per farlo colpire più forte è applicarlo più volte in sequenza, es.
// hitMultiplier = 3 triplica il danno di un colpo normale. I Raver colpiti fanno anche un
// lampo nero (RaverHealth.PlayMeleeFlash), per distinguere questo colpo da un attacco
// Sbirro normale.
public class RobosbirroMeleeAttack : MonoBehaviour
{
    [Header("Sciame")]
    [Tooltip("Quanti Raver entro swarmRadius attivano il fendente: sotto questa soglia, toccare il Robosbirro non ha alcun effetto.")]
    [SerializeField] private int swarmThreshold = 3;
    [SerializeField] private float swarmRadius = 6f;
    [Tooltip("Ogni quanti secondi viene ricontato il numero di Raver vicini.")]
    [SerializeField] private float swarmCheckInterval = 0.5f;

    [Header("Fendente (come Sbirro1, ad area)")]
    [Tooltip("Quanto dura la carica prima che il colpo vada a segno: i bersagli hanno questo tempo per allontanarsi ed evitarlo.")]
    [SerializeField] private float windupDuration = 0.5f;
    [SerializeField] private float cooldown = 0.25f; // pausa dopo un colpo andato a segno, prima di poter caricare un nuovo fendente
    [Tooltip("Raggio del fendente al termine della carica: chiunque implementi IEnemyAttackTarget entro questa distanza dal Robosbirro viene colpito.")]
    [SerializeField] private float attackRadius = 3f;
    [Tooltip("Quante volte TakeHit() viene applicato a ciascun bersaglio in un colpo solo: 1 = stesso danno di un colpo normale di Sbirro1.")]
    [SerializeField] private int hitMultiplier = 1;

    private Coroutine attackRoutine;
    private bool onCooldown;
    private float swarmCheckTimer;
    private bool isSwarmed;

    void Update()
    {
        swarmCheckTimer += Time.deltaTime;
        if (swarmCheckTimer >= swarmCheckInterval)
        {
            swarmCheckTimer = 0f;
            isSwarmed = CountNearbyRavers() >= swarmThreshold;
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

    private void TryStartAttack(Collider other)
    {
        if (!isSwarmed || attackRoutine != null || onCooldown)
        {
            return;
        }

        if (other.GetComponentInParent<IEnemyAttackTarget>() == null)
        {
            return;
        }

        attackRoutine = StartCoroutine(AttackRoutine());
    }

    private IEnumerator AttackRoutine()
    {
        yield return new WaitForSeconds(windupDuration);

        // Fendente ad area: colpisce tutti i bersagli entro attackRadius al momento in cui
        // la carica termina, non solo chi ha toccato per primo il Robosbirro. Chi si è
        // allontanato oltre attackRadius nel frattempo lo evita, esattamente come un colpo
        // normale che si schiva allontanandosi durante il windup.
        Collider[] hits = Physics.OverlapSphere(transform.position, attackRadius);
        foreach (Collider hit in hits)
        {
            IEnemyAttackTarget target = hit.GetComponentInParent<IEnemyAttackTarget>();
            if (target == null)
            {
                continue;
            }

            for (int i = 0; i < hitMultiplier; i++)
            {
                target.TakeHit();
            }

            hit.GetComponent<RaverHealth>()?.PlayMeleeFlash();
        }

        attackRoutine = null;

        onCooldown = true;
        yield return new WaitForSeconds(cooldown);
        onCooldown = false;
    }

    private int CountNearbyRavers()
    {
        int count = 0;
        float sqrRadius = swarmRadius * swarmRadius;

        foreach (RaverHealth raver in FindObjectsByType<RaverHealth>(FindObjectsSortMode.None))
        {
            if ((raver.transform.position - transform.position).sqrMagnitude <= sqrRadius)
            {
                count++;
            }
        }

        return count;
    }
}
