using System.Collections.Generic;
using UnityEngine;

// Lo Sbirro preso dall'Hoffman (EnemyHealth.HoffmanExplode) non scoppia subito: per Duration
// secondi si gonfia come un palloncino, arrotondandosi fino a diventare quasi una sfera,
// tremolando sempre più forte e virando ai colori dell'arcobaleno, e solo alla fine esplode in
// bolle psichedeliche (PsychedelicExplosion) e sparisce.
//
// È già morto da subito: AI, attacchi e collider vengono spenti appena comincia, così non
// cammina, non colpisce, non blocca nessuno e non si fa colpire di nuovo (EnemyHealth ignora
// ogni altra morte mentre si gonfia, vedi IsSwelling).
//
// La forma a palloncino: ogni mesh del modello viene copiata così com'è in quel momento (le
// skinnate con BakeMesh, nella posa dell'animazione) in un oggetto a parte, con i vertici in
// coordinate di mondo, e l'originale si nasconde. Poi, fotogramma per fotogramma, ogni vertice
// scivola verso il punto della sfera nella sua direzione dal centro del corpo. Le mesh non
// skinnate e non leggibili (Read/Write spento, in build non si possono leggere) restano come
// sono e si gonfiano solo col colore.
public class HoffmanSwell : MonoBehaviour
{
    private const float Duration = 0.75f;
    private const float Roundness = 0.9f;      // quanto diventa sfera alla fine (1 = sfera perfetta)
    private const float BalloonRadius = 0.85f; // raggio della sfera, in mezze altezze del corpo
    private const float HueSpeed = 1.2f;       // giri dell'arcobaleno al secondo
    private const float MaxTint = 0.6f;        // quanto vira al colore, alla fine

    private class Balloon
    {
        public Mesh Mesh;
        public Vector3[] Start;     // vertici in mondo, com'erano
        public Vector3[] Direction; // dal centro del corpo verso il vertice
        public Vector3[] StartNormals;
        public Vector3[] Vertices;  // buffer riusato a ogni fotogramma
        public Vector3[] Normals;
        public GameObject Holder;
    }

    private readonly List<Balloon> balloons = new List<Balloon>();
    private Vector3 center;
    private float radius;
    private float elapsed;
    private float hueOffset;
    private System.Action onPop;

    private readonly List<(Renderer renderer, int index, string property, Color original)> tints =
        new List<(Renderer, int, string, Color)>();
    private MaterialPropertyBlock block;

    public static void Begin(GameObject enemy, System.Action onPop)
    {
        HoffmanSwell swell = enemy.AddComponent<HoffmanSwell>();
        swell.onPop = onPop;
    }

    // In Awake (che AddComponent esegue subito) e non in Start: lo Sbirro si ferma all'istante,
    // senza un ultimo fotogramma di AI.
    void Awake()
    {
        hueOffset = Random.value;
        Freeze();
        List<Renderer> renderers = BodyRenderers();
        MeasureBody(renderers);
        BuildBalloons(renderers);
        CollectTints(renderers);
    }

    void OnDestroy()
    {
        foreach (Balloon balloon in balloons)
        {
            Destroy(balloon.Holder);
            Destroy(balloon.Mesh);
        }
    }

    // Spegne tutto quello che lo fa agire o essere toccato. Resta acceso solo EnemyHealth (che
    // chiama onPop) e questo componente.
    private void Freeze()
    {
        foreach (MonoBehaviour behaviour in GetComponentsInChildren<MonoBehaviour>())
        {
            if (behaviour != this && !(behaviour is EnemyHealth))
            {
                behaviour.enabled = false;
            }
        }

        foreach (Collider collider in GetComponentsInChildren<Collider>())
        {
            collider.enabled = false;
        }

        if (TryGetComponent(out Rigidbody body))
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }

        // L'animazione si ferma lì: in trip resta imbambolato mentre si gonfia.
        foreach (Animator animator in GetComponentsInChildren<Animator>())
        {
            animator.speed = 0f;
        }
    }

    private List<Renderer> BodyRenderers()
    {
        List<Renderer> renderers = new List<Renderer>();
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
        {
            if ((renderer is MeshRenderer || renderer is SkinnedMeshRenderer) && renderer.enabled)
            {
                renderers.Add(renderer);
            }
        }
        return renderers;
    }

    private void MeasureBody(List<Renderer> renderers)
    {
        Bounds bounds = new Bounds(transform.position + Vector3.up, Vector3.one * 2f);
        for (int i = 0; i < renderers.Count; i++)
        {
            if (i == 0)
            {
                bounds = renderers[i].bounds;
            }
            else
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
        }

        center = bounds.center;
        radius = Mathf.Max(0.3f, bounds.extents.y * BalloonRadius);
    }

    private void BuildBalloons(List<Renderer> renderers)
    {
        foreach (Renderer renderer in renderers)
        {
            Mesh mesh;
            Matrix4x4 toWorld;
            if (renderer is SkinnedMeshRenderer skinned)
            {
                if (skinned.sharedMesh == null)
                {
                    continue;
                }
                mesh = new Mesh();
                skinned.BakeMesh(mesh, true);
                toWorld = BakedMatrix(skinned, mesh);
            }
            else
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable)
                {
                    continue;
                }
                mesh = Instantiate(filter.sharedMesh);
                toWorld = renderer.transform.localToWorldMatrix;
            }

            Balloon balloon = new Balloon { Mesh = mesh };
            balloon.Start = mesh.vertices;
            balloon.StartNormals = mesh.normals;
            if (balloon.StartNormals.Length != balloon.Start.Length)
            {
                balloon.StartNormals = new Vector3[balloon.Start.Length];
            }
            balloon.Direction = new Vector3[balloon.Start.Length];
            for (int i = 0; i < balloon.Start.Length; i++)
            {
                balloon.Start[i] = toWorld.MultiplyPoint3x4(balloon.Start[i]);
                balloon.StartNormals[i] = toWorld.MultiplyVector(balloon.StartNormals[i]).normalized;
                Vector3 fromCenter = balloon.Start[i] - center;
                balloon.Direction[i] = fromCenter.sqrMagnitude > 0.000001f ? fromCenter.normalized : Vector3.up;
            }
            balloon.Vertices = (Vector3[])balloon.Start.Clone();
            balloon.Normals = (Vector3[])balloon.StartNormals.Clone();
            mesh.SetVertices(balloon.Vertices);
            mesh.SetNormals(balloon.Normals);
            mesh.bounds = new Bounds(center, Vector3.one * (radius * 4f + 4f));

            // Fuori dalla gerarchia dello Sbirro: i vertici sono già in mondo, serve un oggetto
            // senza spostamenti né scala.
            balloon.Holder = new GameObject("Palloncino " + renderer.name);
            balloon.Holder.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer copy = balloon.Holder.AddComponent<MeshRenderer>();
            copy.sharedMaterials = renderer.sharedMaterials;
            copy.shadowCastingMode = renderer.shadowCastingMode;
            renderer.enabled = false;

            balloons.Add(balloon);
        }
    }

    // Dove sta la copia cotta di una mesh skinnata: se BakeMesh l'ha già scalata basta posizione
    // e rotazione del Renderer, altrimenti serve anche la sua scala. Come in PlayerKhole, si
    // sceglie confrontando la copia con l'ingombro vero del modello.
    private static Matrix4x4 BakedMatrix(SkinnedMeshRenderer skinned, Mesh baked)
    {
        Transform t = skinned.transform;
        Matrix4x4 unscaled = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
        float real = skinned.bounds.size.magnitude;
        float asIs = WorldSize(unscaled, baked.bounds);
        float scaled = WorldSize(t.localToWorldMatrix, baked.bounds);
        bool needsScale = real > 0.0001f
            && Mathf.Abs(Mathf.Log(Mathf.Max(scaled, 0.0001f) / real)) < Mathf.Abs(Mathf.Log(Mathf.Max(asIs, 0.0001f) / real));
        return needsScale ? t.localToWorldMatrix : unscaled;
    }

    private static float WorldSize(Matrix4x4 matrix, Bounds local)
    {
        return (matrix.MultiplyPoint3x4(local.max) - matrix.MultiplyPoint3x4(local.min)).magnitude;
    }

    // I colori di partenza dei materiali, per virarli verso l'arcobaleno con un
    // MaterialPropertyBlock (senza creare copie dei materiali). Sui palloncini se ci sono, sui
    // Renderer originali rimasti visibili altrimenti.
    private void CollectTints(List<Renderer> originals)
    {
        block = new MaterialPropertyBlock();
        List<Renderer> renderers = new List<Renderer>();
        foreach (Balloon balloon in balloons)
        {
            renderers.Add(balloon.Holder.GetComponent<Renderer>());
        }
        foreach (Renderer renderer in originals)
        {
            if (renderer.enabled)
            {
                renderers.Add(renderer);
            }
        }

        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null)
                {
                    continue;
                }

                string property = material.HasProperty("_BaseColor") ? "_BaseColor"
                    : material.HasProperty("_Color") ? "_Color" : null;
                if (property != null)
                {
                    tints.Add((renderer, i, property, material.GetColor(property)));
                }
            }
        }
    }

    void Update()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / Duration);

        // Si arrotonda in fretta e poi tiene, mentre il palloncino continua a tendersi: un
        // tremolio che accelera e si allarga verso lo scoppio.
        float roundness = Roundness * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t * 1.4f));
        float wobble = Mathf.Sin(elapsed * Mathf.Lerp(14f, 45f, t)) * 0.06f * t;
        float sphere = radius * (0.85f + 0.2f * t + wobble);
        foreach (Balloon balloon in balloons)
        {
            for (int i = 0; i < balloon.Start.Length; i++)
            {
                Vector3 onSphere = center + balloon.Direction[i] * sphere;
                balloon.Vertices[i] = Vector3.LerpUnclamped(balloon.Start[i], onSphere, roundness);
                balloon.Normals[i] = Vector3.Lerp(balloon.StartNormals[i], balloon.Direction[i], roundness);
            }
            balloon.Mesh.SetVertices(balloon.Vertices);
            balloon.Mesh.SetNormals(balloon.Normals);
        }

        Color hue = Color.HSVToRGB(Mathf.Repeat(hueOffset + elapsed * HueSpeed, 1f), 0.8f, 1f);
        foreach ((Renderer renderer, int index, string property, Color original) in tints)
        {
            if (renderer == null)
            {
                continue;
            }

            renderer.GetPropertyBlock(block, index);
            Color color = Color.Lerp(original, hue, MaxTint * t);
            color.a = original.a;
            block.SetColor(property, color);
            renderer.SetPropertyBlock(block, index);
        }

        if (t >= 1f)
        {
            enabled = false;
            onPop?.Invoke();
        }
    }
}
