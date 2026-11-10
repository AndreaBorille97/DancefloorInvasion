using System.Linq;
using UnityEngine;

// Da mettere sul Raver che ha un modello animato come figlio (vedi Tools/Raver/Monta il modello
// animato, che lo aggiunge da solo). Sceglie quale clip far girare guardando cosa sta facendo
// il Raver, senza parametri né transizioni nel controller:
//  - pugno: quando RaverAttack inizia a caricare un colpo, e lo lascia finire;
//  - camminata: quando RaverChase lo sta portando da qualche parte (alla chillout,
//    di ritorno al punto di guardia, addosso a uno Sbirro);
//  - riposo: a terra e fermo alla chillout (RaverHealth.IsDown), dove non ha senso ballare;
//  - no con la testa: il giocoliere sul posto coi poi a terra, perché qualcuno gli è troppo
//    vicino (RaverJuggler.IsPausedForDanger);
//  - ballo: in tutti gli altri casi, cioè di guardia (anche mentre pattuglia sul posto) o
//    mentre giocola.
public class RaverAnimation : MonoBehaviour
{
    [Header("Stati del controller")]
    [SerializeField] private string danceState = "ballo";
    [SerializeField] private string walkState = "camminata";
    [SerializeField] private string punchState = "pugno";
    [Tooltip("Solo per il giocoliere: lo stato che va quando non può giocolare.")]
    [SerializeField] private string refuseState = "no_testa_bassa";
    [Tooltip("Posa da fermo a terra, alla chillout: il punto della camminata (0 = inizio, 1 = fine " +
             "del passo) su cui il modello resta bloccato. Va scelto un fotogramma con le gambe " +
             "unite; si può ritoccare in Play e si vede subito.")]
    [Range(0f, 1f)]
    [SerializeField] private float restWalkTime = 0.25f;

    private static readonly int RestStateKey = Animator.StringToHash("__riposo__");

    [Header("Tempo del ballo")]
    [Tooltip("Il ballo va alla velocità con cui è stato animato in Blender, senza adattarlo ai bpm " +
             "qui sotto (che allora non contano).")]
    [SerializeField] private bool danceAtBlenderSpeed;
    [Tooltip("Battiti al minuto a cui deve andare il ballo.")]
    [SerializeField] private float danceBpm = 175f;
    [Tooltip("Quanti battiti contiene un giro completo della clip del ballo (in Blender: quanti " +
             "molleggi fa prima di ricominciare). Se a 175 bpm sembra il doppio o la metà del " +
             "tempo della musica, è questo il numero da correggere.")]
    [SerializeField] private float danceBeatsPerLoop = 2f;

    [Tooltip("Secondi di dissolvenza fra un'animazione e l'altra.")]
    [SerializeField] private float crossFadeDuration = 0.15f;
    [Tooltip("Per quanto continua a camminare dopo l'ultimo passo: RaverChase si ferma per un attimo " +
             "quando arriva a contatto o cambia bersaglio, e senza questo margine il modello " +
             "salterebbe al ballo per un fotogramma.")]
    [SerializeField] private float walkHoldTime = 0.15f;

    [Header("Verso del modello")]
    [Tooltip("Gradi al secondo con cui il modello si gira verso dove deve guardare (vedi " +
             "GetFacingDirection): camminando dove va, col pugno verso lo Sbirro, fermo dalla parte " +
             "opposta a quella verso cui RaverChase tiene girato il Raver.")]
    [SerializeField] private float turnSpeed = 720f;
    [Tooltip("Sotto questa velocità (unità al secondo) lo spostamento non conta come direzione di marcia.")]
    [SerializeField] private float minWalkSpeedForFacing = 0.3f;
    [Tooltip("Sopra questa velocità (unità al secondo) il Raver cammina anche se RaverChase non " +
             "lo segnala. Va tenuta sopra la pattuglia lenta sul posto (patrolMoveSpeed di " +
             "RaverChase, 0.5), dove deve ballare, e sotto il passo normale (moveSpeed, 3).")]
    [SerializeField] private float walkDetectSpeed = 1.5f;

