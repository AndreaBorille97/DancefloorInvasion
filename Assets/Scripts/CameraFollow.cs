using UnityEngine;

// Da mettere sulla Main Camera (NON come figlia del Player, ma come oggetto
// separato in scena). Segue solo la posizione del target, mai la sua
// rotazione: la camera mantiene l'orientamento impostato in editor.
public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target; // il player da seguire

    [Header("Smoothing")]
    [SerializeField] private float smoothTime = 0.1f; // tempo (in secondi) impiegato circa per raggiungere il target: più basso = più rapido

    private Vector3 offset; // distanza/angolo camera-target, ricavati dalla posizione impostata in editor
    private Vector3 velocity; // stato interno di SmoothDamp (velocità corrente), non va toccato a mano

    void Start()
    {
        if (target == null)
        {
            return;
        }

        // L'offset non è un valore fisso da inserire in Inspector: viene calcolato
        // dalla posizione in cui la camera è stata piazzata in editor rispetto al
        // target, così a runtime la visuale corrisponde davvero a dove è stata messa.
        offset = transform.position - target.position;
    }

    void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        // LateUpdate gira dopo il movimento del player (Update/FixedUpdate),
        // così la camera insegue una posizione già aggiornata e non "trema".
        Vector3 desiredPosition = target.position + offset;
        // SmoothDamp è basato sulla velocità (non su una frazione fissa della distanza
        // residua come Lerp), quindi è molto più stabile frame per frame e non "batte"
        // contro l'interpolazione del Rigidbody del player.
        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, smoothTime);

        // La rotazione non viene mai toccata: resta quella impostata manualmente
        // sulla camera (es. vista dall'alto), indipendente da come ruota il Player.
    }
}
