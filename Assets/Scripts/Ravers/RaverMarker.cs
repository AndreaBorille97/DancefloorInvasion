using UnityEngine;

// Cerchio verde trasparente sulla sabbia sotto i piedi dei Raver: lo stesso cerchio degli Sbirro
// (SbirroMarker, stesso sprite e stessa misura), ma tenue, per riconoscere i nostri nella ressa
// senza dare nell'occhio. Lo aggiunge da solo RaverHealth.Start, niente da collegare.
public class RaverMarker : MonoBehaviour
{
    private static readonly Color RingColor = new Color(0.45f, 0.9f, 0.55f, 0.14f);

    // Sopra il piano del pavimento oltre questa quota il Raver sta su un rialzo (palco, casse):
    // il cerchio va sotto i suoi piedi e non sepolto sul piano della sabbia.
    private const float RaisedThreshold = 0.3f;

    private Transform ring;
    private Renderer body; // la mesh del corpo: il cerchio le sta sotto, non sotto il pivot
    private float feetOffset; // dal pivot al fondo del modello, preso una volta

    void Start()
    {
        body = FindBody();
        Bounds bounds = body != null ? body.bounds : new Bounds(transform.position, Vector3.one);
        feetOffset = bounds.min.y - transform.position.y;
        ring = SbirroMarker.CreateRing("RaverRing", transform, SbirroMarker.RingRadius(bounds), RingColor).transform;
        PlaceRing();
    }

    // Rifatto ogni fotogramma: il Raver si muove, va a terra, viene spinto.
    void LateUpdate()
    {
        if (ring != null)
        {
            PlaceRing();
        }
    }

    // In pianta al centro del corpo: in alcuni modelli dei Raver la mesh non sta sopra il pivot
    // della radice, e il cerchio messo sul pivot finiva di lato. L'ingombro di una
    // SkinnedMeshRenderer segue l'osso radice, quindi resta sotto il corpo anche mentre si anima.
    private void PlaceRing()
    {
        Vector3 center = body != null ? body.bounds.center : transform.position;
        float feetY = transform.position.y + feetOffset;
        float groundY = feetY;
        if (FloorBounds.Instance != null)
        {
            float floorY = FloorBounds.Instance.Bounds.max.y;
            groundY = feetY > floorY + RaisedThreshold ? feetY : floorY;
        }
        SbirroMarker.PlaceRingAt(ring, center, groundY);
    }

    // La mesh più grande del Raver: è il corpo, non un oggetto che ha in mano (clave, poi...).
    private Renderer FindBody()
    {
        Renderer best = null;
        float bestSize = -1f;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer))
            {
                continue;
            }

            float size = r.bounds.size.sqrMagnitude;
            // Una SkinnedMeshRenderer vince sempre su una mesh fissa: il corpo è quella animata.
            if (r is SkinnedMeshRenderer)
            {
                size += 1e6f;
            }
            if (size > bestSize)
            {
                best = r;
                bestSize = size;
            }
        }
        return best;
    }
}
