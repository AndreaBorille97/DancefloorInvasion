using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Da mettere sul Camper piazzato in scena (Assets/Prefabs/Camper.prefab). Il suo Collider
// viene forzato a Trigger in Awake, come serve allo script: non c'è niente da spuntare in scena.
//
// Il Camper parcheggiato è solido (Player e Sbirro non ci passano attraverso) e insieme
// rilevante ai trigger: i due mestieri stanno su due collider sovrapposti, quello Trigger del
// prefab e il gemello solido blockingCollider, creato da solo in Awake se non gliene è stato
// messo uno a mano (vedi CreateSolidCopy).
//
// Funziona a benzina: CamperPickup la rabbocca di un quarto del serbatoio alla raccolta
// (RefillFuel, non un pieno completo: servono 4 ammo per riempirlo del tutto), e in
// qualsiasi momento della partita il Player può premere L2 (vedi PlayerCamperSummon) per
// mandare il Raver più vicino al Camper (TrySendNearestRaver), a patto che ci sia benzina e
// nessun altro Raver sia già assegnato. Il Raver ci cammina dritto sopra (RaverChase.
// SetForcedTarget) e appena gli arriva accanto (mountDistance, vedi TryMountByDistance: non
// serve il contatto col collider) sale a bordo: da quel momento RaverChase riprende il
// controllo normale (bersaglia gli Sbirro come sempre) e il Camper lo segue in LateUpdate —
// niente vero parenting, altrimenti se il Raver muore Unity distruggerebbe anche il Camper
// insieme a lui. Seguirlo vuol dire tenergli il modello centrato addosso e girare la posa con
// cui il mezzo è piazzato in scena attorno al solo asse verticale, quel tanto che porta il muso
// nella direzione di marcia: la posa piazzata non va mai ricostruita da zero, è lei quella
// giusta (vedi LateUpdate). Mentre è a bordo, ogni Sbirro toccato dal Camper subisce il quadruplo del
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
    [Tooltip("Il muso del mezzo viene riconosciuto da solo (il lato lungo del modello, vedi MeasureParkedNose). Se in gioco lo vedi guidare all'indietro, spunta questa e viene girato di 180°: è un sì/no, non ci sono gradi da tarare a occhio.")]
    [SerializeField] private bool invertNose;
    [Tooltip("Quanto vicino alla fiancata del mezzo deve arrivare il Raver assegnato per salire a bordo (vedi TryMountByDistance): è una distanza dalla sagoma del collider, non dal centro, quindi non va ritarata se il Camper in scena cambia taglia. Tienila almeno un paio di decimi sopra zero: il Raver si ferma appena tocca il mezzo e non arriva mai a distanza esattamente nulla.")]
    [SerializeField] private float mountDistance = 1f;

    [Header("Guida")]
    [Tooltip("Velocità, spunto e raggio di sterzata del mezzo mentre è guidato: vengono passati al Raver che sale a bordo (vedi Mount), che è chi poi lo guida davvero.")]
    [SerializeField] private VehicleTuning driving = new VehicleTuning();

    [Header("UI")]
    [SerializeField] private TMP_Text fuelText; // testo TextMeshPro che mostra la benzina residua; se vuoto non mostra nulla
    [SerializeField] private FuelGaugeHUD hud; // lancetta del gauge; se vuoto non aggiorna nulla

    [Header("Ingombro fisico")]
    [Tooltip("Collider separato, NON Trigger, sullo stesso GameObject: senza Rigidbody è già statico e blocca Player/Sbirro come un muro. Attivo solo a mezzo parcheggiato: da guidato interferirebbe col rilevamento one-shot e spingerebbe fisicamente Raver/nemici. Se resta vuoto va bene lo stesso: in Awake ne viene creato uno a runtime copiando la forma del collider Trigger, quindi il mezzo parcheggiato è comunque solido. Va riempito a mano solo per dargli un ingombro diverso dalla sagoma del Trigger.")]
    [SerializeField] private Collider blockingCollider;

    private RaverChase pilot;
    private bool isClaimed; // già assegnato a un Raver (in avvicinamento o già a bordo)
    private bool isMounted; // il Raver è salito: il collider ora fa danno one-shot, e la benzina scende
    private float fuel;

    // Animazioni del modello del mezzo (ruote, sospensioni...): girano solo mentre è guidato,
    // vedi VehicleDriveAnimation. Basta che il modello sotto al Camper abbia quel componente e
    // viene agganciato da solo; se non c'è, il Camper funziona come prima senza animazioni.
    private VehicleDriveAnimation driveAnimation;

    private Collider triggerCollider; // il collider Trigger: salita a bordo del Raver e Sbirro investiti

    // Posa con cui il mezzo è piazzato in scena: è quella giusta (ruote a terra, modello dritto),
    // e da guidato ci si limita a ruotarla attorno all'asse verticale. Vedi LateUpdate.
    private Quaternion parkedRotation;
    private float parkedHeight; // altezza del pivot da parcheggiato: il pavimento dell'arena è piano, quindi resta questa anche in corsa
    private Vector3 parkedNose; // direzione orizzontale del muso nella posa parcheggiata
    private Vector3 modelLocalCenter; // centro visivo del modello rispetto al pivot: il pivot del Camper non ci sta dentro

    // Sagoma del mezzo vista dall'alto, misurata dal collider Trigger (vedi MeasureBodyShape):
    // un segmento centrale lungo bodyHalfSpine*2 ingrossato di bodyRadius, cioè la pianta di
    // una capsula sdraiata. Serve a KeepBodyClearOfTargets.
    private Vector3 bodyLocalAxis = Vector3.forward; // asse lungo della sagoma, in coordinate locali
    private float bodyHalfSpine;
    private float bodyRadius;
    private int bodySamples = 1; // in quanti cerchi viene spezzata la sagoma per il controllo

    // Sound system, Console e chillout: i posti sopra cui il mezzo non deve mai passare (vedi
    // CacheNoGoZones). Misurati una volta sola: sono oggetti di scena, non si muovono.
    private NoGoZone[] noGoZones;

    // Pianta (vista dall'alto) di un posto vietato al mezzo: un rettangolo ruotato attorno
    // all'asse verticale. La quota non conta, il pavimento dell'arena è piano.
    private struct NoGoZone
    {
        public Transform source;    // se viene distrutto (partita persa) la zona non vale più
        public Vector3 center;
        public Quaternion rotation; // solo imbardata: gli assi del rettangolo
        public float halfX;
        public float halfZ;
    }

    // Direzione orizzontale verso cui punta il muso nella posa parcheggiata, invertNose già
    // applicato: la usa VehicleDriveAnimation per capire quali sono le ruote davanti.
    public Vector3 ParkedNose => invertNose ? -parkedNose : parkedNose;

    void Awake()
    {
        // includeInactive: il modello potrebbe essere disattivato in prefab, e senza il flag
        // non verrebbe trovato affatto.
        driveAnimation = GetComponentInChildren<VehicleDriveAnimation>(true);

        // Il Camper ha bisogno di due collider sovrapposti, con due mestieri diversi:
        // - quello Trigger fa salire a bordo il Raver (OnTriggerEnter) e investe gli Sbirro
        //   mentre è guidato;
        // - quello solido (blockingCollider) è l'ingombro fisico: da parcheggiato il mezzo è un
        //   muro, Player e Sbirro non ci passano attraverso.
        // Con un collider solo si può fare o l'uno o l'altro: se in scena è stato lasciato solido
        // niente sale a bordo e niente viene investito, se è Trigger ci si cammina dentro. Qui il
        // Trigger lo forziamo, e il gemello solido, se non ne è stato messo uno a mano, ce lo
        // costruiamo da soli copiando la forma del Trigger: così l'ingombro combacia sempre con
        // quello che si vede, senza niente da sistemare in scena.
        Collider[] existingColliders = GetComponents<Collider>();
        foreach (Collider col in existingColliders)
        {
            if (col != blockingCollider)
            {
                col.isTrigger = true;
                if (triggerCollider == null)
                {
                    triggerCollider = col;
                }
            }
        }

        // Come è piazzato in scena il mezzo è dritto e poggia a terra: quella posa è il nostro
        // riferimento, da guidato la giriamo soltanto attorno all'asse verticale (vedi LateUpdate).
        parkedRotation = transform.rotation;
        parkedHeight = transform.position.y;

        Renderer[] modelRenderers = GetComponentsInChildren<Renderer>(true);
        modelLocalCenter = MeasureModelCenter(modelRenderers);
        parkedNose = MeasureParkedNose(modelRenderers);

        // Le ruote che girano e sterzano: se nessuno ha messo il componente in scena se lo mette
        // il Camper da sé, qui sopra, e si arrangia a trovare le ruote fra i figli del modello.
        // Senza ruote riconoscibili non fa niente e il mezzo va come prima, immobile nelle parti.
        if (driveAnimation == null)
        {
            driveAnimation = gameObject.AddComponent<VehicleDriveAnimation>();
        }

        // Se le ruote ci sono, il muso lo dicono loro: gli assali sono perpendicolari all'asse
        // lungo del mezzo, ed è una misura molto più solida del riquadro d'ingombro usato qui
        // sopra, che su una carrozzeria tozza può risultare più larga che lunga e far credere che
        // il muso guardi di fianco (con le ruote di un lato che sterzano al posto delle anteriori,
        // e il mezzo che viaggia di traverso). Il verso muso/coda resta quello scelto sopra, e
        // invertNose continua a ribaltarlo.
        Vector3 wheelNose = driveAnimation.MeasureLengthAxis(parkedNose);
        if (wheelNose != Vector3.zero)
        {
            parkedNose = wheelNose;
        }

        // Adesso che il muso è deciso: le ruote davanti sono quelle dalla sua parte.
        driveAnimation.SetUpWheels(ParkedNose);

        // Il fumo di scarico: si arrangia da solo, gli basta vedere di quanto si sposta il mezzo
        // (e chiede a noi da che parte è il muso per piazzare le bocche).
        if (GetComponent<ExhaustSmoke>() == null)
        {
            gameObject.AddComponent<ExhaustSmoke>();
        }

        // Il collider va centrato sul modello, non sul pivot del Camper: il modello è un figlio
        // spostato per conto suo, quindi il collider com'è messo resta indietro di qualche metro
        // rispetto a quello che si vede — il muro sta dove non c'è niente e gli Sbirro vengono
        // investiti dal vuoto. La forma e la taglia non le tocchiamo: quelle restano come sono
        // state impostate in scena.
        CenterColliderOnModel(triggerCollider);

        // Dopo l'allineamento, così il gemello solido nasce già centrato sul modello.
        if (blockingCollider == null)
        {
            blockingCollider = CreateSolidCopy(triggerCollider);
        }
        else
        {
            CenterColliderOnModel(blockingCollider);
        }

        // Con che sagoma il mezzo occupa il pavimento, e quali sono i punti dove non deve
        // mai finirci sopra: vedi KeepBodyClearOfTargets.
        MeasureBodyShape(triggerCollider);
        CacheNoGoZones();

        // L'HUD è un oggetto di scena, non nel prefab del Camper: se il riferimento
        // serializzato è vuoto riagganciamo l'unico FuelGaugeHUD presente in scena.
        if (hud == null)
        {
            hud = FindAnyObjectByType<FuelGaugeHUD>();
        }

        UpdateFuelUI();
    }

    void Update()
    {
        if (!isMounted)
        {
            TryMountByDistance();
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

        // La posa buona del mezzo è quella con cui è piazzato in scena: il modello è un figlio
        // con una sua rotazione locale che compensa quella del pivot, quindi costruire qui una
        // rotazione da zero (come faceva il vecchio codice, pensato per il cilindro segnaposto)
        // manda il mezzo di traverso. Partiamo da parkedRotation e la giriamo solo attorno
        // all'asse verticale, quel tanto che serve a portare il muso nella direzione del Raver:
        // così il modello resta esattamente dritto com'è stato piazzato, comunque sia orientato.
        // Il muso va esattamente dove punta il Raver, senza ritardo: la sterzata graduale la
        // fa già lui (RaverChase.DriveVehicle, che gira in proporzione allo spazio percorso).
        // Interpolare anche qui faceva restare indietro la carrozzeria rispetto alla direzione
        // di marcia, ed è quello che dava l'effetto "slitta di lato" invece che da mezzo su
        // quattro ruote.
        transform.rotation = BodyRotationFor(pilot.transform.forward);

        // Il Raver va messo al centro del mezzo, non sul suo pivot: il pivot del Camper sta
        // fuori dal modello (il modello è un figlio spostato di suo), ed è il motivo per cui in
        // corsa si vedeva il mezzo staccato dal Raver. Compensiamo con lo scostamento misurato
        // in Awake, che ruota insieme al mezzo. La quota resta quella da parcheggiato: il
        // pavimento dell'arena è piano, e seguire la y del Raver farebbe sprofondare il modello.
        Vector3 position = pilot.transform.position
            + pilot.transform.TransformDirection(mountOffset)
            - transform.TransformVector(modelLocalCenter);
        position.y = parkedHeight;
        transform.position = position;
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

    // Chiamato da CamperPilotWatcher quando il Raver assegnato non è più in grado di guidare
    // (va a terra mentre si avvicina o mentre è a bordo, oppure per sicurezza se viene
    // comunque distrutto): il Camper resta fermo dov'è (nessun teletrasporto al punto di
    // partenza) e la benzina resta al livello raggiunto, pronto per il prossimo Raver.
    public void OnPilotDestroyed()
    {
        // Il Raver potrebbe essere ancora vivo (a terra, in attesa di rianimazione): senza
        // cancellare il forcedTarget, appena rianimato tornerebbe dritto qui invece che al
        // suo punto di guardia originale.
        pilot?.ClearForcedTarget();
        pilot?.SetVehicle(null); // il Raver non guida più: niente ingombro del mezzo addosso

        isClaimed = false;
        isMounted = false;
        pilot = null;
        SetBlockingColliderEnabled(true);
        driveAnimation?.SetDriving(false);
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
            pilot.SetVehicle(null); // sceso: torna a muoversi da Raver a piedi, senza l'ingombro del mezzo
            pilot.GetComponent<RaverHealth>()?.SetInvulnerable(false); // torna vulnerabile appena scende
            CamperPilotWatcher watcher = pilot.GetComponent<CamperPilotWatcher>();
            if (watcher != null)
            {
                Destroy(watcher);
            }
        }
        pilot = null;
        SetBlockingColliderEnabled(true);
        driveAnimation?.SetDriving(false);
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

        // L'IdroSbirro non si muove e non può essere investito: il Camper lo attraversa
        // senza fargli danno, come se non ci fosse (usa EnemyHealth come gli altri Sbirro,
        // quindi va escluso esplicitamente prima del controllo generico qui sotto).
        if (other.GetComponent<IdroSbirroHoseAttack>() != null)
        {
            return;
        }

        // A bordo: quadruplo del danno di un colpo di Raver normale sugli Sbirro toccati,
        // non più insta-kill (vedi EnemyHealth.TakeCamperHit).
        EnemyHealth enemyHealth = other.GetComponent<EnemyHealth>();
        if (enemyHealth != null)
        {
            // Il punto d'impatto è il centro del modello, non il pivot: da lì parte la spinta
            // data allo Sbirro investito, e il pivot del Camper sta fuori dalla carrozzeria.
            enemyHealth.TakeCamperHit(transform.TransformPoint(modelLocalCenter));
            return;
        }

        RobosbirroHealth robosbirroHealth = other.GetComponent<RobosbirroHealth>();
        if (robosbirroHealth != null)
        {
            robosbirroHealth.TakeCamperHit();
        }
    }

    // Salire a bordo non può dipendere dal contatto col collider Trigger: da parcheggiato il
    // mezzo ha anche il suo guscio solido, e il Raver in arrivo ci sbatte contro fermandosi lì
    // fuori — è esattamente il "si avvicina ma non entra". Basta quindi che gli sia arrivato
    // accanto: misuriamo la distanza dal punto più vicino della sagoma del mezzo (ClosestPoint),
    // non dal suo centro, così mountDistance è un margine dalla fiancata e vale uguale sul lato
    // lungo e su quello corto, qualunque sia la taglia del Camper in scena. La y va ignorata
    // apposta: il Camper può stare più in alto del Raver.
    private void TryMountByDistance()
    {
        if (!isClaimed || pilot == null)
        {
            return;
        }

        // ClosestPoint vuole un collider attivo: da parcheggiato c'è il guscio solido, ed è
        // quello contro cui il Raver si ferma; altrimenti ripiega sul Trigger, sempre acceso.
        Collider shape = blockingCollider != null && blockingCollider.enabled ? blockingCollider : triggerCollider;

        Vector3 pilotPosition = pilot.transform.position;
        Vector3 toPilot = shape != null
            ? pilotPosition - shape.ClosestPoint(pilotPosition) // dentro la sagoma restituisce il punto stesso: distanza 0, sale subito
            : pilotPosition - transform.position;
        toPilot.y = 0f;

        if (toPilot.sqrMagnitude <= mountDistance * mountDistance)
        {
            Mount();
        }
    }

    private void Mount()
    {
        isMounted = true;
        pilot.ClearForcedTarget(); // torna a bersagliare gli Sbirro normalmente, ora col Camper al seguito
        pilot.SetVehicleTuning(driving); // com'è fatto questo mezzo: velocità, spunto, raggio di sterzata
        pilot.SetVehicle(this); // da qui in poi è il mezzo a dire dove la sua sagoma non può andare (KeepBodyClearOfTargets)
        pilot.SetVehicleMode(true); // da qui guida: accelera, sterza e frena da mezzo (RaverChase.DriveVehicle),
                                    // alla velocità del mezzo e non a quella del Raver a piedi
        pilot.GetComponent<RaverHealth>()?.SetInvulnerable(true); // niente lo tocca finché resta a bordo
        SetBlockingColliderEnabled(false); // da guidato niente ingombro fisico: non deve spingere Raver/nemici né bloccare il one-shot
        driveAnimation?.SetDriving(true);
    }

    // Chiamato da RaverChase.DriveVehicle ad ogni passo di guida, con la posizione in cui il
    // pilota finirebbe: se lì la sagoma del mezzo finirebbe sopra sound system o Console, la
    // riporta fuori e restituisce la posizione corretta.
    //
    // Non può farlo la fisica: da guidato il guscio solido del mezzo è spento (servirebbe a
    // spingere Raver e nemici, e taglierebbe fuori il rilevamento degli Sbirro investiti), e
    // l'unico collider che si muove davvero è quello del Raver alla guida, largo un quinto del
    // camion — infatti il Raver si fermava contro la Console col mezzo già dentro fino a metà.
    // Qui invece è la sagoma del mezzo a contare: la trattiamo come la pianta di una capsula
    // sdraiata (un segmento ingrossato, vedi MeasureBodyShape) e la spingiamo fuori dal
    // rettangolo dell'obiettivo lungo la via più corta, cioè perpendicolarmente al lato
    // toccato. Così quello che resta del movimento è la componente parallela all'obiettivo: il
    // mezzo ci struscia contro e scivola via invece di fermarsi secco o entrarci.
    public Vector3 KeepBodyClearOfTargets(Vector3 pilotPosition, Quaternion pilotRotation)
    {
        if (noGoZones == null || noGoZones.Length == 0 || bodyRadius <= 0f)
        {
            return pilotPosition;
        }

        // Il centro della sagoma è il punto in cui viene messo il pilota (vedi LateUpdate:
        // il mezzo si piazza proprio in modo che il suo centro ci coincida), e il suo asse
        // lungo è quello del mezzo girato come sarà alla fine di questo passo di guida, non
        // com'è adesso: la coda che spazza in curva è proprio quello che non deve entrarci.
        Vector3 center = pilotPosition + pilotRotation * mountOffset;
        Vector3 halfSpine = BodyRotationFor(pilotRotation * Vector3.forward) * bodyLocalAxis * bodyHalfSpine;
        halfSpine.y = 0f;

        // Più obiettivi vicini (le 4 casse del sound system sono 4 rettangoli distinti) possono
        // spingere da parti diverse: ad ogni giro si applica la spinta più profonda e si
        // ricontrolla, invece di sommare spinte che si annullano tra loro.
        Vector3 correction = Vector3.zero;
        for (int pass = 0; pass < 4; pass++)
        {
            Vector3 deepest = Vector3.zero;
            float deepestSqrMagnitude = 0f;

            for (int i = 0; i <= bodySamples; i++)
            {
                Vector3 point = center + correction
                    + Vector3.Lerp(-halfSpine, halfSpine, (float)i / bodySamples);

                foreach (NoGoZone zone in noGoZones)
                {
                    if (zone.source == null)
                    {
                        continue; // obiettivo distrutto: la partita è già persa, non c'è più niente da salvaguardare
                    }

                    Vector3 push = PushOutOfZone(point, bodyRadius, zone);
                    if (push.sqrMagnitude > deepestSqrMagnitude)
                    {
                        deepest = push;
                        deepestSqrMagnitude = push.sqrMagnitude;
                    }
                }
            }

            if (deepestSqrMagnitude <= 0f)
            {
                break; // la sagoma è tutta fuori: non c'è niente da correggere
            }

            correction += deepest;
        }

        return pilotPosition + correction;
    }

    // Con che rotazione si piazza il mezzo quando il pilota guarda in quella direzione: la posa
    // parcheggiata (quella giusta, ruote a terra e modello dritto) girata attorno al solo asse
    // verticale, quel tanto che porta il muso lì. Vedi LateUpdate.
    private Quaternion BodyRotationFor(Vector3 pilotForward)
    {
        pilotForward.y = 0f;
        if (pilotForward.sqrMagnitude <= 0.0001f)
        {
            return transform.rotation;
        }

        Vector3 desiredNose = invertNose ? -pilotForward.normalized : pilotForward.normalized;
        return Quaternion.FromToRotation(parkedNose, desiredNose) * parkedRotation;
    }

    // Di quanto va spostato un cerchio di raggio radius centrato in point perché non si
    // sovrapponga più alla pianta della zona. Vector3.zero se non la tocca.
    private static Vector3 PushOutOfZone(Vector3 point, float radius, NoGoZone zone)
    {
        Vector3 local = Quaternion.Inverse(zone.rotation) * (point - zone.center);

        // Punto del rettangolo più vicino al centro del cerchio: se cade dentro, è il centro
        // stesso, ed è il caso trattato più sotto.
        float closestX = Mathf.Clamp(local.x, -zone.halfX, zone.halfX);
        float closestZ = Mathf.Clamp(local.z, -zone.halfZ, zone.halfZ);
        float offsetX = local.x - closestX;
        float offsetZ = local.z - closestZ;
        float sqrDistance = offsetX * offsetX + offsetZ * offsetZ;

        if (sqrDistance >= radius * radius)
        {
            return Vector3.zero;
        }

        if (sqrDistance > 0.000001f)
        {
            float distance = Mathf.Sqrt(sqrDistance);
            float push = radius - distance;
            return zone.rotation * new Vector3(offsetX / distance * push, 0f, offsetZ / distance * push);
        }

        // Centro già dentro il rettangolo (può capitare solo partendo da lì, es. il mezzo
        // parcheggiato a ridosso): esce dal lato più vicino, quello che costa meno strada.
        float toSideX = zone.halfX - Mathf.Abs(local.x);
        float toSideZ = zone.halfZ - Mathf.Abs(local.z);
        return toSideX <= toSideZ
            ? zone.rotation * new Vector3(Mathf.Sign(local.x) * (toSideX + radius), 0f, 0f)
            : zone.rotation * new Vector3(0f, 0f, Mathf.Sign(local.z) * (toSideZ + radius));
    }

    // Che ingombro ha il mezzo visto dall'alto, letto dal collider Trigger così com'è messo in
    // scena: niente da tarare a mano, e se il Camper cambia taglia la sagoma lo segue. La
    // trattiamo sempre come la pianta di una capsula sdraiata (un segmento lungo
    // bodyHalfSpine*2 ingrossato di bodyRadius), che per un mezzo — lungo e stretto — è una
    // descrizione molto più fedele di un cerchione unico attorno a tutto.
    private void MeasureBodyShape(Collider source)
    {
        Vector3 scale = transform.lossyScale;
        scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

        switch (source)
        {
            case CapsuleCollider capsule:
            {
                bodyLocalAxis = capsule.direction == 0 ? Vector3.right
                    : capsule.direction == 1 ? Vector3.up
                    : Vector3.forward;
                float alongScale = Vector3.Dot(scale, bodyLocalAxis);
                float aroundScale = Mathf.Max(
                    capsule.direction == 0 ? scale.y : scale.x,
                    capsule.direction == 2 ? scale.y : scale.z);
                bodyRadius = capsule.radius * aroundScale;
                bodyHalfSpine = Mathf.Max(0f, capsule.height * alongScale * 0.5f - bodyRadius);
                break;
            }

            case BoxCollider box:
            {
                Vector3 size = Vector3.Scale(box.size, scale);
                // Il lato più lungo è l'asse del mezzo, gli altri due danno lo spessore.
                if (size.x >= size.y && size.x >= size.z)
                {
                    bodyLocalAxis = Vector3.right;
                    bodyRadius = Mathf.Max(size.y, size.z) * 0.5f;
                }
                else if (size.z >= size.x && size.z >= size.y)
                {
                    bodyLocalAxis = Vector3.forward;
                    bodyRadius = Mathf.Max(size.x, size.y) * 0.5f;
                }
                else
                {
                    bodyLocalAxis = Vector3.up;
                    bodyRadius = Mathf.Max(size.x, size.z) * 0.5f;
                }
                bodyHalfSpine = Mathf.Max(0f, Vector3.Dot(size, bodyLocalAxis) * 0.5f - bodyRadius);
                break;
            }

            case SphereCollider sphere:
                bodyLocalAxis = Vector3.forward;
                bodyRadius = sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
                bodyHalfSpine = 0f;
                break;

            default:
                // Forma non prevista (o nessun collider Trigger): ripieghiamo sul riquadro
                // d'ingombro del modello, trattato come un cerchio unico.
                bodyLocalAxis = Vector3.forward;
                bodyRadius = source != null ? Mathf.Max(source.bounds.extents.x, source.bounds.extents.z) : 0f;
                bodyHalfSpine = 0f;
                break;
        }

        // In quanti cerchi spezzare il segmento: abbastanza fitti da non lasciare buchi tra
        // l'uno e l'altro (un passo di mezzo raggio), pochi abbastanza da non pesare.
        bodySamples = bodyRadius > 0f
            ? Mathf.Clamp(Mathf.CeilToInt(bodyHalfSpine * 2f / (bodyRadius * 0.5f)), 1, 16)
            : 1;
    }

    // I posti dove il mezzo non deve mai finire sopra, presi una volta sola all'avvio: il
    // sound system (le sue 4 casse sono 4 ingombri distinti, e il mezzo può benissimo passare
    // tra l'una e l'altra), la Console e la tenda chillout. Sono oggetti di scena fermi,
    // quindi la pianta misurata qui resta buona per tutta la partita.
    private void CacheNoGoZones()
    {
        List<NoGoZone> zones = new List<NoGoZone>();

        foreach (SoundSystem soundSystem in FindObjectsByType<SoundSystem>(FindObjectsSortMode.None))
        {
            AddNoGoZones(zones, soundSystem.gameObject, false);
        }

        foreach (DJConsoleHealth console in FindObjectsByType<DJConsoleHealth>(FindObjectsSortMode.None))
        {
            AddNoGoZones(zones, console.gameObject, false);
        }

        // La chillout è l'unica a cui prendiamo anche i Trigger: la tenda ha i suoi ingombri
        // solidi (pilastri e fianchi), ma tra l'uno e l'altro resta aperta, e senza il Trigger
        // di ChilloutZone — che copre tutta la pianta della tenda — il mezzo ci si infilerebbe
        // in mezzo. A piedi si continua a entrarci come prima: qui il Trigger vale come
        // ingombro solo per la sagoma del mezzo.
        foreach (ChilloutZone chillout in FindObjectsByType<ChilloutZone>(FindObjectsSortMode.None))
        {
            AddNoGoZones(zones, chillout.gameObject, true);
        }

        noGoZones = zones.ToArray();
    }

    private static void AddNoGoZones(List<NoGoZone> destination, GameObject target, bool includeTriggers)
    {
        foreach (Collider collider in target.GetComponentsInChildren<Collider>())
        {
            // I trigger (raccolte, zone d'effetto) di solito non sono ingombri: attraversarli
            // non è compenetrare niente.
            if (includeTriggers || !collider.isTrigger)
            {
                destination.Add(MeasureNoGoZone(collider));
            }
        }
    }

    // Pianta di un ingombro: il rettangolo che lo contiene visto dall'alto, negli assi
    // dell'oggetto (le casse del sound system sono girate ognuna per conto suo, un riquadro
    // allineato al mondo ne coprirebbe molta più superficie del dovuto).
    private static NoGoZone MeasureNoGoZone(Collider source)
    {
        Transform owner = source.transform;
        Vector3 forward = owner.forward;
        forward.y = 0f;
        Quaternion rotation = forward.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(forward.normalized, Vector3.up)
            : Quaternion.identity;

        // Gli 8 spigoli dell'ingombro: per un BoxCollider sono quelli veri (anche se l'oggetto
        // è inclinato), altrimenti quelli del riquadro d'ingombro in coordinate mondo.
        Vector3 localCenter;
        Vector3 localExtents;
        Matrix4x4 toWorld;
        if (source is BoxCollider box)
        {
            localCenter = box.center;
            localExtents = box.size * 0.5f;
            toWorld = owner.localToWorldMatrix;
        }
        else
        {
            Bounds bounds = source.bounds;
            localCenter = bounds.center;
            localExtents = bounds.extents;
            toWorld = Matrix4x4.identity;
        }

        Quaternion toZone = Quaternion.Inverse(rotation);
        Vector3 min = Vector3.positiveInfinity;
        Vector3 max = Vector3.negativeInfinity;
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 offset = new Vector3(
                (corner & 1) == 0 ? -localExtents.x : localExtents.x,
                (corner & 2) == 0 ? -localExtents.y : localExtents.y,
                (corner & 4) == 0 ? -localExtents.z : localExtents.z);
            Vector3 inZone = toZone * toWorld.MultiplyPoint3x4(localCenter + offset);
            min = Vector3.Min(min, inZone);
            max = Vector3.Max(max, inZone);
        }

        Vector3 zoneCenter = (min + max) * 0.5f;
        return new NoGoZone
        {
            source = owner,
            center = rotation * zoneCenter,
            rotation = rotation,
            halfX = (max.x - min.x) * 0.5f,
            halfZ = (max.z - min.z) * 0.5f,
        };
    }

    // Sposta il collider sopra il modello (modelLocalCenter è già in coordinate locali, le
    // stesse in cui Unity esprime il centro di un collider): niente ridimensionamenti, solo la
    // posizione. Le forme non convesse (MeshCollider) non hanno un centro da spostare.
    private void CenterColliderOnModel(Collider target)
    {
        switch (target)
        {
            case CapsuleCollider capsule:
                capsule.center = modelLocalCenter;
                break;
            case BoxCollider box:
                box.center = modelLocalCenter;
                break;
            case SphereCollider sphere:
                sphere.center = modelLocalCenter;
                break;
        }
    }

    // Dove sta il modello rispetto al pivot del Camper (in coordinate locali, così ruota
    // insieme al mezzo): il pivot può benissimo trovarsi fuori dalla carrozzeria, e senza
    // questo scostamento in corsa il mezzo si vede staccato dal Raver che lo guida.
    private Vector3 MeasureModelCenter(Renderer[] renderers)
    {
        bool found = false;
        Bounds worldBounds = new Bounds(transform.position, Vector3.zero);

        foreach (Renderer renderer in renderers)
        {
            // Un renderer senza geometria (sul Camper è rimasto quello del cilindro segnaposto,
            // con la mesh tolta) ha un ingombro nullo piantato sul pivot: contarlo tirerebbe il
            // centro misurato via dal modello, che è proprio l'errore che stiamo correggendo.
            if (renderer.localBounds.size.sqrMagnitude <= 0.000001f)
            {
                continue;
            }

            if (!found)
            {
                worldBounds = renderer.bounds;
                found = true;
                continue;
            }

            worldBounds.Encapsulate(renderer.bounds);
        }

        return found ? transform.InverseTransformPoint(worldBounds.center) : Vector3.zero;
    }

    // Da che parte guarda il muso nella posa parcheggiata, riconosciuto invece che scritto a
    // mano: un mezzo è più lungo che largo, quindi basta prendere il pezzo di modello più
    // grande e, tra i suoi assi, quello lungo. Resta l'ambiguità muso/coda (il lato lungo è
    // lo stesso in entrambi i versi): se si vede guidare all'indietro si spunta invertNose.
    private Vector3 MeasureParkedNose(Renderer[] renderers)
    {
        Vector3 nose = parkedRotation * Vector3.forward;
        float longestSide = 0f;

        foreach (Renderer renderer in renderers)
        {
            // localBounds per ragionare sugli assi del modello, non su un riquadro allineato al
            // mondo: un mezzo messo di traverso darebbe un riquadro più largo che lungo.
            Vector3 size = renderer.localBounds.size;
            Vector3 scale = renderer.transform.lossyScale;
            size = new Vector3(
                size.x * Mathf.Abs(scale.x),
                size.y * Mathf.Abs(scale.y),
                size.z * Mathf.Abs(scale.z));

            Vector3 axis;
            float side;
            if (size.x >= size.y && size.x >= size.z)
            {
                axis = Vector3.right;
                side = size.x;
            }
            else if (size.z >= size.x && size.z >= size.y)
            {
                axis = Vector3.forward;
                side = size.z;
            }
            else
            {
                axis = Vector3.up;
                side = size.y;
            }

            if (side > longestSide)
            {
                longestSide = side;
                nose = renderer.transform.TransformDirection(axis);
            }
        }

        nose.y = 0f; // il muso punta in orizzontale: il mezzo sterza solo attorno all'asse verticale
        return nose.sqrMagnitude > 0.0001f ? nose.normalized : Vector3.forward;
    }

    // Gemello solido del collider Trigger, aggiunto a runtime quando in scena non ne è stato
    // messo uno a mano: stessa forma e stessa posizione, ma non Trigger, così da parcheggiato
    // il mezzo è un muro (vedi SetBlockingColliderEnabled, che lo spegne mentre è guidato).
    // Niente Rigidbody sul Camper: un collider così è già statico e blocca chi lo tocca.
    private Collider CreateSolidCopy(Collider source)
    {
        if (source is CapsuleCollider capsule)
        {
            CapsuleCollider copy = gameObject.AddComponent<CapsuleCollider>();
            copy.center = capsule.center;
            copy.radius = capsule.radius;
            copy.height = capsule.height;
            copy.direction = capsule.direction;
            copy.isTrigger = false;
            return copy;
        }

        if (source is BoxCollider box)
        {
            BoxCollider copy = gameObject.AddComponent<BoxCollider>();
            copy.center = box.center;
            copy.size = box.size;
            copy.isTrigger = false;
            return copy;
        }

        if (source is SphereCollider sphere)
        {
            SphereCollider copy = gameObject.AddComponent<SphereCollider>();
            copy.center = sphere.center;
            copy.radius = sphere.radius;
            copy.isTrigger = false;
            return copy;
        }

        // Forma non prevista (es. MeshCollider): niente ingombro automatico, il Camper resta
        // attraversabile finché non gli si mette a mano un collider solido in blockingCollider.
        return null;
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

            // Un Raver che sta facendo il giocoliere (vedi RaverJuggler.IsJuggling) non conta
            // come "più vicino": è impegnato lì, richiamarlo al Camper interromperebbe la sua
            // esibizione (e i bonus di gruppo) senza che il Player l'abbia scelto apposta.
            RaverJuggler juggler = raver.GetComponent<RaverJuggler>();
            if (juggler != null && juggler.IsJuggling)
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
