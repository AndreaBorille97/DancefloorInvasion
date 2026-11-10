using System.Collections.Generic;
using UnityEngine;

// Da mettere sul prefab del Raver, insieme a RaverHealth/RaverAttack/RaverChase. Gestisce
// il giocoliere: un solo Raver in scena, fatto partire da PlayerJugglerSummon a inizio partita
// nel punto in cui è stato messo (il suo "posto", homePosition). Il giocatore non lo comanda.
// Quando deve raggiungere il posto ci cammina sopra (RaverChase dirottato con
// SetForcedPosition, stesso meccanismo usato da RaverHealth per la ChilloutZone) e
// smette di attaccare finché non arriva. Appena è a destinazione (hasArrived) comincia ad
// attirare verso di sé gli Sbirro che gli si avvicinano entro attractionRadius (vedi
// FindNearestAttractor, letto da EnemyChase/EnemyRangedAttack/IdroSbirro al posto
// del loro bersaglio normale) e a "fare il giocoliere" (isPerforming): finché dura, aumenta
// leggermente vita e danno di tutti i Raver in scena in quel momento (stesso schema di
// RaverDrugBuff). Ce ne può essere solo uno alla volta in tutta la scena (vedi AnyJuggling,
// applicato da BeginJuggling). Se però chiunque (non solo uno Sbirro: anche il Robosbirro,
// il Player o il Camper) gli arriva ancora più addosso, entro dangerRadius, smette subito di
// esibirsi, cioè si spengono SOLO i bonus di vita/danno (vedi PauseForDanger): l'attrazione
// invece resta attiva per tutto il tempo da hasArrived in poi, pausa compresa, altrimenti gli
// Sbirro già in arrivo perderebbero interesse proprio nel momento in cui lo scontro comincia
// davvero, lasciandolo troppo a lungo in vita. Il Raver resta comunque fermo lì, senza
// difendersi, e riprende a dare i bonus da solo non appena non resta più nessuno entro
// dangerRadius (vedi ResumeFromDanger). Per tutta l'esibizione la sua vita è moltiplicata
// (vedi jugglingHealthMultiplier), perché stando fermo e senza difendersi verrebbe altrimenti
// abbattuto quasi subito; tolto il power-up torna un Raver come gli altri. Resta comunque un
// bersaglio comodo proprio perché non si difende: se gli Sbirro lo abbattono, RaverHealth.OnDown interrompe subito
// tutto (bonus e attrazione, e libera lo slot per un nuovo giocoliere), il Raver va a terra
// e cammina alla ChilloutZone come qualunque altro Raver esausto; una volta rianimato torna da
// solo al suo posto e ricomincia l'esibizione da capo (vedi HandleRevived).
//
// L'unico segnale a schermo delle sue aree è una luce calda tremolante il cui raggio d'azione
// è attractionRadius (vedi CreateFireLight): dangerRadius resta puramente logico, non è
// disegnato in nessun modo.
// Se sotto il Raver c'è anche un PoiFireDance (i poi di fuoco, cartella Prefabs/Flame), viene
// acceso e spento insieme a tutto il resto, cioè per tutta la durata di hasArrived: vedi
// UpdateVisuals.
[RequireComponent(typeof(RaverHealth))]
[RequireComponent(typeof(RaverAttack))]
[RequireComponent(typeof(RaverChase))]
public class RaverJuggler : MonoBehaviour
{
    [Header("Esca")]
    [Tooltip("Raggio entro cui gli Sbirro che si avvicinano a questo Raver, da quando arriva sul posto come giocoliere fino a quando va a terra (pausa per pericolo compresa), lo scelgono come bersaglio al posto del loro normale (vedi FindNearestAttractor). Non ha effetto su chi ha già il Player entro il proprio raggio di aggro: quello ha la precedenza.")]
    [SerializeField] private float attractionRadius = 6f;

    [Header("Buff di gruppo")]
    [Tooltip("Moltiplicatore di vita applicato a tutti i Raver in scena finché almeno un giocoliere sta facendo la sua esibizione.")]
    [SerializeField] private float healthBuffMultiplier = 1.2f;
    [Tooltip("Moltiplicatore di danno applicato a tutti i Raver in scena finché almeno un giocoliere sta facendo la sua esibizione.")]
    [SerializeField] private float damageBuffMultiplier = 1.2f;

