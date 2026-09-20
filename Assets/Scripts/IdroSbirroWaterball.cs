using UnityEngine;

// Creata a runtime da IdroSbirroHoseAttack.LaunchWaterball (nessun prefab da collegare a
// mano). Il volo ad arco e il danno finale sono guidati dal proprio Update(), non da una
// Coroutine sul componente che ha sparato il colpo: così continuano fino in fondo anche se
// l'IdroSbirro che l'ha lanciato muore/viene distrutto mentre è ancora in volo. Una Coroutine
// legata al MonoBehaviour del tiratore si fermerebbe invece a metà — Unity le interrompe
// quando il GameObject proprietario viene distrutto — lasciando il colpo bloccato a mezz'aria
// per sempre, senza mai infliggere il danno.
// Visivamente non è una bolla ma un fascio corto ("colpo di laser"): un LineRenderer che
// mostra la testa (posizione attuale) e una coda a distanza fissa nel tempo di volo
// (TrailFraction) indietro lungo lo stesso arco, non l'ultima posizione del frame precedente
// (che a framerate alti sarebbe troppo corta per essere visibile).
public class IdroSbirroWaterball : MonoBehaviour
{
    private const float TrailFraction = 0.22f;

    private Vector3 startPosition;
    private Vector3 destination;
    private float duration;
    private float elapsed;
    private float arcHeight;
    private float damage;
    private RaverHealth target;
    private LineRenderer lineRenderer;

    // Il bersaglio (destination) è catturato qui una volta sola, come la posizione del
    // target al momento del lancio: nessun homing, stesso schema di ParabolicProjectile. Il
    // danno a fine volo va a target (il RaverHealth catturato), non a chi si trova
    // fisicamente al punto di atterraggio.
    public void Init(Vector3 start, Vector3 destination, float speed, float arcHeight, float damage, RaverHealth target, Color color, float width)
    {
        startPosition = start;
        this.destination = destination;
        this.arcHeight = arcHeight;
        this.damage = damage;
        this.target = target;

        Vector3 flatDelta = destination - start;
        flatDelta.y = 0f;
        duration = Mathf.Max(flatDelta.magnitude / speed, 0.01f);

        transform.position = start;

        lineRenderer = gameObject.AddComponent<LineRenderer>();
        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = 2;
        lineRenderer.startWidth = width * 0.4f; // coda più sottile della testa
        lineRenderer.endWidth = width;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.startColor = new Color(color.r, color.g, color.b, color.a * 0.3f); // la coda svanisce
        lineRenderer.endColor = color;
        lineRenderer.SetPosition(0, start);
        lineRenderer.SetPosition(1, start);
    }

    void Update()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);

        Vector3 headPosition = GetPositionAt(t);
        Vector3 tailPosition = GetPositionAt(Mathf.Max(t - TrailFraction, 0f));

        transform.position = headPosition;
        lineRenderer.SetPosition(0, tailPosition);
        lineRenderer.SetPosition(1, headPosition);

        if (t >= 1f)
        {
            if (target != null && !target.IsDown)
            {
                target.TakeDamage(damage);
            }
            Destroy(gameObject);
        }
    }

    private Vector3 GetPositionAt(float t)
    {
        Vector3 flatPosition = Vector3.Lerp(startPosition, destination, t);
        float arc = 4f * arcHeight * t * (1f - t); // parabola: 0 in t=0 e t=1, arcHeight in t=0.5
        return flatPosition + Vector3.up * arc;
    }
}
