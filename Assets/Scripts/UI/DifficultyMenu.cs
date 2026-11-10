using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Le due scelte che compaiono appena parte la partita: NORMALE o FACILE (vedi GameDifficulty).
// Finché non si sceglie il gioco resta in pausa (timeScale 0, come GameOverUI/WinUI), tutti i
// Raver ballano sotto cassa e la camera, più indietro, inquadra sound system e folla (SetStage).
// Scelta la modalità la partita comincia subito (Sbirro compresi): la camera va al centro della
// mappa e i Raver tornano camminando alle loro postazioni (Choose/UpdateIntro). Il Player fino ad
// allora non c'è (HidePlayer): appena i Raver della Console escono dall'inquadratura la camera va
// sul suo punto di partenza e lui ci cade dall'alto; atterrato, torna comandabile (Finish).
// Si sceglie con croce direzionale/levetta su e giù e X (tasto sud) del gamepad, con frecce/W-S
// e Invio/Spazio della tastiera, oppure cliccando col mouse.
//
// Si aggiunge da solo all'avvio in ogni scena con un Player (vedi Create): non serve metterlo
// a mano in scena.
public class DifficultyMenu : MonoBehaviour
{
    private static readonly string[] Labels = { "NORMALE", "FACILE" };
    private static readonly string[] Hints =
    {
        "",
        "Raver più resistenti, Sbirro meno aggressivi, poteri automatici",
    };

    [Tooltip("Metri fra il bordo del sound system e la prima fila della folla.")]
    [SerializeField] private float crowdGap = 1.8f;
    [Tooltip("Al massimo quanti metri può sporgere il sound system dal suo centro verso la pista, per il calcolo della prima fila.")]
    [SerializeField] private float maxSoundReach = 6f;
    [Tooltip("Metri fra un Raver e l'altro nella folla.")]
    [SerializeField] private float crowdSpacing = 2.2f;
    [Tooltip("Quanto si allontana la camera rispetto alla distanza di gioco (1 = uguale).")]
    [SerializeField] private float cameraDistanceScale = 1.2f;
    [Tooltip("Dove punta la camera fra il sound system (0) e il centro della folla (1).")]
    [Range(0f, 1f)]
    [SerializeField] private float cameraCrowdBias = 0.35f;

    [Header("Movimento della folla")]
    [Tooltip("Metri al massimo che ognuno si sposta attorno al suo posto, per conto suo.")]
    [SerializeField] private float driftAmplitude = 0.3f;
    [Tooltip("Velocità del movimento sul posto: a ognuno ne tocca una a caso fra questi due valori.")]
    [SerializeField] private Vector2 driftSpeedRange = new Vector2(0.5f, 1.4f);

    [Header("Entrata in scena")]
    [Tooltip("Al massimo quanti secondi si aspetta, a partita iniziata, prima che il Player scenda comunque.")]
    [SerializeField] private float maxIntroSeconds = 8f;
    [Tooltip("Quanto vanno più veloci i Raver dalla folla alle postazioni (2.5 = due volte e mezza il passo normale). Arrivati, tornano al passo di sempre.")]
    [SerializeField] private float introRaverSpeedMultiplier = 2.5f;
    [Tooltip("Quanti secondi prima che i Raver della Console escano dall'inquadratura (o che arrivino tutti) il Player comincia a scendere.")]
    [SerializeField] private float dropLeadSeconds = 2f;
    [Tooltip("Entro quanti metri dalla sua postazione un Raver conta come arrivato.")]
    [SerializeField] private float homeRadius = 2f;
    [Tooltip("Secondi di attesa, a Raver della Console usciti dall'inquadratura (o tutti arrivati), prima che la camera vada dove cadrà il Player.")]
    [SerializeField] private float introHoldSeconds = 0.6f;
    [Tooltip("Quanto si allontana la camera al centro della mappa, rispetto alla distanza di gioco.")]
    [SerializeField] private float introCameraDistanceScale = 1.6f;