    private Animator animator;
    private Transform model;
    private Quaternion modelBaseRotation;
    private Vector3 modelBasePosition;
    // Verso del modello in gradi mondo, seguito per conto suo: quando RaverChase gira di
    // scatto il Raver il modello non scatta con lui, ma si gira alla sua velocità (turnSpeed).
    private float modelWorldYaw;
    private Vector3 lastPosition;
    // Velocità orizzontale misurata sul passo di fisica, non sul frame: vedi FixedUpdate.
    private Vector3 lastFixedPosition;
    private float physicsSpeed;
    private Transform punchTarget;
    private RaverChase raverChase;
    private RaverAttack raverAttack;
    private RaverHealth raverHealth;
    private RaverJuggler raverJuggler;
    private int currentState;
    private float punchLength = 0.5f;
    private float danceClipLength; // 0 = clip del ballo non trovata, va a velocità normale
    private float punchEndTime;
    private float lastWalkTime = float.NegativeInfinity;
    private bool wasAttacking;
    // Menu della modalità (DifficultyMenu): se c'è, il Raver balla girato verso questo punto (il
    // sound system) e basta. Il gioco è in pausa, quindi l'Animator va a tempo reale.
    private Vector3? menuDanceTarget;
    private bool menuWalking;
    private float walkSpeedMultiplier = 1f; // vedi SetWalkSpeedMultiplier
    private AnimatorUpdateMode baseUpdateMode;

    void Awake()
    {
        // L'Animator sta sul modello figlio, non su questo GameObject.
        animator = GetComponentInChildren<Animator>();
        raverChase = GetComponent<RaverChase>();
        raverAttack = GetComponent<RaverAttack>();
        raverHealth = GetComponent<RaverHealth>();
        raverJuggler = GetComponent<RaverJuggler>();

        if (animator == null || animator.runtimeAnimatorController == null)
        {
            enabled = false;
            return;
        }

        model = animator.transform;
        baseUpdateMode = animator.updateMode;
        modelBaseRotation = model.localRotation;
        modelBasePosition = model.localPosition;
        lastPosition = transform.position;
        lastFixedPosition = transform.position;
        modelWorldYaw = transform.eulerAngles.y;

        AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
        AnimationClip punchClip = clips.FirstOrDefault(clip => ShortName(clip.name) == punchState);
        if (punchClip != null)
        {
            punchLength = punchClip.length;
        }

        AnimationClip danceClip = clips.FirstOrDefault(clip => ShortName(clip.name) == danceState);
        if (danceClip != null)
        {
            danceClipLength = danceClip.length;
        }
    }

    // Davanti del modello in coordinate mondo: non è per forza quello del Raver, vedi modelWorldYaw.
    // Usato da RaverJuggler per orientare i poi di fuoco.
    public Vector3 ModelForward => Quaternion.Euler(0f, modelWorldYaw, 0f) * Vector3.forward;

    // Se il modello sta ballando: quanti giri della clip ha fatto finora (parte frazionaria
    // compresa) e quanti secondi dura un giro alla velocità attuale. Usato da RaverJuggler per
    // mettere a tempo i poi di fuoco col ballo.
    public bool TryGetDanceLoop(out float loops, out float loopLength)
    {
        loops = loopLength = 0f;
        if (!enabled || danceClipLength <= 0f || animator.speed <= 0.0001f
            || currentState != Animator.StringToHash(danceState) || animator.IsInTransition(0))
        {
            return false;
        }

        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        if (info.shortNameHash != currentState)
        {
            return false;
        }

        loops = info.normalizedTime;
        loopLength = danceClipLength / animator.speed;
        return true;
    }

    // Velocità dell'Animator che porta il ballo a danceBpm. La clip dura danceClipLength
    // secondi e contiene danceBeatsPerLoop battiti: per farla stare a danceBpm deve durare
    // danceBeatsPerLoop * 60 / danceBpm secondi. Calcolata al volo e non una volta sola, così
    // ritoccando bpm o battiti dall'Inspector in Play si sente subito.
    private float DanceSpeed => !danceAtBlenderSpeed && danceClipLength > 0f && danceBeatsPerLoop > 0f
        ? danceClipLength * danceBpm / (60f * danceBeatsPerLoop)
        : 1f;

