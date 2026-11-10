using System.Collections;
using UnityEngine;

// Da mettere sul prefab del nemico, insieme a EnemyChase.
// Quando il nemico tocca un bersaglio attaccabile (Player, Raver, Console/DJ o
// SoundSystem, vedi IEnemyAttackTarget) parte una "carica" di un secondo: se il
// bersaglio resta a contatto per tutta la carica subisce il colpo, se si allontana
// prima che scada lo evita. Gestito interamente a livello di collider fisico
// (OnCollisionEnter/Stay/Exit) grazie ai Rigidbody già presenti sui GameObject
// coinvolti, niente OverlapSphere o controlli a distanza. Cerca IEnemyAttackTarget
// anche nei genitori del collider colpito (GetComponentInParent), non solo sull'oggetto
// esatto: il SoundSystem ad esempio ha il collider sulle casse figlie ("muro1..muro4"),
// non sul GameObject radice dove sta lo script.
//
// I bersagli già fuori gioco vengono saltati (IEnemyAttackTarget.CanBeAttacked): in primo
// luogo i Raver sconfitti, che restano solidi e in mezzo ai piedi mentre camminano verso la
// ChilloutZone. Il controllo si ripete a fine carica, perché nel frattempo possono cadere.
//
// Eccezione: Console/DJ e SoundSystem non si attaccano a contatto ma a distanza, dal punto
// di attacco davanti all'obiettivo in cui si ferma EnemyChase (vedi EnemyChase.IsAtObjective).
// Il contatto fisico con loro viene ignorato: altrimenti i nemici dovrebbero schiacciarsi
// contro le casse per colpirle, finendo per compenetrarle o infilarsi dietro la barriera.
public class EnemyAttack : MonoBehaviour
{
    [Header("Attacco")]
    [Tooltip("Quanto dura la carica prima che il colpo vada a segno: il bersaglio ha questo tempo per allontanarsi ed evitarlo.")]
    [SerializeField] private float windupDuration = 1f;
    [SerializeField] private float cooldown = 1f; // pausa dopo un colpo andato a segno, prima di poter caricare un nuovo attacco
    [Tooltip("Contro Console e SoundSystem la pausa fra un colpo e l'altro è cooldown per questo valore (2 = il doppio). Contro Raver e Player resta cooldown.")]
    [SerializeField] private float objectiveCooldownMultiplier = 2f;

    private Coroutine attackRoutine;
    private IEnemyAttackTarget currentTarget;
    private bool onCooldown;
    private Animator animator;
    private EnemyChase chase;
    // true se la carica in corso è contro Console/SoundSystem (partita dalla distanza, vedi
    // FixedUpdate): non va interrotta da OnCollisionExit, solo dall'allontanarsi dal punto di attacco.
    private bool attackingObjective;

    // true finché l'attacco è in corso: letto da EnemyChase, che nel frattempo tiene il nemico
    // fermo e non gli fa cambiare bersaglio. Conta l'animazione, non solo la carica: lo stato di
    // attacco dell'Animator (tag "Attack", vedi i controller Sbirro*New_Controller) dura più
    // della carica e prosegue anche quando questa viene annullata perché il bersaglio si è
    // staccato, e un nemico che scivola via o si gira a metà colpo sembrava cambiare idea.
    // La carica resta nella condizione per il primo frame dopo SetTrigger, quando l'Animator
    // non è ancora entrato nello stato.
    public bool IsAttacking => attackRoutine != null || IsAttackAnimationPlaying();

    private bool IsAttackAnimationPlaying()
    {
        if (animator == null || !animator.isActiveAndEnabled)
        {
            return false;
        }

        return animator.GetCurrentAnimatorStateInfo(0).IsTag("Attack")
            || (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsTag("Attack"));
    }

    void Awake()
    {
        // L'Animator sta sul modello figlio (es. RiotCop_Unity), non su questo GameObject.
        animator = GetComponentInChildren<Animator>();
        chase = GetComponent<EnemyChase>();
    }

