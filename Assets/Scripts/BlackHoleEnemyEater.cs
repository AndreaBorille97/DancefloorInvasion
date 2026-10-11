using UnityEngine;

// Aggiunto da PlayerBlackHoleThrow a ogni buco nero lanciato. Ogni Sbirro (chiunque abbia
// EnemyHealth) che entra entro killRadius dal buco nero viene spaghettificato e muore: lo stira e
// lo risucchia BlackHoleConsumable (quello del pacchetto del buco nero), che alla fine lo distrugge.
//
// Non si affida alla gravità del pacchetto: EnemyChase azzera la velocità degli Sbirro a ogni passo
// di fisica, quindi l'attrazione non li sposterebbe mai fino all'orizzonte. Qui basta entrare nel raggio.
// Il Robosbirro (boss) non ha EnemyHealth e resta immune, come per le onde sonore.
[RequireComponent(typeof(BlackHole))]
public class BlackHoleEnemyEater : MonoBehaviour
{
    [Tooltip("Distanza orizzontale dal centro del buco nero entro cui uno Sbirro viene risucchiato, in metri.")]
    public float killRadius = 3f;

    private BlackHole hole;
    private readonly Collider[] hits = new Collider[256];

    void Awake()
    {
        hole = GetComponent<BlackHole>();
    }

    void FixedUpdate()
    {
        // Solo mentre è vivo: durante l'animazione di morte non mangia più.
        if (hole.State != BlackHole.LifeState.Nascita && hole.State != BlackHole.LifeState.Attivo)
        {
            return;
        }

        Vector3 center = transform.position;
        // La sfera è più alta del raggio perché il buco nero sta sollevato da terra: il controllo vero
        // è sulla distanza orizzontale, sotto.
        int count = Physics.OverlapSphereNonAlloc(center, killRadius + 3f, hits, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            EnemyHealth enemy = hits[i].GetComponentInParent<EnemyHealth>();
            if (enemy == null)
            {
                continue;
            }

            Vector3 flat = enemy.transform.position - center;
            flat.y = 0f;
            if (flat.sqrMagnitude > killRadius * killRadius)
            {
                continue;
            }

            Rigidbody body = enemy.GetComponent<Rigidbody>();
            if (body == null)
            {
                continue;
            }

            if (enemy.TryGetComponent(out BlackHoleConsumable consumable) && consumable.IsBeingConsumed)
            {
                continue;
            }

            Swallow(enemy.gameObject, body);
        }
    }

    private void Swallow(GameObject enemy, Rigidbody body)
    {
        // Spegne tutto il comportamento dello Sbirro (movimento, attacchi, vita...): altrimenti
        // EnemyChase continuerebbe a spostarlo col Rigidbody e combatterebbe contro il risucchio.
        // Animator lasciato acceso: si dimena mentre viene stirato.
        foreach (Behaviour behaviour in enemy.GetComponentsInChildren<Behaviour>())
        {
            if (behaviour is Animator || behaviour is Animation || behaviour is BlackHoleConsumable)
            {
                continue;
            }

            behaviour.enabled = false;
        }

        hole.Consume(body);
    }
}