    [Header("Resistenza da giocoliere")]
    [Tooltip("Di quanto viene moltiplicata la vita di questo Raver mentre fa il giocoliere. Si applica al lancio e viene tolta quando va a terra, quindi vale per tutta l'esibizione, pause per pericolo comprese: in barriera, da guardia normale, è un Raver come tutti gli altri. Serve perché il giocoliere sta fermo e non si difende, quindi senza un margine di vita in più verrebbe abbattuto quasi subito.")]
    [SerializeField] private float jugglingHealthMultiplier = 2f;

    [Header("Arrivo")]
    [Tooltip("Distanza dalla posizione di lancio sotto la quale il Raver è considerato arrivato e comincia a fare il giocoliere: tienila di poco superiore a minApproachDistance di RaverChase, altrimenti non la raggiunge mai esattamente.")]
    [SerializeField] private float arrivalDistance = 1.2f;

    [Header("Interruzione")]
    [Tooltip("Se chiunque (Sbirro, Robosbirro, Player o Camper) entra entro questa distanza dal Raver mentre sta facendo il giocoliere, si spengono subito i bonus di vita/danno del gruppo (l'attrazione resta attiva): riprendono da soli non appena non resta più nessuno entro questo stesso raggio. Tienila più piccola di attractionRadius.")]
    [SerializeField] private float dangerRadius = 2.5f;
    [Tooltip("Ogni quanti secondi, mentre si esibisce o è in pausa per pericolo, ricontrolla se c'è qualcuno entro dangerRadius.")]
    [SerializeField] private float dangerCheckInterval = 0.25f;

    [Header("Luce del fuoco")]
    [Tooltip("Colore della luce tremolante che segna l'area di attrazione: il suo raggio d'azione è sempre attractionRadius, quindi illumina esattamente la zona in cui gli Sbirro lo ingaggiano.")]
    [SerializeField] private Color fireLightColor = new Color(1f, 0.55f, 0.18f, 1f);
    [Tooltip("Intensità media della luce: il tremolio oscilla attorno a questo valore. Modificabile anche in Play Mode, l'effetto si vede subito.")]
    [SerializeField] private float fireLightIntensity = 10f;
    [Tooltip("Ampiezza del tremolio come frazione dell'intensità media: 0.35 = oscilla fra -35% e +35%.")]
    [Range(0f, 1f)]
    [SerializeField] private float fireFlickerAmount = 0.35f;
    [Tooltip("Velocità del tremolio. Valori bassi danno un respiro lento da braciere, valori alti una fiamma nervosa.")]
    [SerializeField] private float fireFlickerSpeed = 7f;
    [Tooltip("Altezza da terra a cui sta la luce, in metri: tienila all'altezza delle fiamme dei poi, non a terra, altrimenti illumina solo il pavimento.")]
    [SerializeField] private float fireLightHeight = 1.6f;

    // Il singolo giocoliere attualmente impegnato (da BeginJuggling fino a quando va a
    // terra), null se nessuno: ci può essere solo un giocoliere alla volta, vedi BeginJuggling.
    private static RaverJuggler activeJuggler;

    public static bool AnyJuggling => activeJuggler != null;

    // Con un solo giocoliere possibile alla volta (vedi activeJuggler), questo conta 0 o 1:
    // resta comunque un contatore (0 -> 1 applica il buff, 1 -> 0 lo toglie) invece di un
    // semplice bool perché lo stesso giocoliere può attraversare più cicli di
    // pausa/ripresa (vedi PauseForDanger/ResumeFromDanger), e riusa così la stessa logica
    // idempotente di EnterPerforming/EndPerforming senza bisogno di un caso speciale.
    private static int activePerformerCount;
    private static readonly List<RaverHealth> buffedHealths = new List<RaverHealth>();
    private static readonly List<RaverAttack> buffedAttacks = new List<RaverAttack>();

    // Chi sta effettivamente attirando gli Sbirro in questo momento: aggiunto una sola volta
    // all'arrivo (vedi Arrive) e tolto solo quando va a terra (vedi HandleGoDown), a
    // differenza del buff NON si spegne durante una pausa per pericolo (vedi
    // PauseForDanger/ResumeFromDanger, che toccano solo activePerformerCount).
    private static readonly List<RaverJuggler> activeAttractors = new List<RaverJuggler>();

    private RaverHealth raverHealth;
    private RaverAttack raverAttack;
    private RaverChase raverChase;

