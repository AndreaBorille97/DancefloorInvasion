using UnityEngine;
using UnityEngine.InputSystem;

// Premendo L1 (spalla sinistra del gamepad) lancia un buco nero davanti al Player. Il buco nero
// (BlackHole, cartella "Buco nero parametrico Unity") ha già le sue animazioni di nascita e di morte
// scritte nello script: qui si decide solo quando nasce e quanto vive. Resta attivo aliveSeconds
// secondi dopo la nascita, poi muore con la sua animazione e si distrugge.
//
// I lanci si guadagnano raccogliendo le ammo Hoffman (HoffmanAmmoPickup): ogni raccolta mette un
// buco nero in tasca, ogni L1 ne spende uno. A tasca vuota L1 non fa niente.
//
// Si aggiunge da solo al Player all'avvio (vedi AttachToPlayer): non serve metterlo a mano in scena.
// Se vuoi un buco nero con parametri tuoi, crea un prefab con il componente BlackHole e assegnalo a
// blackHolePrefab su questo componente (a mano sul Player): altrimenti ne viene creato uno con i
// valori di default dello script.
public class PlayerBlackHoleThrow : MonoBehaviour
{
    [Header("Lancio")]
    [Tooltip("Buchi neri in tasca a inizio partita. Gli altri si guadagnano raccogliendo le ammo Hoffman.")]
    [SerializeField] private int startingCharges = 0;
    [Tooltip("Prefab con il componente BlackHole. Se vuoto ne viene creato uno a runtime coi valori di default.")]
    [SerializeField] private BlackHole blackHolePrefab;
    [Tooltip("A che distanza davanti al Player compare.")]
    [SerializeField] private float throwDistance = 8f;
    [Tooltip("Altezza rispetto ai piedi del Player.")]
    [SerializeField] private float heightOffset = 1.5f;

    [Header("Aspetto (solo se non c'è un prefab)")]
    [Tooltip("Raggio dell'orizzonte degli eventi, cioè la dimensione del buco nero.")]
    [SerializeField] private float eventHorizonRadius = 0.7f;
    [Tooltip("Raggio dell'effetto psichedelico/lente, in raggi d'orizzonte. L'effetto visibile arriva un po' oltre il disco (diskSize = 5 di default): più basso = effetto più piccolo.")]
    [SerializeField] private float lensSize = 30f;
    [Tooltip("Forza della deformazione gravitazionale della lente.")]
    [Range(0f, 2f)] [SerializeField] private float lensStrength = 0.2f;
    [Tooltip("Intensità dell'effetto psichedelico.")]
    [Range(0f, 1f)] [SerializeField] private float psyche = 1f;

    [Header("Durata")]
    [Tooltip("Secondi in cui resta attivo, dopo l'animazione di nascita e prima di quella di morte. 0 = infinito.")]
    [SerializeField] private float aliveSeconds = 10f;

    [Header("Risucchio")]
    [Tooltip("Distanza orizzontale dal buco nero, in metri, entro cui uno Sbirro viene spaghettificato e muore.")]
    [SerializeField] private float killRadius = 9f;

    private int charges;
    private FuelGaugeHUD hud; // rettangolo dei buchi neri sotto la barra stamina

    public int Charges => charges;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToPlayer()
    {
        PlayerMovement player = FindAnyObjectByType<PlayerMovement>();
        if (player != null && player.GetComponent<PlayerBlackHoleThrow>() == null)
        {
            player.gameObject.AddComponent<PlayerBlackHoleThrow>();
        }
    }

    void Awake()
    {
        charges = startingCharges;
        // Il componente si aggiunge da solo a runtime, quindi niente riferimento serializzato:
        // l'HUD è l'unico FuelGaugeHUD in scena (stesso fallback di PlayerStamina).
        hud = FindAnyObjectByType<FuelGaugeHUD>();
        UpdateUI();
    }

    void Update()
    {
        Gamepad pad = Gamepad.current;
        if (pad != null && pad.leftShoulder.wasPressedThisFrame && charges > 0)
        {
            charges--;
            UpdateUI();
            Throw();
        }
    }

    // Chiamato dalle ammo Hoffman raccolte.
    public void AddCharges(int amount)
    {
        charges += amount;
        UpdateUI();
    }

    private void UpdateUI()
    {
        if (hud != null)
        {
            hud.SetBlackHoles(charges);
        }
    }

    private void Throw()
    {
        Vector3 position = transform.position + transform.forward * throwDistance;
        position.y = transform.position.y + heightOffset;

        BlackHole hole;
        if (blackHolePrefab != null)
        {
            hole = Instantiate(blackHolePrefab, position, Quaternion.identity);
        }
        else
        {
            hole = new GameObject("BlackHole").AddComponent<BlackHole>();
            hole.transform.position = position;
            hole.eventHorizonRadius = eventHorizonRadius;
            hole.lensSize = lensSize;
            hole.lensStrength = lensStrength;
            hole.psyche = psyche;
        }

        hole.launchOnStart = false; // la nascita la facciamo partire noi, con la durata voluta
        hole.destroyOnDeath = true;
        // Gravità e spinta finale solo sugli Sbirro: Player, Raver, Camper e oggetti di scena
        // non vengono né attirati né inghiottiti (Consume li distruggerebbe).
        hole.excludeFilter = body => body.GetComponent<EnemyHealth>() == null;
        hole.gameObject.AddComponent<BlackHoleEnemyEater>().killRadius = killRadius;

        // duration comprende nascita e morte: si somma la parte "viva" a quelle due animazioni.
        float total = aliveSeconds > 0f ? aliveSeconds + hole.birthDuration + hole.deathDuration : 0f;
        hole.Launch(total);
    }
}
