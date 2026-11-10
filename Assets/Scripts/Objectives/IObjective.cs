using System.Collections.Generic;
using UnityEngine;

// Interfaccia comune dei due obiettivi da difendere (SoundSystem e DJConsoleHealth): prima
// chiunque dovesse ragionare "per obiettivo" faceva due foreach gemelli con FindObjectsByType,
// uno per tipo. Qui c'è un solo elenco (ObjectiveRegistry.All, aggiornato da OnEnable/OnDisable
// dei due componenti) e le proprietà che servono a minimappa, etichette e AI.
public interface IObjective
{
    // Soddisfatta da Component.transform: l'interfaccia è implementata solo da MonoBehaviour.
    Transform transform { get; }
    float HealthFraction { get; }
    int RemainingHits { get; }
    int MaxHits { get; }
    bool CanBeAttacked { get; }
}

public static class ObjectiveRegistry
{
    // Gli obiettivi vivi in scena, nell'ordine in cui si sono registrati. Non vengono mai
    // distrutti (alla sconfitta resta il componente con isDestroyed = true), quindi l'elenco
    // cambia solo al caricamento della scena.
    public static readonly List<IObjective> All = new List<IObjective>();

    public static void Add(IObjective objective)
    {
        if (!All.Contains(objective))
        {
            All.Add(objective);
        }
    }

    public static void Remove(IObjective objective)
    {
        All.Remove(objective);
    }

    // Con "Enter Play Mode" senza ricarica del dominio i campi statici sopravvivono tra una
    // partita e l'altra nell'Editor: si riparte sempre da lista vuota (stesso schema di
    // GameDifficulty.ResetStatics).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        All.Clear();
    }
}
