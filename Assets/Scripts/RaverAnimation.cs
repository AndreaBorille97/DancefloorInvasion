using System.Linq;
using UnityEngine;

// Da mettere sul Raver che ha un modello animato come figlio (vedi Tools/Raver/Monta il modello
// animato, che lo aggiunge da solo). Sceglie quale clip far girare guardando cosa sta facendo
// il Raver, senza parametri né transizioni nel controller:
//  - pugno: quando RaverAttack inizia a caricare un colpo, e lo lascia finire;
//  - camminata: quando RaverChase lo sta portando da qualche parte (al Camper, alla chillout,
//    di ritorno al punto di guardia, addosso a uno Sbirro);
//  - ballo: in tutti gli altri casi, cioè di guardia (anche mentre pattuglia sul posto).
public class RaverAnimation : MonoBehaviour
{
    [Header("Stati del controller")]
    [SerializeField] private string danceState = "ballo";
    [SerializeField] private string walkState = "camminata";
    [SerializeField] private string punchState = "pugno";

    [Header("Tempo del ballo")]
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

    private Animator animator;
    private RaverChase raverChase;
    private RaverAttack raverAttack;
    private int currentState;
    private float punchLength = 0.5f;
    private float danceSpeed = 1f; // velocità dell'Animator che porta il ballo a danceBpm
    private float punchEndTime;
    private float lastWalkTime = float.NegativeInfinity;
    private bool wasAttacking;

    void Awake()
    {
        // L'Animator sta sul modello figlio, non su questo GameObject.
        animator = GetComponentInChildren<Animator>();
        raverChase = GetComponent<RaverChase>();
        raverAttack = GetComponent<RaverAttack>();

        if (animator == null || animator.runtimeAnimatorController == null)
        {
            enabled = false;
            return;
        }

        AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
        AnimationClip punchClip = clips.FirstOrDefault(clip => ShortName(clip.name) == punchState);
        if (punchClip != null)
        {
            punchLength = punchClip.length;
        }

        // La clip dura clip.length secondi e contiene danceBeatsPerLoop battiti: per farla
        // stare a danceBpm deve durare danceBeatsPerLoop * 60 / danceBpm secondi.
        AnimationClip danceClip = clips.FirstOrDefault(clip => ShortName(clip.name) == danceState);
        if (danceClip != null && danceClip.length > 0f && danceBeatsPerLoop > 0f)
        {
            danceSpeed = danceClip.length * danceBpm / (60f * danceBeatsPerLoop);
        }
    }

    void Update()
    {
        bool attacking = raverAttack != null && raverAttack.enabled && raverAttack.IsAttacking;
        if (attacking && !wasAttacking)
        {
            Play(punchState, true);
            punchEndTime = Time.time + punchLength;
        }
        wasAttacking = attacking;

        if (Time.time < punchEndTime)
        {
            return;
        }

        if (raverChase != null && raverChase.enabled && raverChase.IsWalking)
        {
            lastWalkTime = Time.time;
        }

        bool walking = Time.time - lastWalkTime <= walkHoldTime;
        Play(walking ? walkState : danceState, false);
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
        animator.speed = state == danceState ? danceSpeed : 1f;
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