    private Vector3 targetPosition;
    // Il posto del giocoliere: dove è stato fatto partire la prima volta (vedi BeginJuggling), e
    // dove torna ogni volta che viene rianimato (vedi HandleRevived).
    private Vector3 homePosition;
    private bool hasHome;
    private bool isJuggling; // dal lancio fino a quando va a terra
    private bool hasArrived; // da quando raggiunge targetPosition a quando va a terra: governa l'attrazione, indipendente dalle pause
    private bool isPerforming; // solo mentre il buff di vita/danno è attivo, sotto hasArrived: si spegne durante una pausa per pericolo
    private bool isPausedForDanger; // sotto hasArrived, tra una pausa per qualcuno troppo vicino e la ripresa
    private float dangerCheckTimer;

    // Luce calda tremolante, unico segnale visivo delle aree del giocoliere: il suo
    // range è attractionRadius, quindi l'area illuminata coincide con quella in cui gli Sbirro
    // vengono attirati (vedi CreateFireLight/UpdateFireFlicker).
    private Light fireLight;
    // Sfasamento del tremolio, per istanza: due giocolieri accesi insieme non pulserebbero
    // all'unisono. Assegnato in Awake.
    private float flickerSeed;

    // Poi di fuoco (vedi PoiFireDance, cartella Prefabs/Flame): basta metterne uno come figlio
    // del Raver e viene agganciato da solo, niente da collegare nell'Inspector. Resta null se
    // non c'è, e in quel caso il giocoliere si comporta esattamente come prima, senza fiamme.
    private PoiFireDance poiFire;
    private bool menuHidden; // vedi SetMenuHidden
    private RaverAnimation raverAnimation; // null se il Raver non ha un modello animato

    // Usato da CamperVehicle per non mandare il giocoliere a pilotare il Camper.
    public bool IsJuggling => isJuggling;

    // true per il Raver che fa il giocoliere, anche mentre è a terra o sta tornando al suo posto
    // (a differenza di IsJuggling): ha un posto suo, non presidia un obiettivo. Usato da
    // EnemyChase.GetGuardsFor per non contarlo fra le guardie.
    public bool IsJuggler => hasHome;

    // Usati da RaverAnimation: sul posto (dall'arrivo fino a quando va a terra) e, lì, fermo coi
    // poi a terra perché qualcuno è entro dangerRadius (vedi PauseForDanger).
    public bool IsOnSpot => hasArrived;
    public bool IsPausedForDanger => isPausedForDanger;

    // Usato da EnemyChase/EnemyRangedAttack/IdroSbirro: il Transform del
    // giocoliere più vicino a fromPosition tra quelli che stanno attualmente attirando
    // (hasArrived, pausa per pericolo compresa) e hanno fromPosition entro il proprio
    // attractionRadius, altrimenti null.
    public static Transform FindNearestAttractor(Vector3 fromPosition)
    {
        RaverJuggler nearest = null;
        float nearestSqrDistance = float.MaxValue;

        foreach (RaverJuggler attractor in activeAttractors)
        {
            float sqrRadius = attractor.attractionRadius * attractor.attractionRadius;
            float sqrDistance = (attractor.transform.position - fromPosition).sqrMagnitude;
            if (sqrDistance <= sqrRadius && sqrDistance < nearestSqrDistance)
            {
                nearest = attractor;
                nearestSqrDistance = sqrDistance;
            }
        }

        return nearest != null ? nearest.transform : null;
    }

    void Awake()
    {
        raverHealth = GetComponent<RaverHealth>();
        raverAttack = GetComponent<RaverAttack>();
        raverChase = GetComponent<RaverChase>();

        raverHealth.OnDown += HandleGoDown;
        raverHealth.OnRevived += HandleRevived;

        fireLight = CreateFireLight();
        flickerSeed = Random.value * 100f;

        // includeInactive: i poi non devono vedersi finché non comincia l'esibizione, quindi
        // è lecito lasciarli già disattivati nel prefab — senza questo flag, in quel caso non
        // verrebbero trovati affatto.
        poiFire = GetComponentInChildren<PoiFireDance>(true);
        if (poiFire != null)
        {
            poiFire.gameObject.SetActive(false);
            AttachPoiToModel();
        }
    }

    // Se il Raver ha un modello animato, i poi girano attorno alle sue mani, col suo verso e a
    // tempo col suo ballo (vedi Update), invece che attorno a punti fissi: altrimenti il fuoco
    // va per conto suo mentre il modello balla.
    private void AttachPoiToModel()
    {
        raverAnimation = GetComponent<RaverAnimation>();
        Animator animator = GetComponentInChildren<Animator>();
        if (animator == null)
        {
            return;
        }

        foreach (Transform bone in animator.GetComponentsInChildren<Transform>())
        {
            if (bone.name == "LeftHand")
            {
                poiFire.handLeft = bone;
            }
            else if (bone.name == "RightHand")
            {
                poiFire.handRight = bone;
            }
        }
    }

