using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Appoggia i piedi del modello dello Sbirro sulla sabbia, allo stesso modo per tutti i tipi.
// Prima ognuno stava a un'altezza sua: lo Sbirro1 viene appoggiato al pavimento quando nasce
// (EnemyChase.snapToFloorOnSpawn) e gli altri no, e ogni prefab ha il modello figlio a una quota
// diversa rispetto alla radice; col cerchio nero a terra (SbirroMarker) si vedeva benissimo che
// il corazzato aveva i piedi lontani dal pavimento.
//
// Si sposta solo il modello (il figlio con l'Animator), non lo Sbirro: capsula, movimento e
// colpi restano come sono. La pianta dei piedi si misura sul primo Sbirro di ogni tipo: per
// MeasureDuration secondi, mentre cammina, il punto più basso della mesh animata (bounds
// calcolati sulla mesh deformata, vedi updateWhenOffscreen), rispetto alla radice. La misura
// resta per il tipo, quindi i successivi si allineano subito, appena nati.
//
// Lo aggiunge da solo EnemyHealth.Start.
public class SbirroGroundAlign : MonoBehaviour
{
    private const float MeasureDuration = 0.8f;

    // Per tipo (nome del prefab): quota della pianta dei piedi rispetto alla radice, in metri.
    private static readonly Dictionary<string, float> soleOffsets = new Dictionary<string, float>();

    private Transform model;
    private string kind;
    private SkinnedMeshRenderer[] skinned;
    private bool[] skinnedUpdateWhenOffscreen;
    private Renderer[] renderers;
    private float measureEnd;
    private float lowestSole = float.MaxValue;
    private bool measuring;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetState()
    {
        soleOffsets.Clear();
    }

    void Start()
    {
        Animator animator = GetComponentInChildren<Animator>();
        model = animator != null ? animator.transform : null;
        if (model == null || model == transform || FloorBounds.Instance == null)
        {
            // Senza un modello figlio da spostare (o senza pavimento di riferimento) non si
            // tocca niente: spostare la radice muoverebbe anche la capsula.
            enabled = false;
            return;
        }

        kind = name.Replace("(Clone)", "").Trim();
        if (soleOffsets.TryGetValue(kind, out float soleOffset))
        {
            Align(soleOffset);
            enabled = false;
            return;
        }

        // Prima misura per questo tipo: bounds calcolati sulla mesh deformata, così seguono
        // davvero i piedi nella camminata invece di essere quelli fissi del modello a riposo.
        renderers = model.GetComponentsInChildren<Renderer>()
            .Where(r => r is SkinnedMeshRenderer || r is MeshRenderer)
            .ToArray();
        skinned = model.GetComponentsInChildren<SkinnedMeshRenderer>();
        skinnedUpdateWhenOffscreen = new bool[skinned.Length];
        for (int i = 0; i < skinned.Length; i++)
        {
            skinnedUpdateWhenOffscreen[i] = skinned[i].updateWhenOffscreen;
            skinned[i].updateWhenOffscreen = true;
        }

        measureEnd = Time.time + MeasureDuration;
        measuring = true;
    }

    // In LateUpdate: la posa di questo fotogramma è già stata applicata dall'Animator.
    void LateUpdate()
    {
        if (!measuring)
        {
            return;
        }

        foreach (Renderer r in renderers)
        {
            if (r != null && r.enabled)
            {
                lowestSole = Mathf.Min(lowestSole, r.bounds.min.y);
            }
        }

        if (Time.time < measureEnd)
        {
            return;
        }

        measuring = false;
        for (int i = 0; i < skinned.Length; i++)
        {
            if (skinned[i] != null)
            {
                skinned[i].updateWhenOffscreen = skinnedUpdateWhenOffscreen[i];
            }
        }

        if (lowestSole < float.MaxValue)
        {
            float soleOffset = lowestSole - transform.position.y;
            soleOffsets[kind] = soleOffset;
            Align(soleOffset);
        }
        enabled = false;
    }

    // Sposta il modello in su o in giù quanto basta perché la pianta dei piedi stia sul pavimento.
    private void Align(float soleOffset)
    {
        float floorY = FloorBounds.Instance.Bounds.max.y;
        float delta = floorY - (transform.position.y + soleOffset);
        model.position += Vector3.up * delta;
    }
}
