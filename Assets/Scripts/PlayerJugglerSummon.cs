using UnityEngine;

// Da mettere sul Player. Il giocoliere non si comanda: è UN SOLO Raver, quello chiamato
// "GIOCOLIERE" in scena (vedi jugglerName/FindJuggler), e giocola sempre, dal primo istante della
// partita, nel punto in cui è stato messo in scena. Qui c'è solo il via iniziale: il resto lo fa
// RaverJuggler da sé. Se va a terra va in chillout come qualunque Raver, e quando il Player lo
// rianima col cono torna da solo al suo punto a riprendere l'esibizione (vedi
// RaverJuggler.HandleRevived).
public class PlayerJugglerSummon : MonoBehaviour
{
    [Header("Giocoliere")]
    [Tooltip("Nome del GameObject del Raver, in scena, che fa il giocoliere. Il confronto ignora maiuscole e minuscole.")]
    [SerializeField] private string jugglerName = "GIOCOLIERE";

    // A inizio partita, non appena tutti gli Awake sono passati: il giocoliere sta già
    // giocolando. In Start e non in Awake perché RaverJuggler si aggancia a RaverHealth nel
    // proprio Awake, e va lasciato finire prima di chiamarlo.
    void Start()
    {
        RaverHealth raver = FindJuggler();
        if (raver == null)
        {
            Debug.LogWarning(
                $"{nameof(PlayerJugglerSummon)}: in scena non c'è nessun Raver che si chiami " +
                $"'{jugglerName}'. Controlla il nome del GameObject.", this);
            return;
        }

        // Si esibisce dov'è già: la sua posizione in scena è una scelta fatta a mano, e qui non
        // si tocca. Passargli la posizione in cui si trova significa che è già arrivato, quindi
        // il primo Update di RaverJuggler fa partire subito l'esibizione senza nessun tragitto.
        // È anche il punto a cui tornerà ogni volta che viene rianimato.
        raver.GetComponent<RaverJuggler>()?.BeginJuggling(raver.transform.position);
    }

    private RaverHealth FindJuggler()
    {
        foreach (RaverHealth raver in FindObjectsByType<RaverHealth>(FindObjectsSortMode.None))
        {
            if (string.Equals(raver.name, jugglerName, System.StringComparison.OrdinalIgnoreCase))
            {
                return raver;
            }
        }

        return null;
    }
}