    // Verso e tempo del modello passati ai poi, ogni fotogramma: PoiFireDance li usa nel suo
    // LateUpdate, quindi sempre dopo questo.
    private void SyncPoiToModel()
    {
        if (poiFire == null || raverAnimation == null || !raverAnimation.enabled)
        {
            return;
        }

        poiFire.SetFacing(raverAnimation.ModelForward);
        if (raverAnimation.TryGetDanceLoop(out float loops, out float loopLength))
        {
            poiFire.SyncToLoop(loops, loopLength);
        }
    }

    void OnDestroy()
    {
        raverHealth.OnDown -= HandleGoDown;
        raverHealth.OnRevived -= HandleRevived;

        // Difensivo: i Raver normalmente non vengono mai distrutti (vanno a terra, non
        // Destroy), ma se succedesse mentre sta ancora esibendosi non deve lasciare il
        // contatore/registro globali sballati per gli altri, né tenere occupato per sempre
        // l'unico slot di giocoliere disponibile.
        if (activeJuggler == this)
        {
            activeJuggler = null;
        }

        if (hasArrived)
        {
            activeAttractors.Remove(this);
        }

        if (isPerforming)
        {
            EndPerforming();
        }
    }

    void Update()
    {
        SyncPoiToModel();

        if (!isJuggling)
        {
            return;
        }

        if (!hasArrived)
        {
            // Ancora in cammino verso targetPosition.
            float sqrArrival = arrivalDistance * arrivalDistance;
            if ((transform.position - targetPosition).sqrMagnitude <= sqrArrival)
            {
                Arrive();
            }
            return;
        }

        // È sul posto: la fiamma tremola ad ogni frame, altrimenti a scatti di
        // dangerCheckInterval si vedrebbe pulsare a gradini invece che con continuità.
        UpdateFireFlicker();

        // Sia mentre si esibisce sia mentre è in pausa: ricontrolla periodicamente (non ogni
        // frame, FindObjectsByType non è gratis) se c'è qualcuno entro dangerRadius, per
        // decidere se accendere/spegnere il buff (l'attrazione, invece, resta attiva in
        // entrambi i casi, vedi Arrive/HandleGoDown).
        dangerCheckTimer += Time.deltaTime;
        if (dangerCheckTimer < dangerCheckInterval)
        {
            return;
        }
        dangerCheckTimer = 0f;

        bool anyoneNearby = IsAnyoneWithinDangerRadius();

        if (isPerforming && anyoneNearby)
        {
            PauseForDanger();
        }
        else if (isPausedForDanger && !anyoneNearby)
        {
            ResumeFromDanger();
        }
    }

    // Il Raver ha raggiunto targetPosition: da qui comincia ad attirare gli Sbirro
    // (activeAttractors, indipendente da pausa/ripresa) e parte anche il buff di gruppo
    // (EnterPerforming, che invece si spegne e riaccende con le pause).
    private void Arrive()
    {
        hasArrived = true;
        activeAttractors.Add(this);
        EnterPerforming();
    }

    // Luce puntiforme calda, figlia di questo Raver, spenta di default. Il range è
    // attractionRadius, quindi la zona illuminata coincide con quella entro cui gli Sbirro lo
    // scelgono come bersaglio, e resta allineata da sé se quel raggio viene ritarato.
    private Light CreateFireLight()
    {
        GameObject lightObject = new GameObject("JugglerFireLight");
        lightObject.transform.SetParent(transform, false);
        lightObject.transform.localPosition = Vector3.up * fireLightHeight;

        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = fireLightColor;
        light.intensity = fireLightIntensity;
        light.range = attractionRadius;
        // Niente ombre: sarebbero una luce dinamica con ombre proiettate in mezzo alla mischia,
        // cioè il caso peggiore per le prestazioni, e a terra le coprono comunque i Raver.
        light.shadows = LightShadows.None;

        lightObject.SetActive(false);
        return light;
    }

