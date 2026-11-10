using UnityEngine;

// Feedback di colpo n.1: il nemico colpito diventa per un attimo rosso. Lo stesso lampo lo fa
// il Player quando subisce un colpo (PlayerHealth.TakeHit chiama Flash).
// Si iscrive da solo a HitEvents. Per toglierlo cancella questo file (e la chiamata in PlayerHealth).
//
// Scambia per pochi centesimi di secondo i materiali dei renderer con uno piatto e luminoso,
// poi rimette gli originali: niente istanze di materiali (vedi memoria sul .material dei
// renderer) e funziona qualunque shader abbia il nemico.
public static class HitFlashFeedback
{
    private const float Duration = 0.12f;
    // Valori sopra 1: con HDR + bloom il nemico si accende davvero; senza, satura a un rosso pieno.
    private static readonly Color FlashColor = new Color(1.5f, 0.12f, 0.08f, 1f);

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
        Flash(enemy);
    }

    // Usato anche da PlayerHealth: il Player colpito fa lo stesso lampo dei nemici.
    public static void Flash(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        if (flashMaterial == null)
        {
            flashMaterial = new Material(Shader.Find("Sprites/Default")) { color = FlashColor };
        }

        Runner runner = target.GetComponent<Runner>();
        if (runner == null)
        {
            runner = target.AddComponent<Runner>();
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