    void FixedUpdate()
    {
        if (attackRoutine != null || onCooldown || chase == null || !chase.IsAtObjective || chase.Target == null)
        {
            return;
        }

        IEnemyAttackTarget objective = chase.Target.GetComponent<IEnemyAttackTarget>();
        if (objective == null || !objective.CanBeAttacked)
        {
            return;
        }

        attackingObjective = true;
        currentTarget = objective;
        attackRoutine = StartCoroutine(AttackRoutine());
    }

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
        IEnemyAttackTarget target = collision.collider.GetComponentInParent<IEnemyAttackTarget>();
        if (attackingObjective || target == null || target != currentTarget)
        {
            return;
        }

        // Il bersaglio si è allontanato durante la carica: colpo evitato, nessun danno.
        StopCoroutine(attackRoutine);
        attackRoutine = null;
        currentTarget = null;
    }

    // Spento da SerotoninBrawl mentre lo Sbirro è in rissa: una carica già partita non deve
    // arrivare a segno (le coroutine non si fermano da sole quando il componente si spegne), e
    // il prossimo attacco, a componente riacceso, deve poter partire subito.
    void OnDisable()
    {
        StopAllCoroutines();
        attackRoutine = null;
        currentTarget = null;
        onCooldown = false;
    }

    private void TryStartAttack(Collider other)
    {
        // Come RaverAttack: Unity chiama OnCollisionEnter/Stay anche sui componenti spenti.
        // A fine partita (RoutState) chi scappa verso lo spawner non picchia più nessuno.
        if (!enabled || RoutState.IsActive)
        {
            return;
        }

        if (attackRoutine != null || onCooldown)
        {
            return; // carica già in corso, o in pausa dopo l'ultimo colpo
        }

        IEnemyAttackTarget target = other.GetComponentInParent<IEnemyAttackTarget>();
        if (target == null || !target.CanBeAttacked)
        {
            // CanBeAttacked: un Raver sconfitto (che sta andando in chillout o è già fermo lì)
            // non va toccato. Il suo collider resta attivo apposta, quindi senza questo
            // controllo ogni urto lungo il tragitto faceva partire un attacco completo — a
            // vuoto, perché RaverHealth.TakeDamage scarta il danno, ma con tanto di animazione
            // e soprattutto con questo Sbirro occupato per tutta la carica più il cooldown.
            return;
        }

        // Console e SoundSystem si attaccano solo a distanza (vedi FixedUpdate), mai a contatto.
        if (target is SoundSystem || target is DJConsoleHealth)
        {
            return;
        }

        attackingObjective = false;
        currentTarget = target;
        attackRoutine = StartCoroutine(AttackRoutine());
    }

    private IEnumerator AttackRoutine()
    {
        if (animator != null)
        {
            animator.SetTrigger("Attack");
        }

        yield return new WaitForSeconds(windupDuration);

        // Ricontrollato qui e non solo alla partenza: durante la carica il bersaglio può essere
        // stato messo fuori gioco da qualcun altro (tipico con più Sbirro addosso allo stesso
        // Raver). In quel caso il colpo non parte e non scatta nemmeno il cooldown: questo
        // Sbirro è libero subito di prendersela con qualcun altro, invece di restare fermo mezzo
        // secondo a guardare un Raver che se ne sta andando in chillout.
        // Contro un obiettivo il colpo va a segno solo se il nemico è ancora al suo punto di
        // attacco: spinto via, o passato ad altro bersaglio (es. il Player in aggro), lo manca.
        bool leftObjective = attackingObjective && (chase == null || !chase.IsAtObjective);
        if (!currentTarget.CanBeAttacked || leftObjective)
        {
            attackRoutine = null;
            currentTarget = null;
            yield break;
        }

        currentTarget.TakeHit();
        attackRoutine = null;
        currentTarget = null;

        onCooldown = true;
        yield return new WaitForSeconds(attackingObjective ? cooldown * objectiveCooldownMultiplier : cooldown);
        onCooldown = false;
    }
}