    // Il Raver si sposta solo nei passi di fisica (MovePosition in RaverChase, 50 al secondo),
    // mentre i frame sono 60 e irregolari: diviso per il tempo del frame, lo spostamento dava
    // 0 in un frame e il doppio in quello dopo, e quando in un frame corto cadevano due passi
    // di fisica anche la pattuglia lenta superava walkDetectSpeed. Partiva la camminata per
    // walkHoldTime e si tornava al ballo: uno scatto, e su tutti i Raver nello stesso frame.
    // Misurato qui, spostamento e tempo sono sempre quelli di un passo.
    void FixedUpdate()
    {
        Vector3 moved = transform.position - lastFixedPosition;
        lastFixedPosition = transform.position;
        moved.y = 0f;
        physicsSpeed = moved.magnitude / Time.fixedDeltaTime;
    }

    // Camminata animata più veloce quando il Raver corre (DifficultyMenu, che lo manda alle
    // postazioni a passo accelerato): altrimenti i piedi scivolerebbero sul pavimento.
    public void SetWalkSpeedMultiplier(float multiplier)
    {
        walkSpeedMultiplier = multiplier;
        if (animator != null && currentState == Animator.StringToHash(walkState))
        {
            animator.speed = multiplier;
        }
    }

    // Chiamato da DifficultyMenu: lookAt = ballo verso quel punto finché dura il menu (o, con
    // walking, camminata verso quel punto: l'entrata in scena verso le postazioni), null = di
    // nuovo il Raver di sempre.
    public void SetMenuDance(Vector3? lookAt, bool walking = false)
    {
        menuDanceTarget = lookAt;
        menuWalking = walking;
        lastPosition = transform.position;
        if (animator != null)
        {
            animator.updateMode = lookAt.HasValue ? AnimatorUpdateMode.UnscaledTime : baseUpdateMode;
            if (lookAt.HasValue && !walking && enabled)
            {
                // Ognuno dal suo punto del ballo: partiti tutti insieme, si muoverebbero all'unisono.
                currentState = Animator.StringToHash(danceState);
                animator.speed = DanceSpeed;
                animator.Play(currentState, 0, Random.value);
            }
        }
    }

    void Update()
    {
        if (menuDanceTarget.HasValue)
        {
            Play(menuWalking ? walkState : danceState, false);
            animator.speed = menuWalking ? 1f : DanceSpeed;
            Vector3 toward = Flat(menuDanceTarget.Value - transform.position);
            if (toward.sqrMagnitude > 0.0001f)
            {
                modelWorldYaw = Quaternion.LookRotation(toward, Vector3.up).eulerAngles.y;
            }
            TurnModel();
            return;
        }

        bool attacking = raverAttack != null && raverAttack.enabled && raverAttack.IsAttacking;
        if (attacking && !wasAttacking)
        {
            Play(punchState, true);
            punchEndTime = Time.time + punchLength;
            punchTarget = raverAttack.CurrentTarget;
        }
        wasAttacking = attacking;

        Vector3 moved = transform.position - lastPosition;
        lastPosition = transform.position;
        moved.y = 0f;

        bool punching = Time.time < punchEndTime;
        bool walking = false;
        if (!punching)
        {
            // Cammina se RaverChase lo sta portando da qualche parte, ma anche solo se si sposta
            // abbastanza in fretta: così la camminata va comunque per i tragitti lunghi (verso la
            // chillout e ritorno), qualunque cosa lo stia muovendo.
            if ((raverChase != null && raverChase.enabled && raverChase.IsWalking) || physicsSpeed >= walkDetectSpeed)
            {
                lastWalkTime = Time.time;
            }

            walking = Time.time - lastWalkTime <= walkHoldTime;
            bool resting = !walking && raverHealth != null && raverHealth.IsDown;
            if (resting)
            {
                Rest();
            }
            else if (!walking && raverJuggler != null && raverJuggler.IsOnSpot && raverJuggler.IsPausedForDanger)
            {
                Play(refuseState, false);
            }
            else
            {
                // Dalla posa a terra la camminata riparte da capo: la posa è la camminata stessa
                // ferma su un fotogramma, e un crossfade verso lo stato in cui si è già può non
                // farla ripartire.
                bool fromRest = currentState == RestStateKey;
                Play(walking ? walkState : danceState, fromRest);
                if (!walking)
                {
                    animator.speed = DanceSpeed;
                }
            }
        }

        Vector3 facing = GetFacingDirection(punching, walking, moved);
        if (facing.sqrMagnitude > 0.0001f)
        {
            float targetYaw = Quaternion.LookRotation(facing, Vector3.up).eulerAngles.y;
            modelWorldYaw = Mathf.MoveTowardsAngle(modelWorldYaw, targetYaw, turnSpeed * Time.deltaTime);
        }
        TurnModel();
    }

