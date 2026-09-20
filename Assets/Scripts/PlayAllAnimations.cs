using System.Collections.Generic;
using UnityEngine;

// Prop scenografico le cui animazioni non cambiano mai: partono tutte insieme all'avvio e
// girano in loop per sempre. Niente Animator Controller, niente stati, niente transizioni -
// solo il vecchio componente Animation, che sa riprodurre piu' clip contemporaneamente.
//
// Le clip non si lanciano con Play(): quello ne tiene attiva una sola per layer. Si abilitano
// invece i singoli AnimationState a peso pieno, che e' il modo in cui l'Animation legacy
// gestisce piu' clip insieme. Funziona perche' ogni clip anima un oggetto diverso del modello
// (un faretto, l'occhio, ...): sullo stesso layer Unity fonde per-proprieta', quindi le clip
// convivono invece di sovrascriversi. Clip che animano lo stesso transform si spartirebbero il
// peso, e li' servirebbe invece fonderle a monte.
[RequireComponent(typeof(Animation))]
public class PlayAllAnimations : MonoBehaviour
{
    [Tooltip("Ogni clip parte da un punto casuale del suo ciclo: utile se ci sono piu' copie " +
             "dello stesso prop in scena, cosi' non si muovono all'unisono.")]
    public bool desincronizza;

    [Header("Velocita' selettiva")]
    [Tooltip("Le clip il cui nome contiene questo testo (case-insensitive) vengono rallentate/accelerate " +
             "con 'Moltiplicatore velocita' invece di girare a velocita' normale. Lascia vuoto per non " +
             "filtrare nessuna clip (tutte a velocita' normale).")]
    [SerializeField] private string filtroNomeClip = "Faretto";
    [Tooltip("Moltiplicatore di velocita' per le clip che matchano il filtro sopra: 1 = normale, " +
             "0.5 = meta' velocita', 2 = doppia.")]
    [SerializeField] private float moltiplicatoreVelocita = 0.6f;

    private void Start()
    {
        var anim = GetComponent<Animation>();
        anim.playAutomatically = false;
        anim.cullingType = AnimationCullingType.AlwaysAnimate;

        // I nomi vanno raccolti prima: toccare gli stati mentre si itera la collezione la invalida.
        var names = new List<string>();
        foreach (AnimationState state in anim)
        {
            if (state.clip != null && state.length > 0f) names.Add(state.name);
        }

        if (names.Count == 0)
        {
            Debug.LogWarning($"[PlayAllAnimations] {name}: nessuna clip da riprodurre. " +
                             "Controlla che il componente Animation abbia le clip e che il rig " +
                             "dell'FBX sia impostato su Legacy.", this);
            return;
        }

        anim.Play(names[0]);   // avvia il componente; le altre si aggiungono qui sotto

        foreach (string clipName in names)
        {
            AnimationState state = anim[clipName];
            bool rallentata = !string.IsNullOrEmpty(filtroNomeClip) &&
                              clipName.ToLowerInvariant().Contains(filtroNomeClip.ToLowerInvariant());

            state.wrapMode = WrapMode.Loop;
            state.blendMode = AnimationBlendMode.Blend;
            state.layer = 0;
            state.speed = rallentata ? moltiplicatoreVelocita : 1f;
            state.weight = 1f;
            state.time = desincronizza ? Random.Range(0f, state.length) : 0f;
            state.enabled = true;
        }
    }
}