    // Tremolio della fiamma, chiamato ad ogni frame mentre la luce è accesa. Usa Perlin e non
    // Random.value: con un valore casuale indipendente ad ogni frame l'intensità salterebbe da
    // un estremo all'altro sessanta volte al secondo, e sembrerebbe uno stroboscopio invece di
    // un fuoco. Perlin dà una curva continua, quindi il respiro resta morbido.
    private void UpdateFireFlicker()
    {
        float noise = Mathf.PerlinNoise(StageClock.Time * fireFlickerSpeed, flickerSeed);
        fireLight.intensity = fireLightIntensity * Mathf.Lerp(1f - fireFlickerAmount, 1f + fireFlickerAmount, noise);
    }

    // Accende e spegne luce e poi con hasArrived, cioè dall'arrivo sul posto fino a quando va a
    // terra, pausa per pericolo compresa: qui si decide solo se ESISTONO a schermo, non se la
    // danza è in corso. La pausa per pericolo non li spegne di colpo — le fiamme scendono
    // dolcemente a terra e ci restano accese, vedi EnterPerforming/EndPerforming che chiamano
    // PoiFireDance.Resume/Lower.
    private void UpdateVisuals()
    {
        bool visible = hasArrived && !menuHidden;
        fireLight.gameObject.SetActive(visible);

        if (poiFire != null)
        {
            poiFire.gameObject.SetActive(visible);
        }
    }

    // Menu della modalità (DifficultyMenu): mentre balla in folla niente poi né luce (col gioco in
    // pausa le fiamme resterebbero congelate a mezz'aria). A partita iniziata li riaccende quando è
    // di nuovo al suo posto.
    public void SetMenuHidden(bool hidden)
    {
        menuHidden = hidden;
        UpdateVisuals();
    }