    // Dove deve guardare il modello, in coordinate mondo; zero = resta com'è. Non si fida del
    // davanti del Raver: in combattimento RaverChase lo tiene rivolto verso quello che difende
    // (vedi RaverChase.FacingTowardObjective), quindi mentre carica uno Sbirro o lo colpisce il
    // suo davanti può essere proprio dalla parte opposta.
    private Vector3 GetFacingDirection(bool punching, bool walking, Vector3 moved)
    {
        if (punching)
        {
            if (punchTarget == null)
            {
                return Vector3.zero;
            }

            return Flat(punchTarget.position - transform.position);
        }

        if (walking)
        {
            // moved è lo spostamento del frame, che può essere nullo: in quel caso resta com'è.
            return physicsSpeed >= minWalkSpeedForFacing ? moved : Vector3.zero;
        }

        // Il giocoliere sul posto non difende niente: resta girato com'è, cioè come è stato
        // messo in scena (o come è arrivato camminando).
        if (raverJuggler != null && raverJuggler.IsOnSpot)
        {
            return Vector3.zero;
        }

        // Fermo (a ballare o a riposo): il ballo va fatto verso la pista, cioè di spalle a
        // quello che difende, verso cui RaverChase tiene girato il Raver.
        return -Flat(transform.forward);
    }

    private static Vector3 Flat(Vector3 direction)
    {
        direction.y = 0f;
        return direction;
    }

    // Gira il modello attorno all'asse verticale del Raver (non al proprio pivot, che sta un
    // po' spostato): così girandosi resta sulla capsula di collisione invece di scivolare via.
    private void TurnModel()
    {
        Quaternion turn = Quaternion.Euler(0f, Mathf.DeltaAngle(transform.eulerAngles.y, modelWorldYaw), 0f);
        model.localRotation = turn * modelBaseRotation;
        model.localPosition = turn * modelBasePosition;
    }

    // Posa da fermo a terra: la camminata bloccata su un fotogramma (restWalkTime). Riapplicata
    // ogni frame, così ritoccando restWalkTime dall'Inspector in Play si vede subito l'effetto.
    private void Rest()
    {
        currentState = RestStateKey;
        animator.speed = 0f;
        animator.Play(walkState, 0, Mathf.Repeat(restWalkTime, 1f));
    }

    private void Play(string state, bool restart)
    {
        int hash = Animator.StringToHash(state);
        if (hash == currentState && !restart)
        {
            return;
        }

        currentState = hash;
        // La velocità dell'Animator vale per tutte le clip: solo il ballo va accelerato.
        animator.speed = state == danceState ? DanceSpeed : state == walkState ? walkSpeedMultiplier : 1f;
        if (restart)
        {
            animator.CrossFadeInFixedTime(hash, crossFadeDuration, 0, 0f);
        }
        else
        {
            animator.CrossFadeInFixedTime(hash, crossFadeDuration);
        }
    }

    // "Armature|pugno" -> "pugno": così si chiamano le clip esportate da Blender.
    private static string ShortName(string clipName)
    {
        int bar = clipName.LastIndexOf('|');
        return bar >= 0 ? clipName.Substring(bar + 1) : clipName;
    }
}
