using UnityEngine;
using UnityEngine.InputSystem;

// Da mettere sul Player, stesso schema di PlayerCamperSummon: nessuna munizione da
// raccogliere, in qualsiasi momento della partita premendo il paraurti sinistro (L1/LB) del
// gamepad manda a fare il giocoliere UN SOLO Raver, quello chiamato "GIOCOLIERE" in scena
// (vedi jugglerName/FindJuggler): gli altri Raver ignorano il richiamo, qualunque sia la loro
// posizione. Se quel Raver non c'è, è a terra (vita a zero), sta guidando il Camper o è già
// dirottato altrove, premere il tasto non fa nulla. Va a esibirsi nel punto in cui il
// Player sta mirando, a throwDistance unità davanti a sé (stessa logica di PlayerShooting:
// transform.forward segue già la direzione di mira, vedi PlayerMovement.Rotate). Se è già
// impegnato, premere di nuovo lo sposta semplicemente nel nuovo punto mirato (vedi
// RaverJuggler.Reposition). Se va a terra mentre si esibisce l'esibizione finisce lì e il
// richiamo resta inutilizzabile finché il Player non lo rianima col cono: non esiste un
// secondo Raver a cui ripiegare.
public class PlayerJugglerSummon : MonoBehaviour
{
    [Header("Lancio")]
    [SerializeField] private float throwDistance = 6f; // N: distanza davanti al Player a cui atterra il power-up

    [Header("Giocoliere")]
    [Tooltip("Nome del GameObject del Raver, in scena, che è l'unico a poter fare il giocoliere: tutti gli altri Raver ignorano il richiamo. Il confronto ignora maiuscole e minuscole.")]
    [SerializeField] private string jugglerName = "GIOCOLIERE";

    void Update()
    {
        Gamepad pad = Gamepad.current;
        if (pad == null)
        {
            return;
        }

        // wasPressedThisFrame: scatta una sola volta alla pressione, non ripete finché il tasto resta premuto.
        if (pad.leftShoulder.wasPressedThisFrame)
        {
            TrySendJuggler();
        }
    }

    private void TrySendJuggler()
    {
        Vector3 landingPosition = ClampToFloor(transform.position + transform.forward * throwDistance);

        if (RaverJuggler.AnyJuggling)
        {
            RaverJuggler.Active.Reposition(landingPosition); // già impegnato altrove: lo sposto invece di sceglierne un altro
            return;
        }

        RaverHealth raver = FindJuggler();
        if (raver == null)
        {
            return; // il giocoliere non c'è, è a terra o è impegnato altrove
        }

        raver.GetComponent<RaverJuggler>()?.BeginJuggling(landingPosition);
    }

    // L'unico Raver abilitato a fare il giocoliere, cioè quello il cui GameObject si chiama
    // jugglerName: non è più "il più vicino di turno", quindi non c'è nessuna scelta da fare
    // tra più candidati. Ritorna null (e il richiamo non fa nulla) se quel Raver non esiste in
    // scena, oppure se è indisponibile per uno di questi motivi:
    // - è a terra (vita a zero, vedi RaverHealth.IsDown): non può esibirsi finché non viene
    //   rianimato a vita piena;
    // - sta guidando il Camper (IsInvulnerable) o è già dirottato altrove (RaverChase.IsForced):
    //   BeginJuggling gli sovrascriverebbe silenziosamente la destinazione, lasciando il Camper
    //   con un pilota "fantasma" che non arriva mai (vedi CamperVehicle.isClaimed, mai più
    //   liberato).
    private RaverHealth FindJuggler()
    {
        foreach (RaverHealth raver in FindObjectsByType<RaverHealth>(FindObjectsSortMode.None))
        {
            if (!string.Equals(raver.name, jugglerName, System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (raver.IsDown || raver.IsInvulnerable)
            {
                return null;
            }

            RaverChase chase = raver.GetComponent<RaverChase>();
            if (chase != null && chase.IsForced)
            {
                return null;
            }

            return raver;
        }

        return null;
    }

    // Stesso schema di ClampToFloor usato da EnemyChase/EnemyRangedAttack: senza, mirando
    // verso il bordo della mappa il punto di lancio finirebbe fuori dai bounds del pavimento.
    private static Vector3 ClampToFloor(Vector3 position)
    {
        if (FloorBounds.Instance == null)
        {
            return position;
        }

        Bounds bounds = FloorBounds.Instance.Bounds;
        position.x = Mathf.Clamp(position.x, bounds.min.x, bounds.max.x);
        position.z = Mathf.Clamp(position.z, bounds.min.z, bounds.max.z);
        return position;
    }
}
