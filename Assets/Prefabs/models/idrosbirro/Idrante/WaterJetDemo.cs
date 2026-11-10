// Esempio: la camionetta spara al giocatore a intervalli. Danno solo mentre l'acqua colpisce.
using UnityEngine;

public class WaterJetDemo : MonoBehaviour
{
    public WaterJet jet;
    public Transform nozzle;
    public Transform player;
    public float interval = 3f;
    public float damagePerSecond = 10f;
    float _next;

    void Start()
    {
        jet.OnHitStart += () => Debug.Log("Getto: colpo a segno");
        jet.OnHitEnd += () => Debug.Log("Getto: fine colpo");
    }

    void Update()
    {
        if (jet.CurrentPhase == WaterJet.Phase.Idle && Time.time > _next)
        {
            jet.Fire(nozzle, player);
            _next = Time.time + interval;
        }
        if (jet.IsHitting)
        {
            // player.GetComponent<Health>().TakeDamage(damagePerSecond * Time.deltaTime);
        }
    }
}