    [Header("Caduta del Player")]
    [Tooltip("Da quanti metri sopra il suo punto di partenza cade il Player.")]
    [SerializeField] private float dropHeight = 25f;
    [Tooltip("Accelerazione della caduta, in m/s² (9.8 = gravità vera, più alto = più secco).")]
    [SerializeField] private float fallGravity = 90f;
    [Tooltip("Quanto si piega all'atterraggio: 0.25 = si abbassa del 25% e torna dritto.")]
    [Range(0f, 0.6f)]
    [SerializeField] private float squashAmount = 0.22f;
    [Tooltip("Secondi della flessione all'atterraggio, dal tocco a terra a di nuovo dritto.")]
    [SerializeField] private float squashSeconds = 0.45f;
    [Tooltip("Fin dove arriva l'anello d'urto all'atterraggio, in metri (CyberGiorgia: 8).")]
    [SerializeField] private float landingWaveRadius = 6f;
    [SerializeField] private Color landingWaveColor = new Color(0.86f, 0.74f, 0.52f, 0.9f);
    [Tooltip("Quanti puffi compongono l'anello d'urto.")]
    [SerializeField] private int landingWaveParticles = 120;
    [Tooltip("Tremolio dello schermo all'atterraggio, da 0 a 1 (CameraShake).")]
    [Range(0f, 1f)]
    [SerializeField] private float landingShake = 0.6f;
    [Tooltip("Secondi fra l'atterraggio e l'inizio della partita.")]
    [SerializeField] private float landingHoldSeconds = 0.8f;

    private enum IntroPhase { Walk, ToLanding, Fall, Landed }

    private int selected;
    private bool stageSet;
    private bool inIntro;
    private IntroPhase introPhase;
    private float phaseTime;
    private float holdTime;
    private float fallSpeed;
    private PlayerMovement player;
    private readonly List<Transform> consoleWalkers = new List<Transform>();
    private Vector3 playerStart;
    private Vector3 playerScale;
    private float pivotAboveFeet;
    private bool interpolationSaved;
    private RigidbodyInterpolation savedInterpolation;
    private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
    private readonly List<(Transform raver, Vector3 destination)> walkers = new List<(Transform, Vector3)>();
    private readonly List<MonoBehaviour> playerControls = new List<MonoBehaviour>();
    private RaverJuggler hiddenJuggler;
    private readonly Dictionary<Transform, Vector3> lastWalkerPositions = new Dictionary<Transform, Vector3>();
    private readonly Dictionary<Transform, Vector3> walkerVelocities = new Dictionary<Transform, Vector3>();
    private readonly HashSet<RaverChase> hurriedRavers = new HashSet<RaverChase>();
    private Vector3 crowdFront;
    private Vector3 crowdSide;
    private readonly List<(Transform raver, Vector3 basePosition, float seed, float speed)> dancers = new List<(Transform, Vector3, float, float)>();
    private readonly List<(Transform raver, Vector3 position, Quaternion rotation)> stagedRavers = new List<(Transform, Vector3, Quaternion)>();
    private GUIStyle titleStyle;
    private GUIStyle buttonStyle;
    private GUIStyle hintStyle;
    private Texture2D backdrop;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (FindAnyObjectByType<PlayerMovement>() == null)
        {
            return;
        }

