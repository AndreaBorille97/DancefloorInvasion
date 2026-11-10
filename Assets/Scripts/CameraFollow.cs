using UnityEngine;

// Da mettere sulla Main Camera (NON come figlia del Player, ma come oggetto
// separato in scena). Segue solo la posizione del target, mai la sua
// rotazione: la camera mantiene l'orientamento impostato in editor.
public class CameraFollow : MonoBehaviour
{
    // Per DefeatSequence, che porta la camera sull'obiettivo distrutto fuori schermo.
    public static CameraFollow Instance { get; private set; }

    [Header("Target")]
    [SerializeField] private Transform target; // il player da seguire

    [Header("Smoothing")]
    [SerializeField] private float smoothTime = 0.1f; // tempo (in secondi) impiegato circa per raggiungere il target: più basso = più rapido
    [Tooltip("Come smoothTime, ma per il viaggio verso la Console o il SoundSystem distrutti fuori schermo (Focus): più lento, per far vedere dove si va.")]
    [SerializeField] private float focusSmoothTime = 0.45f;

    private Vector3 offset; // distanza/angolo camera-target, ricavati dalla posizione impostata in editor
    private Vector3 velocity; // stato interno di SmoothDamp (velocità corrente), non va toccato a mano
    private Vector3 followPosition; // posizione che insegue il target, senza tremolio (vedi CameraShake)
    private float targetHeight; // altezza del target, a cui viene portato il punto di Focus
    private Vector3? focusPosition; // se c'è, la camera va qui e non segue più il target

    // Arrivata (quasi) sul punto di Focus.
    public bool IsAtFocus => focusPosition.HasValue && (followPosition - focusPosition.Value).sqrMagnitude < 0.04f;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    void Start()
    {
        followPosition = transform.position;

        if (target == null)
        {
            return;
        }

        // L'offset non è un valore fisso da inserire in Inspector: viene calcolato
        // dalla posizione in cui la camera è stata piazzata in editor rispetto al
        // target, così a runtime la visuale corrisponde davvero a dove è stata messa.
        offset = transform.position - target.position;
        targetHeight = target.position.y;
    }

    // Smette di seguire il target e inquadra point come inquadrerebbe il Player lì sopra. Serve
    // alla sconfitta e al menu della modalità (DifficultyMenu, che poi chiama Unfocus).
    // distanceScale allontana (sopra 1) la camera lungo la stessa direzione di sempre.
    public void Focus(Vector3 point, float distanceScale = 1f)
    {
        point.y = targetHeight;
        focusPosition = point + offset * distanceScale;
    }

    // Torna a seguire il target.
    public void Unfocus()
    {
        focusPosition = null;
    }

    void LateUpdate()
    {
        if (focusPosition.HasValue)
        {
            // Tempo non scalato: il viaggio non si ferma se il gioco va in pausa a metà.
            followPosition = Vector3.SmoothDamp(followPosition, focusPosition.Value, ref velocity, focusSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            transform.position = followPosition + CameraShake.Tick(transform, Time.unscaledDeltaTime);
            return;
        }

        // Player morto (distrutto): la camera resta dov'è, ma il tremolio dell'esplosione
        // (PlayerHealth.Die) va applicato lo stesso.
        if (target == null)
        {
            transform.position = followPosition + CameraShake.Tick(transform, Time.unscaledDeltaTime);
            return;
        }

        // LateUpdate gira dopo il movimento del player (Update/FixedUpdate),
        // così la camera insegue una posizione già aggiornata e non "trema".
        Vector3 desiredPosition = target.position + offset;
        // SmoothDamp è basato sulla velocità (non su una frazione fissa della distanza
        // residua come Lerp), quindi è molto più stabile frame per frame e non "batte"
        // contro l'interpolazione del Rigidbody del player.
        // Lo SmoothDamp lavora su followPosition e non su transform.position: il tremolio dello
        // schermo (CameraShake) si somma solo alla fine, altrimenti verrebbe inseguito e smorzato.
        followPosition = Vector3.SmoothDamp(followPosition, desiredPosition, ref velocity, smoothTime);
        transform.position = followPosition + CameraShake.Tick(transform, Time.unscaledDeltaTime);

        // La rotazione non viene mai toccata: resta quella impostata manualmente
        // sulla camera (es. vista dall'alto), indipendente da come ruota il Player.
    }
}
