using UnityEngine;

// Feedback di colpo n.1: il nemico colpito diventa per un attimo rosso.
// Si iscrive da solo a HitEvents. Per toglierlo cancella questo file.
//
// Scambia per pochi centesimi di secondo i materiali dei renderer con uno piatto e luminoso,
// poi rimette gli originali: niente istanze di materiali (vedi memoria sul .material dei
// renderer) e funziona qualunque shader abbia il nemico.
public static class HitFlashFeedback
{
    private const float Duration = 0.07f;
    private static readonly Color FlashColor = new Color(0.75f, 0.2f, 0.18f, 1f);

    private static Material flashMaterial;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        HitEvents.EnemyHit -= OnEnemyHit;
        HitEvents.EnemyHit += OnEnemyHit;
        flashMaterial = null;
    }

    private static void OnEnemyHit(GameObject enemy, Vector3 origin, Vector3 direction)
    {
        if (enemy == null)
        {
            return;
        }

        if (flashMaterial == null)
        {
            flashMaterial = new Material(Shader.Find("Sprites/Default")) { color = FlashColor };
        }

        Runner runner = enemy.GetComponent<Runner>();
        if (runner == null)
        {
            runner = enemy.AddComponent<Runner>();
        }

        runner.Flash(flashMaterial, Duration);
    }

    private class Runner : MonoBehaviour
    {
        private Renderer[] renderers;
        private Material[][] originals;
        private float endTime;
        private bool flashing;

        public void Flash(Material material, float duration)
        {
            endTime = Time.time + duration;
            if (flashing)
            {
                return; // già acceso: basta allungare la durata
            }

            // Solo mesh: particelle e trail (es. fumo, occhi in volo) restano come sono.
            var found = new System.Collections.Generic.List<Renderer>();
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                if (r is MeshRenderer || r is SkinnedMeshRenderer)
                {
                    found.Add(r);
                }
            }

            renderers = found.ToArray();
            originals = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
            {
                originals[i] = renderers[i].sharedMaterials;
                var flat = new Material[originals[i].Length];
                for (int m = 0; m < flat.Length; m++)
                {
                    flat[m] = material;
                }

                renderers[i].sharedMaterials = flat;
            }

            flashing = true;
        }

        void Update()
        {
            if (flashing && Time.time >= endTime)
            {
                Restore();
            }
        }

        void OnDisable()
        {
            Restore();
        }

        private void Restore()
        {
            if (!flashing)
            {
                return;
            }

            flashing = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].sharedMaterials = originals[i];
                }
            }
        }
    }
}