    // Chiunque (non solo gli Sbirro) entro dangerRadius da questo Raver: qualunque Sbirro
    // (compreso l'IdroSbirro, che usa anche lui EnemyHealth), il Robosbirro, il Player o il
    // Camper (parcheggiato o in guida, la sua sola presenza fisica basta).
    private bool IsAnyoneWithinDangerRadius()
    {
        float sqrRadius = dangerRadius * dangerRadius;

        foreach (EnemyHealth enemy in FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None))
        {
            if ((enemy.transform.position - transform.position).sqrMagnitude <= sqrRadius)
            {
                return true;
            }
        }


        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null && (player.transform.position - transform.position).sqrMagnitude <= sqrRadius)
        {
            return true;
        }

        foreach (CamperVehicle camper in FindObjectsByType<CamperVehicle>(FindObjectsSortMode.None))
        {
            if ((camper.transform.position - transform.position).sqrMagnitude <= sqrRadius)
            {
                return true;
            }
        }

        return false;
    }

    // Chiamato da PlayerJugglerSummon a inizio partita, e da HandleRevived dopo ogni
    // rianimazione: lo dirotta subito verso position e gli fa smettere di attaccare (è
    // impegnato a raggiungerla, poi a esibirsi). Ignorato se già a terra, già impegnato come
    // giocoliere, o se c'è già un altro giocoliere in scena (ce ne può essere solo uno alla
    // volta, vedi activeJuggler).
    public void BeginJuggling(Vector3 position)
    {
        if (raverHealth.IsDown || isJuggling || AnyJuggling)
        {
            return;
        }

        if (!hasHome)
        {
            homePosition = position;
            hasHome = true;
        }

        activeJuggler = this;
        isJuggling = true;

        // Vita moltiplicata per tutta la durata dell'esibizione, non solo mentre dà i bonus:
        // legarla a isPerforming significherebbe togliergli il margine di vita proprio durante
        // una pausa per pericolo, cioè nell'unico momento in cui ha davvero qualcuno addosso.
        // MultiplyHealth scala insieme vita massima e attuale, quindi la percentuale non cambia
        // e la divisione in HandleGoDown riporta esattamente ai valori di partenza.
        raverHealth.MultiplyHealth(jugglingHealthMultiplier);

        hasArrived = false;
        isPausedForDanger = false;
        dangerCheckTimer = 0f;
        targetPosition = position;

        raverAttack.enabled = false;
        raverAttack.CancelAttack(); // se aveva già un colpo in carica, enabled = false da solo non basterebbe a fermarlo (vedi RaverAttack.CancelAttack)
        raverChase.SetForcedPosition(position);
        UpdateVisuals();
    }

    // Chiamato quando questo Raver viene rianimato (vedi RaverHealth.OnRevived): se è il
    // giocoliere, cioè ha già un posto, ci torna a piedi e lì ricomincia da solo l'esibizione.
    // RaverHealth.Reactivate lo ha appena rimesso da guardia normale con l'attacco acceso:
    // BeginJuggling lo rispegne e lo dirotta verso homePosition.
    private void HandleRevived()
    {
        if (hasHome)
        {
            BeginJuggling(homePosition);
        }
    }

    // Qualcuno è entrato entro dangerRadius mentre si esibiva: interrompe subito SOLO il buff
    // di vita/danno (riusa EndPerforming), ma resta "in gioco" (isJuggling, hasArrived) in
    // attesa che l'area si liberi, senza muoversi né rimettersi ad attaccare, e continuando ad
    // attirare gli Sbirro come prima (l'attrazione non dipende da isPerforming, vedi Arrive).
    private void PauseForDanger()
    {
        isPausedForDanger = true;
        EndPerforming();
    }

    // L'area entro dangerRadius si è liberata: riprende il buff di gruppo da dove l'aveva lasciato.
    private void ResumeFromDanger()
    {
        isPausedForDanger = false;
        EnterPerforming();
    }

    private void EnterPerforming()
    {
        isPerforming = true;

        if (poiFire != null)
        {
            poiFire.Resume();
        }

        activePerformerCount++;
        if (activePerformerCount == 1)
        {
            ApplyGroupBuff();
        }

        UpdateVisuals();
    }

    // Applica il buff a tutti i Raver presenti in scena in questo momento (chi nasce dopo
    // non ne beneficia, stesso comportamento già accettato altrove, vedi RaverDrugBuff): la
    // lista viene tenuta da parte così, alla fine, si toglie esattamente lo stesso buff agli
    // stessi Raver, anche se nel frattempo ne sono comparsi altri.
    private void ApplyGroupBuff()
    {
        buffedHealths.Clear();
        buffedAttacks.Clear();

        foreach (RaverHealth health in FindObjectsByType<RaverHealth>(FindObjectsSortMode.None))
        {
            health.MultiplyHealth(healthBuffMultiplier);
            health.SetGroupBuffTint(true); // tutti i Raver diventano rossi finché il buff resta attivo (vedi RaverHealth.SetGroupBuffTint)
            buffedHealths.Add(health);
        }

        foreach (RaverAttack attack in FindObjectsByType<RaverAttack>(FindObjectsSortMode.None))
        {
            attack.MultiplyDamage(damageBuffMultiplier);
            buffedAttacks.Add(attack);
        }
    }

    private void RevertGroupBuff()
    {
        foreach (RaverHealth health in buffedHealths)
        {
            if (health != null)
            {
                health.MultiplyHealth(1f / healthBuffMultiplier);
                health.SetGroupBuffTint(false);
            }
        }

        foreach (RaverAttack attack in buffedAttacks)
        {
            if (attack != null)
            {
                attack.MultiplyDamage(1f / damageBuffMultiplier);
            }
        }

        buffedHealths.Clear();
        buffedAttacks.Clear();
    }

    // Chiamato quando questo Raver va a terra (vedi RaverHealth.OnDown): l'esibizione finisce
    // subito, bonus e attrazione compresi. RaverHealth.GoDown si occupa già da sé di rimandare
    // raverAttack.enabled a false e di dirottare RaverChase verso la ChilloutZone (o il
    // fallback), quindi qui basta chiudere lo stato del giocoliere.
    private void HandleGoDown()
    {
        if (!isJuggling)
        {
            return;
        }

        isJuggling = false;
        isPausedForDanger = false;

        // Torna la vita da Raver normale. Qui currentHealth è già a zero (GoDown la azzera
        // prima di lanciare OnDown), quindi la divisione tocca di fatto solo il massimo: per
        // essere rianimato dovrà risalire alla soglia normale, non a quella raddoppiata.
        raverHealth.MultiplyHealth(1f / jugglingHealthMultiplier);

        if (activeJuggler == this)
        {
            activeJuggler = null;
        }

        if (hasArrived)
        {
            hasArrived = false;
            activeAttractors.Remove(this);
        }

        if (isPerforming)
        {
            EndPerforming();
        }

        UpdateVisuals();
    }

    private void EndPerforming()
    {
        isPerforming = false;

        // Le fiamme non si spengono: scendono dolcemente a terra e restano lì accese finché
        // l'area non si libera (vedi PoiFireDance.Lower). Il GameObject resta quindi attivo,
        // a differenza di quando va a terra davvero (vedi UpdateVisuals/hasArrived).
        if (poiFire != null)
        {
            poiFire.Lower();
        }

        activePerformerCount--;
        if (activePerformerCount == 0)
        {
            RevertGroupBuff();
        }

        UpdateVisuals();
    }
}
