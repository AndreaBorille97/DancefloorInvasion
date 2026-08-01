using TMPro;
using UnityEngine;

// Da mettere sul Camper piazzato in scena (Assets/Prefabs/Camper.prefab), con il suo
// Collider impostato come Trigger (Is Trigger = true).
//
// Funziona a benzina: CamperPickup la rabbocca di un quarto del serbatoio alla raccolta
// (RefillFuel, non un pieno completo: servono 4 ammo per riempirlo del tutto), e in
// qualsiasi momento della partita il Player può premere L2 (vedi PlayerCamperSummon) per
// mandare il Raver più vicino al Camper (TrySendNearestRaver), a patto che ci sia benzina e
// nessun altro Raver sia già assegnato. Il Raver ci cammina dritto sopra (RaverChase.
// SetForcedTarget) e appena lo tocca sale a bordo: da quel momento RaverChase riprende il
// controllo normale (bersaglia gli Sbirro come sempre) e il Camper lo segue in LateUpdate —
// niente vero parenting, altrimenti se il Raver muore Unity distruggerebbe anche il Camper
// insieme a lui. Mentre è a bordo, ogni Sbirro toccato dal Camper subisce il quadruplo del
// danno di un colpo di Raver normale (vedi EnemyHealth.TakeCamperHit), non più insta-kill,
// la benzina scende di un secondo per ogni secondo di guida, e il Raver alla guida è
// invulnerabile (vedi RaverHealth.SetInvulnerable, chiamato da Mount/Dismount): niente lo
// tocca finché resta a bordo.
//
// Il Camper resta fermo esattamente dov'è (nessun teletrasporto) sia quando il Raver pilota
// muore ucciso da un nemico (OnPilotDestroyed), sia quando finisce semplicemente la benzina:
// in quel caso il Raver resta vivo, scende e torna a comportarsi normalmente (Dismount, vedi
// Update). In entrambi i casi la benzina resta al livello raggiunto: il Player può subito
// mandare un altro Raver con L2, se ne resta.
public class CamperVehicle : MonoBehaviour
{
    [Header("Benzina")]
    [Tooltip("N: secondi massimi di guida col serbatoio pieno.")]
    [SerializeField] private float maxFuel = 40f;
    [Tooltip("Frazione del serbatoio rabboccata da ogni ammo raccolta (vedi RefillFuel, chiamato da CamperPickup): 0.25 = un quarto, servono 4 ammo per il pieno.")]
    [SerializeField] private float refillFraction = 0.25f;

    [Header("A bordo")]
    [Tooltip("Offset locale rispetto al Raver mentre è a bordo: regolabile per far combaciare visivamente i due modelli.")]
    [SerializeField] private Vector3 mountOffset = Vector3.zero;
    [Tooltip("Il cilindro di default sta in piedi (asse lungo = Y locale): con questa rotazione extra, applicata dopo aver orientato il Camper verso la direzione del Raver, resta coricato invece di rizzarsi con lui. Stesso trucco usato dal vecchio CamperProjectile.")]
    [SerializeField] private Vector3 lyingTiltEuler = new Vector3(90f, 0f, 0f);
    [Tooltip("Quanto velocemente il Camper ruota per allinearsi alla direzione del Raver: più basso = sterzata più lenta e pesante, da veicolo invece che da oggetto agganciato di scatto.")]
    [SerializeField] private float rotationSpeed = 4f;
    [Tooltip("Moltiplicatore di velocità applicato al Raver mentre pilota (vedi RaverChase.MultiplyMoveSpeed): più veloce di un Raver normale a piedi.")]
    [SerializeField] private float speedMultiplier = 1.5f;

    [Header("UI")]
    [SerializeField] private TMP_Text fuelText; // testo TextMeshPro che mostra la benzina residua; se vuoto non mostra nulla
    [SerializeField] private FuelGaugeHUD hud; // lancetta del gauge; se vuoto non aggiorna nulla

    [Header("Ingombro fisico")]
    [Tooltip("Collider separato, NON Trigger, da aggiungere sullo stesso GameObject (es. un altro CapsuleCollider/BoxCollider intorno alla mesh): senza Rigidbody è già statico e blocca Player/Sbirro/Raver come un muro. Attivo solo a mezzo parcheggiato: da guidato interferirebbe col rilevamento one-shot e spingerebbe fisicamente Raver/nemici. Se vuoto, il Camper resta sempre attraversabile.")]
    [SerializeField] private Collider blockingCollider;

    private RaverChase pilot;
    private bool isClaimed; // già assegnato a un Raver (in avvicinamento o già a bordo)
    private bool isMounted; // il Raver è salito: il collider ora fa danno one-shot, e la benzina scende
    private float fuel;

    void Awake()
    {
        UpdateFuelUI();
    }

    void Update()
    {
        if (!isMounted)
        {
            return;
        }

        fuel -= Time.deltaTime;
        if (fuel <= 0f)
        {
            fuel = 0f;
            Dismount();
        }
        UpdateFuelUI();
    }