        new GameObject("DifficultyMenu").AddComponent<DifficultyMenu>();
    }

    // Letto da StageClock/PlayAllAnimations: col menu aperto le animazioni del palco vanno in tempo reale.
    public static bool IsOpen { get; private set; }

    // Letto dagli spawner dei power-up (AmmoSpawner & co.): gli Sbirro arrivano da subito, i
    // power-up solo a Player atterrato. Vero di partenza, così una scena senza menu non li blocca.
    public static bool PlayerLanded { get; private set; } = true;

    void Awake()
    {
        IsOpen = true;
        PlayerLanded = false;
        Time.timeScale = 0f;
        HideHud(true);
        backdrop = new Texture2D(1, 1);
        backdrop.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.35f));
        backdrop.Apply();
    }

    // Finish distrugge il menu appena il Player è atterrato (o se si salta l'entrata in scena).
    void OnDestroy()
    {
        IsOpen = false;
        PlayerLanded = true;
    }

    // Pad e minimappa (con la barra vita sopra) spariscono durante il menu: trasparenza a zero con
    // un CanvasGroup invece di spegnere gli oggetti, così i loro Awake/Start girano come sempre.
    private static void HideHud(bool hidden)
    {
        Component[] parts = { FindAnyObjectByType<FuelGaugeHUD>(), FindAnyObjectByType<MinimapUI>() };
        foreach (Component part in parts)
        {
            if (part == null)
            {
                continue;
            }

            CanvasGroup group = part.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = part.gameObject.AddComponent<CanvasGroup>();
            }
            group.alpha = hidden ? 0f : 1f;
        }
    }

    void Update()
    {
        StageClock.AdvanceMenu(Time.unscaledDeltaTime);

        // Al primo Update e non in Start: CameraFollow ricava la sua distanza dal Player, e
        // PlayerJugglerSummon il posto del giocoliere, nei loro Start, che devono essere già passati.
        if (!stageSet)
        {
            stageSet = true;
            SetStage();
        }

        Gamepad pad = Gamepad.current;
        Keyboard keyboard = Keyboard.current;
        bool confirm = (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame))
            || (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame));

        if (inIntro)
        {
            // Lo stesso tasto della scelta salta l'entrata in scena.
            if (UpdateIntro() || confirm)
            {
                Finish();
            }
            return;
        }

        SwayCrowd();

        bool up = (pad != null && (pad.dpad.up.wasPressedThisFrame || pad.leftStick.up.wasPressedThisFrame))
            || (keyboard != null && (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame));
        bool down = (pad != null && (pad.dpad.down.wasPressedThisFrame || pad.leftStick.down.wasPressedThisFrame))
            || (keyboard != null && (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame));

        if (up || down)
        {
            selected = 1 - selected;
        }

        if (confirm)
        {
            Choose(selected);
        }
    }

    // Scelta fatta: sparisce il menu e la partita comincia subito, Sbirro compresi. I Raver tornano
    // alle loro postazioni da soli (RaverChase li riporta al punto di guardia, il giocoliere al suo
    // posto, dove riaccende i poi), la camera va al centro della mappa e il Player, che ancora non
    // c'è, cade dall'alto appena i Raver della Console sono usciti dall'inquadratura (UpdateIntro).
    private void Choose(int index)
    {
        if (inIntro)
        {
            return;
        }

        GameDifficulty.Choose(index == 1 ? GameDifficulty.Mode.Easy : GameDifficulty.Mode.Normal);
        inIntro = true;
        holdTime = 0f;
        SetPhase(IntroPhase.Walk);

        walkers.Clear();
        consoleWalkers.Clear();
        DJConsoleHealth console = FindAnyObjectByType<DJConsoleHealth>();
        foreach ((Transform raver, Vector3 position, Quaternion rotation) in stagedRavers)
        {
            if (raver == null)
            {
                continue;
            }

            walkers.Add((raver, position));
            if (console != null && raver.TryGetComponent(out RaverHealth health) && EnemyChase.GetGuardedObjective(health) == console.transform)
            {
                consoleWalkers.Add(raver);
            }
        }
        ReleaseRavers();

        HideHud(false);
        IsOpen = false;
        Time.timeScale = 1f;

        if (CameraFollow.Instance != null && FloorBounds.Instance != null)
        {
            CameraFollow.Instance.Focus(FloorBounds.Instance.Bounds.center, introCameraDistanceScale);
        }
    }

    // Fasi dell'entrata, in ordine; true quando è tutto finito e può cominciare la partita.
    private bool UpdateIntro()
    {
        float dt = Time.unscaledDeltaTime;
        phaseTime += dt;
        // I Raver camminano da soli (gioco già in corso): qui si guarda solo se sono arrivati.
        bool allArrived = RaversHome();
        switch (introPhase)
        {
            case IntroPhase.Walk:
                // Il Player scende dropLeadSeconds prima che i Raver diretti alla Console escano
                // dall'inquadratura del centro mappa (o che arrivino tutti), più un attimo di
                // respiro, o comunque dopo un po' se qualcosa si incastra. "Prima" vuol dire
                // guardando dove saranno fra dropLeadSeconds, al passo che stanno tenendo ora.
                TrackWalkerVelocities(dt);
                bool cameraArrived = CameraFollow.Instance == null || FloorBounds.Instance == null || CameraFollow.Instance.IsAtFocus;
                if (cameraArrived && (allArrived || AllHomeSoon() || ConsoleRaversOutOfView()))
                {
                    holdTime += dt;
                }
                if (holdTime >= introHoldSeconds || phaseTime > maxIntroSeconds + 3f - dropLeadSeconds)
                {
                    if (CameraFollow.Instance != null)
                    {
                        CameraFollow.Instance.Focus(playerStart);
                    }
                    SetPhase(IntroPhase.ToLanding);
                }
                return false;

            case IntroPhase.ToLanding:
                if (player == null)
                {
                    SetPhase(IntroPhase.Landed);
                }
                else if (CameraFollow.Instance == null || CameraFollow.Instance.IsAtFocus || phaseTime > 3f)
                {
                    PlacePlayer(playerStart + Vector3.up * VisibleDropHeight());
                    ShowPlayer(true);
                    fallSpeed = 0f;
                    SetPhase(IntroPhase.Fall);
                }
                return false;

            case IntroPhase.Fall:
                // Caduta con accelerazione costante, come un corpo che cade davvero.
                fallSpeed += fallGravity * dt;
                Vector3 position = player.transform.position;
                position.y -= fallSpeed * dt;
                if (position.y <= playerStart.y)
                {
                    PlacePlayer(playerStart);
                    CameraShake.Add(landingShake);
                    // L'anello d'urto di CyberGiorgia quando atterra.
                    CyberGiorgiaBoss.SpawnSandWave(playerStart, landingWaveRadius, landingWaveColor, landingWaveParticles, true);
                    SetPhase(IntroPhase.Landed);
                }
                else
                {
                    PlacePlayer(position);
                }
                return false;

            default:
                SquashPlayer(phaseTime / Mathf.Max(squashSeconds, 0.01f));
                // Rimesso dritto, il Player è di nuovo comandabile.
                return phaseTime >= Mathf.Max(landingHoldSeconds, squashSeconds);
        }
    }

    // Il Player non ha un'animazione di atterraggio: si finge la flessione schiacciandolo un po'
    // in altezza (e allargandolo appena) e rimettendolo dritto, tenendo i piedi a terra. t va da
    // 0 (appena toccato terra) a 1 (di nuovo dritto): giù in fretta, su più piano.
    private void SquashPlayer(float t)
    {
        if (player == null)
        {
            return;
        }

        float k = 0f;
        if (t < 1f)
        {
            const float down = 0.25f; // frazione del tempo spesa a piegarsi
            k = t < down ? Mathf.Sin(t / down * Mathf.PI * 0.5f) : Mathf.Cos((t - down) / (1f - down) * Mathf.PI * 0.5f);
        }

        float height = 1f - squashAmount * k;
        float width = 1f + squashAmount * 0.5f * k;
        player.transform.localScale = new Vector3(playerScale.x * width, playerScale.y * height, playerScale.z * width);
        // Lo schiacciamento avviene attorno al pivot: lo si abbassa quanto basta a non staccare i piedi.
        PlacePlayer(playerStart - Vector3.up * (pivotAboveFeet * (1f - height)));
    }

    private void SetPhase(IntroPhase phase)
    {
        introPhase = phase;
        phaseTime = 0f;
    }

    // true se tutti i Raver sono (quasi) alla loro postazione, o a terra. Il giocoliere, appena
    // arrivato, riaccende i poi e si mette a giocolare.
    private bool RaversHome()
    {
        bool allHome = true;
        foreach ((Transform raver, Vector3 destination) in walkers)
        {
            if (raver == null)
            {
                continue;
            }

            bool down = raver.TryGetComponent(out RaverHealth health) && health.IsDown;
            Vector3 offset = raver.position - destination;
            offset.y = 0f;
            bool home = offset.sqrMagnitude <= homeRadius * homeRadius;
            if (home || down)
            {
                RestoreSpeed(raver.GetComponent<RaverChase>());
            }
            if (down)
            {
                continue;
            }
            if (hiddenJuggler != null && raver == hiddenJuggler.transform && home)
            {
                ShowJuggler();
            }
            allHome &= home;
        }
        return allHome;
    }

    // Arrivato (o a terra): di nuovo al passo di sempre. Una volta sola per Raver.
    private void RestoreSpeed(RaverChase chase)
    {
        if (chase != null && hurriedRavers.Remove(chase))
        {
            chase.MultiplyMoveSpeed(1f / introRaverSpeedMultiplier);
            chase.GetComponent<RaverAnimation>()?.SetWalkSpeedMultiplier(1f);
        }
    }

    private void ShowJuggler()
    {
        if (hiddenJuggler != null)
        {
            hiddenJuggler.SetMenuHidden(false);
            hiddenJuggler = null;
        }
    }

    // Tutti i Raver che vanno a presidiare la Console (EnemyChase.GetGuardedObjective) sono fuori
    // dall'inquadratura. false se non ce n'è nessuno: allora si aspetta che arrivino tutti.
    private bool ConsoleRaversOutOfView()
    {
        Camera cam = Camera.main;
        if (cam == null || consoleWalkers.Count == 0)
        {
            return false;
        }

        foreach (Transform raver in consoleWalkers)
        {
            if (raver == null)
            {
                continue;
            }

            Vector3 viewport = cam.WorldToViewportPoint(PredictedPosition(raver) + Vector3.up);
            bool inView = viewport.z > 0f && viewport.x > -0.05f && viewport.x < 1.05f && viewport.y > -0.05f && viewport.y < 1.05f;
            if (inView)
            {
                return false;
            }
        }
        return true;
    }

    // Velocità di ogni Raver in cammino, misurata frame per frame e ammorbidita (RaverChase si
    // ferma un attimo quando cambia bersaglio, e una misura secca farebbe saltare la previsione).
    private void TrackWalkerVelocities(float dt)
    {
        if (dt <= 0f)
        {
            return;
        }

        foreach ((Transform raver, Vector3 destination) in walkers)
        {
            if (raver == null)
            {
                continue;
            }

            Vector3 position = raver.position;
            if (lastWalkerPositions.TryGetValue(raver, out Vector3 last))
            {
                Vector3 measured = (position - last) / dt;
                measured.y = 0f;
                walkerVelocities[raver] = walkerVelocities.TryGetValue(raver, out Vector3 smoothed)
                    ? Vector3.Lerp(smoothed, measured, Mathf.Clamp01(dt * 5f))
                    : measured;
            }
            lastWalkerPositions[raver] = position;
        }
    }

    // Fra dropLeadSeconds, al passo attuale, saranno tutti alla loro postazione (o a terra)?
    private bool AllHomeSoon()
    {
        foreach ((Transform raver, Vector3 destination) in walkers)
        {
            if (raver == null || (raver.TryGetComponent(out RaverHealth health) && health.IsDown))
            {
                continue;
            }

            Vector3 offset = PredictedPosition(raver) - destination;
            offset.y = 0f;
            if (offset.sqrMagnitude > homeRadius * homeRadius)
            {
                return false;
            }
        }
        return true;
    }

    // Dove sarà il Raver fra dropLeadSeconds al passo attuale, senza oltrepassare la sua postazione.
    private Vector3 PredictedPosition(Transform raver)
    {
        Vector3 position = raver.position;
        if (!walkerVelocities.TryGetValue(raver, out Vector3 velocity))
        {
            return position;
        }

        Vector3 predicted = position + velocity * dropLeadSeconds;
        foreach ((Transform walker, Vector3 destination) in walkers)
        {
            if (walker == raver)
            {
                Vector3 toDestination = destination - position;
                toDestination.y = 0f;
                if ((predicted - position).sqrMagnitude > toDestination.sqrMagnitude)
                {
                    predicted = position + toDestination;
                }
                break;
            }
        }
        return predicted;
    }

    // Fine dell'entrata: il Player (anche se l'entrata è stata saltata a metà) al suo posto,
    // visibile e di nuovo comandabile, e la camera torna a seguirlo.
    private void Finish()
    {
        walkers.Clear();
        UnfocusCamera();
        ShowJuggler();
        // Chi è ancora per strada finisce il tragitto al passo normale: il menu sta per sparire.
        foreach (RaverChase chase in new List<RaverChase>(hurriedRavers))
        {
            RestoreSpeed(chase);
        }
        if (player != null)
        {
            player.transform.localScale = playerScale;
            PlacePlayer(playerStart);
            ShowPlayer(true);
            RestorePlayerInterpolation();
            SetPlayerControl(true);
        }
        Destroy(backdrop);
        Destroy(gameObject);
    }

    // Finché non è atterrato il Player non si muove, non spara e non lancia poteri: la partita sta
    // già girando, e da lassù sparerebbe o si sposterebbe per conto suo.
    private void SetPlayerControl(bool on)
    {
        foreach (MonoBehaviour control in playerControls)
        {
            if (control != null)
            {
                control.enabled = on;
            }
        }
    }

    // Durante il menu e la camminata dei Raver il Player non c'è: invisibile e in alto, sopra il suo
    // punto di partenza, da dove poi cade (vedi IntroPhase.Fall).
    private void HidePlayer()
    {
        player = FindAnyObjectByType<PlayerMovement>();
        if (player == null)
        {
            return;
        }

        playerStart = player.transform.position;
        playerScale = player.transform.localScale;
        // Distanza fra il pivot e la pianta dei piedi, per la flessione all'atterraggio (SquashPlayer).
        float feet = float.MaxValue;
        foreach (Renderer r in player.GetComponentsInChildren<Renderer>())
        {
            if (r.enabled && !(r is ParticleSystemRenderer))
            {
                feet = Mathf.Min(feet, r.bounds.min.y);
            }
        }
        pivotAboveFeet = feet < float.MaxValue ? Mathf.Max(0f, playerStart.y - feet) : 0f;
        hiddenRenderers.Clear();
        foreach (Renderer r in player.GetComponentsInChildren<Renderer>())
        {
            if (r.enabled)
            {
                hiddenRenderers.Add(r);
            }
        }
        ShowPlayer(false);
        PlacePlayer(playerStart + Vector3.up * dropHeight);

        playerControls.Clear();
        playerControls.Add(player);
        playerControls.Add(player.GetComponent<PlayerShooting>());
        playerControls.Add(player.GetComponent<PlayerDash>());
        playerControls.Add(player.GetComponent<PlayerHoffman>());
        playerControls.Add(player.GetComponent<PlayerKhole>());
        playerControls.Add(player.GetComponent<PlayerSerotonin>());
        playerControls.Add(player.GetComponent<EasyAutoPowers>());
        playerControls.RemoveAll(control => control == null);
        SetPlayerControl(false);
    }

    // Solo i Renderer accesi all'inizio: quelli spenti apposta (es. il LED del berserk) restano spenti.
    private void ShowPlayer(bool visible)
    {
        foreach (Renderer r in hiddenRenderers)
        {
            if (r != null)
            {
                r.enabled = visible;
            }
        }
    }

    private void PlacePlayer(Vector3 position)
    {
        player.transform.position = position;
        if (player.TryGetComponent(out Rigidbody body))
        {
            // PlayerMovement accende l'interpolazione: a gioco in pausa Unity riscriverebbe ogni
            // frame la posizione con l'ultima della fisica (a terra), e la caduta non si vedrebbe.
            // Spenta finché dura l'entrata, riaccesa in Finish.
            if (!interpolationSaved)
            {
                savedInterpolation = body.interpolation;
                interpolationSaved = true;
            }
            body.interpolation = RigidbodyInterpolation.None;
            body.position = position;
        }
    }

    private void RestorePlayerInterpolation()
    {
        if (interpolationSaved && player != null && player.TryGetComponent(out Rigidbody body))
        {
            body.interpolation = savedInterpolation;
        }
    }

    // Altezza da cui cadere perché la caduta si veda tutta: appena sopra il bordo alto dello
    // schermo, con la camera già sul punto di atterraggio. Mai più di dropHeight.
    private float VisibleDropHeight()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            return dropHeight;
        }

        for (float h = 1f; h < dropHeight; h += 0.5f)
        {
            Vector3 viewport = cam.WorldToViewportPoint(playerStart + Vector3.up * h);
            if (viewport.z <= 0f || viewport.y > 1.1f)
            {
                return h;
            }
        }
        return dropHeight;
    }

    // Durante il menu tutti i Raver (anche quelli della Console e il giocoliere) ballano in folla
    // davanti al sound system, girati verso le casse, e la camera si allontana per inquadrare
    // casse e folla insieme. Ognuno si segna dov'era: a partita iniziata ci torna camminando.
    private void SetStage()
    {
        SoundSystem sound = FindAnyObjectByType<SoundSystem>();
        if (sound == null)
        {
            return;
        }

        Vector3 soundPosition = sound.transform.position;
        Vector3 floorCenter = FloorBounds.Instance != null ? FloorBounds.Instance.Bounds.center : Vector3.zero;
        // Le casse suonano verso la pista: la folla sta dalla parte del centro del pavimento.
        Vector3 front = floorCenter - soundPosition;
        front.y = 0f;
        front = front.sqrMagnitude > 0.01f ? front.normalized : sound.transform.forward;
        Vector3 side = Vector3.Cross(Vector3.up, front);
        crowdFront = front;
        crowdSide = side;
        HidePlayer();
        // Copia in array dal registry: MixModels rimescola sul posto, e la lista condivisa
        // RaverHealth.All non va toccata. Una tantum all'apertura del menu.
        RaverHealth[] ravers = RaverHealth.All.ToArray();

        // Quanto sporge il sound system verso la pista, dal suo centro: la folla parte da lì. Solo
        // i collider solidi: i Renderer comprendono anche particelle e simili, con bounds che
        // possono stare ovunque (portavano la folla in mezzo al mare). Limitato per sicurezza.
        float soundRadius = 0f;
        foreach (Collider c in sound.GetComponentsInChildren<Collider>())
        {
            if (c.isTrigger)
            {
                continue;
            }

            Bounds b = c.bounds;
            Vector3 toCenter = b.center - soundPosition;
            float reach = Vector3.Dot(toCenter, front) + Mathf.Abs(front.x) * b.extents.x + Mathf.Abs(front.z) * b.extents.z;
            soundRadius = Mathf.Max(soundRadius, reach);
        }
        soundRadius = Mathf.Clamp(soundRadius, 0f, maxSoundReach);
        Debug.Log($"[DifficultyMenu] folla a {soundRadius + crowdGap:0.0} m dal centro del sound system, {ravers.Length} Raver.");

        int columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(ravers.Length * 2f)));
        int rows = Mathf.CeilToInt(ravers.Length / (float)columns);
        float firstRow = soundRadius + crowdGap;
        ravers = MixModels(ravers, columns);
        for (int i = 0; i < ravers.Length; i++)
        {
            Transform raver = ravers[i].transform;
            stagedRavers.Add((raver, raver.position, raver.rotation));

            int row = i / columns;
            int column = i % columns;
            // Righe sfalsate di mezzo posto e un filo di disordine: una folla, non un plotone.
            float across = (column - (columns - 1) * 0.5f + (row % 2) * 0.5f) * crowdSpacing;
            Vector3 jitter = new Vector3(Random.Range(-0.25f, 0.25f), 0f, Random.Range(-0.25f, 0.25f));
            Vector3 position = soundPosition + front * (firstRow + row * crowdSpacing) + side * across + jitter;
            position.y = raver.position.y;
            raver.SetPositionAndRotation(position, Quaternion.LookRotation(-front, Vector3.up));
            dancers.Add((raver, position, Random.Range(0f, 1000f), Random.Range(driftSpeedRange.x, driftSpeedRange.y)));

            ravers[i].GetComponent<RaverAnimation>()?.SetMenuDance(soundPosition);
            if (ravers[i].TryGetComponent(out RaverJuggler juggler))
            {
                // In folla balla come gli altri, senza poi: li riaccende a partita iniziata, quando
                // RaverJuggler lo riporta al suo posto (ReleaseRavers).
                juggler.SetMenuHidden(true);
            }
        }

        if (CameraFollow.Instance != null)
        {
            Vector3 crowdCenter = soundPosition + front * (firstRow + (rows - 1) * crowdSpacing * 0.5f);
            CameraFollow.Instance.Focus(Vector3.Lerp(soundPosition, crowdCenter, cameraCrowdBias), cameraDistanceScale);
        }
    }

    // Ordine dei posti in folla in cui i Raver con lo stesso modello stanno il più lontano possibile:
    // posto per posto si sceglie il modello che "pesa" meno sui posti già occupati intorno (un
    // doppione accanto pesa molto, a due posti di distanza poco), preferendo a parità i modelli con
    // più Raver ancora da piazzare, così i più comuni si spargono su tutta la folla invece di
    // ammucchiarsi in fondo. Un pizzico di caso evita che si formi un motivo regolare.
    private static RaverHealth[] MixModels(RaverHealth[] ravers, int columns)
    {
        Dictionary<int, Queue<RaverHealth>> groups = new Dictionary<int, Queue<RaverHealth>>();
        foreach (RaverHealth raver in ravers)
        {
            int key = ModelKey(raver);
            if (!groups.TryGetValue(key, out Queue<RaverHealth> group))
            {
                group = new Queue<RaverHealth>();
                groups.Add(key, group);
            }
            group.Enqueue(raver);
        }

        int[] placedKeys = new int[ravers.Length];
        RaverHealth[] mixed = new RaverHealth[ravers.Length];
        for (int i = 0; i < ravers.Length; i++)
        {
            int largest = 1;
            foreach (Queue<RaverHealth> group in groups.Values)
            {
                largest = Mathf.Max(largest, group.Count);
            }

            int best = 0;
            float bestScore = float.MaxValue;
            foreach (KeyValuePair<int, Queue<RaverHealth>> group in groups)
            {
                if (group.Value.Count == 0)
                {
                    continue;
                }

                float score = CrowdingPenalty(placedKeys, i, columns, group.Key)
                    - 0.3f * group.Value.Count / largest
                    + Random.Range(0f, 0.08f);
                if (score < bestScore)
                {
                    best = group.Key;
                    bestScore = score;
                }
            }

            placedKeys[i] = best;
            mixed[i] = groups[best].Dequeue();
        }

        return mixed;
    }

    // Quanto il modello key è già presente vicino al posto index: per ogni posto già occupato dallo
    // stesso modello entro due posti e mezzo, 1 / distanza². Le file sono sfalsate di mezzo posto,
    // come in SetStage.
    private static float CrowdingPenalty(int[] placedKeys, int index, int columns, int key)
    {
        Vector2 slot = SlotCoordinates(index, columns);
        float penalty = 0f;
        int firstToCheck = Mathf.Max(0, index - columns * 3);
        for (int j = firstToCheck; j < index; j++)
        {
            if (placedKeys[j] != key)
            {
                continue;
            }

            float sqrDistance = (SlotCoordinates(j, columns) - slot).sqrMagnitude;
            if (sqrDistance <= 2.5f * 2.5f)
            {
                penalty += 1f / sqrDistance;
            }
        }
        return penalty;
    }

    private static Vector2 SlotCoordinates(int index, int columns)
    {
        int row = index / columns;
        return new Vector2(index % columns + (row % 2) * 0.5f, row);
    }

    // Il modello si riconosce dall'asset della mesh, non dal nome: le mesh esportate da Blender di
    // FBX diversi possono chiamarsi tutte uguali, mentre i Raver dello stesso prefab condividono
    // proprio lo stesso asset.
    private static int ModelKey(RaverHealth raver)
    {
        SkinnedMeshRenderer skinned = raver.GetComponentInChildren<SkinnedMeshRenderer>();
        if (skinned != null && skinned.sharedMesh != null)
        {
            return skinned.sharedMesh.GetInstanceID();
        }

        MeshFilter filter = raver.GetComponentInChildren<MeshFilter>();
        return filter != null && filter.sharedMesh != null ? filter.sharedMesh.GetInstanceID() : raver.name.GetHashCode();
    }

    // Oltre al ballo, ognuno si sposta attorno al suo posto per conto suo: un percorso irregolare
    // (rumore di Perlin, non un'oscillazione), con un punto di partenza e una velocità sua. Niente
    // di sincronizzato, una folla vera.
    private void SwayCrowd()
    {
        float t = Time.unscaledTime;
        foreach ((Transform raver, Vector3 basePosition, float seed, float speed) in dancers)
        {
            if (raver == null)
            {
                continue;
            }

            float time = t * speed;
            float across = Mathf.PerlinNoise(seed, time) * 2f - 1f;
            float along = Mathf.PerlinNoise(time, seed + 37f) * 2f - 1f;
            raver.position = basePosition + (crowdSide * across + crowdFront * along) * driftAmplitude;
        }
    }

    // I Raver tornano Raver normali là dove sono, in folla: niente teletrasporto, alle postazioni
    // ci camminano da soli col gioco che gira. Il Rigidbody va allineato alla posizione data a mano
    // durante la pausa, altrimenti la fisica ripartirebbe da dov'erano prima del menu.
    private void ReleaseRavers()
    {
        dancers.Clear();
        foreach ((Transform raver, Vector3 position, Quaternion rotation) in stagedRavers)
        {
            if (raver == null)
            {
                continue;
            }

            raver.rotation = rotation;
            if (raver.TryGetComponent(out Rigidbody body))
            {
                body.position = raver.position;
                body.rotation = rotation;
            }
            raver.GetComponent<RaverAnimation>()?.SetMenuDance(null);
            // Alle postazioni ci corrono: passo accelerato finché non arrivano (RaversHome).
            if (raver.TryGetComponent(out RaverChase chase))
            {
                chase.MultiplyMoveSpeed(introRaverSpeedMultiplier);
                chase.GetComponent<RaverAnimation>()?.SetWalkSpeedMultiplier(introRaverSpeedMultiplier);
                hurriedRavers.Add(chase);
            }
            // Il giocoliere resta senza poi finché non è di nuovo al suo posto (RaversHome).
            if (raver.TryGetComponent(out RaverJuggler juggler) && juggler.IsJuggler)
            {
                hiddenJuggler = juggler;
            }
        }
        stagedRavers.Clear();
    }

    private void UnfocusCamera()
    {
        if (CameraFollow.Instance != null)
        {
            CameraFollow.Instance.Unfocus();
        }
    }

    void OnGUI()
    {
        if (inIntro)
        {
            return;
        }

        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            titleStyle.normal.textColor = Color.white;
            buttonStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
            hintStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            hintStyle.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
        }

        float h = Screen.height;
        titleStyle.fontSize = Mathf.RoundToInt(h * 0.058f);
        buttonStyle.fontSize = Mathf.RoundToInt(h * 0.036f);
        hintStyle.fontSize = Mathf.RoundToInt(h * 0.021f);

        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, h), backdrop);
        GUI.Label(new Rect(0f, h * 0.18f, Screen.width, h * 0.12f), "SCEGLI LA MODALITÀ", titleStyle);

        float width = Mathf.Min(Screen.width * 0.5f, h * 0.56f);
        float x = (Screen.width - width) * 0.5f;
        for (int i = 0; i < Labels.Length; i++)
        {
            float y = h * (0.38f + i * 0.17f);
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = i == selected ? new Color(1f, 0.3f, 0.8f) : Color.gray;
            string label = i == selected ? $"> {Labels[i]} <" : Labels[i];
            if (GUI.Button(new Rect(x, y, width, h * 0.08f), label, buttonStyle))
            {
                Choose(i);
            }
            GUI.backgroundColor = previous;
            GUI.Label(new Rect(0f, y + h * 0.085f, Screen.width, h * 0.045f), Hints[i], hintStyle);
        }
    }
}
