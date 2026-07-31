using UnityEngine;

// Da mettere sul prefab della sfera lanciata da EnemyRangedAttack (fumogeno), con un
// Collider impostato come Trigger (Is Trigger = true), stesso schema di CamperProjectile.
// A differenza di CamperProjectile (che vola dritto lungo una direzione), qui il punto
// di arrivo è fisso: Init() lo cattura al momento del lancio, poi la sfera lo raggiunge
// con una traiettoria ad arco (nessuna fisica, solo interpolazione), quindi è schivabile
// spostandosi ma non deviabile dopo il lancio. Colpisce Player e Raver infliggendo un
// colpo (IEnemyAttackTarget.TakeHit(), come un attacco corpo a corpo di Sbirro), ma non fa
// nulla a Console/DJ o SoundSystem (vedi EnemyRangedAttack: non li punta mai come bersaglio,
// e qui li ignora comunque anche se li tocca per puro caso lungo la traiettoria). A contatto
// con un bersaglio valido, o comunque a fine corsa se non colpisce nessuno, scoppia
// lasciando a terra una nuvola di fumo (vedi SbirroCloud).
public class ParabolicProjectile : MonoBehaviour
{
    [Header("Volo")]
    [Tooltip("Velocità orizzontale: usata per calcolare quanto dura il volo in base alla distanza dal bersaglio.")]
    [SerializeField] private float speed = 14f;
    [SerializeField] private float arcHeight = 3f; // altezza raggiunta a metà tragitto
    [SerializeField] private float lifespan = 6f; // autodistruzione di sicurezza se non atterra mai (es. bersaglio sparito)

    [Header("Nuvola a terra")]
    [Tooltip("Prefab della nuvoletta lasciata quando la sfera finisce la sua corsa, per colpo diretto o atterraggio a vuoto (vedi SbirroCloud).")]
    [SerializeField] private GameObject cloudPrefab;

    private Vector3 startPosition;
    private Vector3 targetPosition;
    private float duration;
    private float elapsed;
    private bool initialized;

    public void Init(Vector3 destination)
    {
        startPosition = transform.position;
        targetPosition = destination;

        Vector3 flatDelta = destination - startPosition;
        flatDelta.y = 0f;
        duration = Mathf.Max(flatDelta.magnitude / speed, 0.01f);

        initialized = true;
        Destroy(gameObject, lifespan);
    }

    void Update()
    {
        if (!initialized)
        {
            return; // nessun Init() ricevuto (errore di setup): resta fermo finché non scade lifespan
        }

        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);

        Vector3 flatPosition = Vector3.Lerp(startPosition, targetPosition, t);
        float arc = 4f * arcHeight * t * (1f - t); // parabola: 0 in t=0 e t=1, arcHeight in t=0.5
        transform.position = flatPosition + Vector3.up * arc;

        if (t >= 1f)
        {
            SpawnCloud();
            Destroy(gameObject);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        // Console/DJ e SoundSystem vanno ignorati anche se il proiettile li tocca per
        // caso lungo la traiettoria: il fumogeno non deve mai far danno a loro (vedi
        // EnemyRangedAttack, che comunque non li punta mai come bersaglio).
        if (other.GetComponent<DJConsoleHealth>() != null || other.GetComponent<SoundSystem>() != null)
        {
            return;
        }

        IEnemyAttackTarget target = other.GetComponent<IEnemyAttackTarget>();
        if (target == null)
        {
            return;
        }

        target.TakeHit();
        SpawnCloud();
        Destroy(gameObject);
    }

    private void SpawnCloud()
    {
        if (cloudPrefab != null)
        {
            Instantiate(cloudPrefab, transform.position, Quaternion.identity);
        }
    }
}