    void LateUpdate()
    {
        if (!isMounted || pilot == null)
        {
            return;
        }

        // Solo l'orientamento orizzontale (yaw) del Raver: usare la sua rotazione completa
        // farebbe rizzare in piedi il cilindro seguendo eventuali beccheggi/rollii, invece
        // di restare coricato come richiesto.
        Vector3 forward = pilot.transform.forward;
        forward.y = 0f;
        Quaternion facing = forward.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(forward.normalized, Vector3.up)
            : Quaternion.identity;
        Quaternion targetRotation = facing * Quaternion.Euler(lyingTiltEuler);

        // Rotazione interpolata invece che agganciata di scatto: dà l'idea di un veicolo
        // che sterza, non di un oggetto incollato al Raver. La posizione resta invece
        // agganciata subito, altrimenti il Camper si "staccherebbe" visivamente da lui.
        transform.SetPositionAndRotation(
            pilot.transform.position + pilot.transform.TransformDirection(mountOffset),
            Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime));
    }

    // Chiamato da CamperPickup alla raccolta dell'ammo: aggiunge solo refillFraction del
    // serbatoio (non un pieno completo), sommandosi a quanta benzina restava già, senza
    // superare maxFuel.
    public void RefillFuel()
    {
        fuel = Mathf.Min(fuel + maxFuel * refillFraction, maxFuel);
        UpdateFuelUI();
    }

    private void UpdateFuelUI()
    {
        if (fuelText != null)
        {
            fuelText.text = $"Benzina: {Mathf.CeilToInt(fuel)}s";
        }

        if (hud != null)
        {
            hud.SetFuel(fuel, maxFuel);
        }
    }

    // Chiamato da PlayerCamperSummon quando si preme L2, in qualsiasi momento della
    // partita: manda il Raver più vicino al Camper solo se c'è benzina e nessun altro
    // Raver è già assegnato (in avvicinamento o a bordo).
    public void TrySendNearestRaver()
    {
        if (isClaimed || fuel <= 0f)
        {
            return;
        }

        RaverChase nearest = FindNearestRaver();
        if (nearest == null)
        {
            return;
        }

        isClaimed = true;
        pilot = nearest;
        pilot.SetForcedTarget(transform);

        CamperPilotWatcher watcher = pilot.gameObject.AddComponent<CamperPilotWatcher>();
        watcher.Init(this);
    }

    // Chiamato da CamperPilotWatcher quando il Raver assegnato muore ucciso da un nemico
    // (in avvicinamento o a bordo): il Camper resta fermo dov'è (nessun teletrasporto al
    // punto di partenza) e la benzina resta al livello raggiunto, pronto per il prossimo Raver.
    public void OnPilotDestroyed()
    {
        isClaimed = false;
        isMounted = false;
        pilot = null;
        SetBlockingColliderEnabled(true);
    }

    // Finita la benzina mentre il Raver è ancora vivo (vedi Update): scende dal mezzo e
    // torna subito a comportarsi normalmente (RaverChase ha già ripreso il controllo dal
    // momento del Mount). Il Camper resta fermo dov'è, pronto per il prossimo Raver.
    private void Dismount()
    {
        isClaimed = false;
        isMounted = false;

        if (pilot != null)
        {
            pilot.SetVehicleMode(false);
            pilot.MultiplyMoveSpeed(1f / speedMultiplier); // ripristina la velocità normale a piedi
            pilot.GetComponent<RaverHealth>()?.SetInvulnerable(false); // torna vulnerabile appena scende
            CamperPilotWatcher watcher = pilot.GetComponent<CamperPilotWatcher>();
            if (watcher != null)
            {
                Destroy(watcher);
            }
        }
        pilot = null;
        SetBlockingColliderEnabled(true);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!isMounted)
        {
            // Ancora a terra: solo il Raver assegnato può salirci sopra.
            if (pilot != null && other.transform == pilot.transform)
            {
                Mount();
            }
            return;
        }

        // A bordo: quadruplo del danno di un colpo di Raver normale sugli Sbirro toccati,
        // non più insta-kill (vedi EnemyHealth.TakeCamperHit).
        EnemyHealth enemyHealth = other.GetComponent<EnemyHealth>();
        if (enemyHealth != null)
        {
            enemyHealth.TakeCamperHit();
            return;
        }

        RobosbirroHealth robosbirroHealth = other.GetComponent<RobosbirroHealth>();
        if (robosbirroHealth != null)
        {
            robosbirroHealth.TakeCamperHit();
        }
    }

    private void Mount()
    {
        isMounted = true;
        pilot.ClearForcedTarget(); // torna a bersagliare gli Sbirro normalmente, ora col Camper al seguito
        pilot.SetVehicleMode(true); // avanza sterzando invece di ruotare/spostarsi di scatto
        pilot.MultiplyMoveSpeed(speedMultiplier); // più veloce di un Raver a piedi
        pilot.GetComponent<RaverHealth>()?.SetInvulnerable(true); // niente lo tocca finché resta a bordo
        SetBlockingColliderEnabled(false); // da guidato niente ingombro fisico: non deve spingere Raver/nemici né bloccare il one-shot
    }

    private void SetBlockingColliderEnabled(bool value)
    {
        if (blockingCollider != null)
        {
            blockingCollider.enabled = value;
        }
    }

    private RaverChase FindNearestRaver()
    {
        RaverChase nearest = null;
        float nearestSqrDistance = float.MaxValue;

        foreach (RaverChase raver in FindObjectsByType<RaverChase>(FindObjectsSortMode.None))
        {
            // Un Raver a terra (in attesa di essere rianimato dal cono, vedi RaverHealth.IsDown)
            // non può essere richiamato a pilotare il Camper.
            RaverHealth health = raver.GetComponent<RaverHealth>();
            if (health != null && health.IsDown)
            {
                continue;
            }

            float sqrDistance = (raver.transform.position - transform.position).sqrMagnitude;
            if (sqrDistance < nearestSqrDistance)
            {
                nearest = raver;
                nearestSqrDistance = sqrDistance;
            }
        }

        return nearest;
    }
}
