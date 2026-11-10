using System.Collections.Generic;
using UnityEngine;

// Patina oro metallizzata sopra il Player mentre corre più veloce (anfetamina, vedi
// PlayerMovement.ApplySpeedBoost, che la accende e la spegne).
//
// Non tocca i materiali del modello: HitFlashFeedback li scambia per il lampo dei colpi e
// PlayerBerserk ci mette sopra il property block del LED, e i tre si pesterebbero i piedi. Qui
// invece ogni mesh del corpo ha un guscio: una seconda copia della stessa mesh (per le skinnate
// con le stesse ossa, quindi segue ogni posa) disegnata sopra con lo shader
// DancefloorInvasion/MetallicPatina, trasparente. Il material sta in Resources/MetallicPatina
// perché lo shader finisca anche nelle build.
public class MetallicPatina : MonoBehaviour
{
    // Segna i gusci, perché chi copia il corpo del Player (la scia del khole) li salti.
    public class Shell : MonoBehaviour { }

    private const string MaterialPath = "MetallicPatina"; // Assets/Resources/MetallicPatina.mat
    private const float FadeTime = 0.25f;
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");

    private readonly List<GameObject> shells = new List<GameObject>();
    private Material material; // copia: la dissolvenza non deve toccare l'asset
    private float fullAlpha;
    private float shown;       // 0 = spenta, 1 = piena
    private bool on;

    public static void Set(GameObject character, bool on)
    {
        MetallicPatina patina = character.GetComponent<MetallicPatina>();
        if (patina == null)
        {
            if (!on)
            {
                return;
            }
            patina = character.AddComponent<MetallicPatina>();
        }
        patina.on = on;
        if (on)
        {
            patina.Build();
        }
    }

    private void Build()
    {
        if (material != null)
        {
            return;
        }

        Material asset = Resources.Load<Material>(MaterialPath);
        if (asset == null)
        {
            Debug.LogWarning($"[MetallicPatina] Materiale Resources/{MaterialPath} non trovato: niente patina.");
            enabled = false;
            return;
        }
        material = new Material(asset);
        fullAlpha = material.GetFloat(AlphaId);
        material.SetFloat(AlphaId, 0f);

        foreach (Renderer source in GetComponentsInChildren<Renderer>())
        {
            // Solo il corpo visibile: niente capsula spenta, particelle, onde o altri gusci.
            if (!source.enabled || source.GetComponent<Shell>() != null || source.GetComponentInParent<SoundWaveProjectile>() != null)
            {
                continue;
            }
            if (source is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            {
                shells.Add(CreateSkinnedShell(skinned));
            }
            else if (source is MeshRenderer && source.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
            {
                shells.Add(CreateMeshShell(source, filter.sharedMesh));
            }
        }

        // Una riga sola, alla prima accensione: se la patina non si vede, dice se i gusci ci sono e su cosa.
        var names = new List<string>();
        foreach (GameObject shell in shells)
        {
            names.Add(shell.transform.parent.name);
        }
        Debug.Log($"[MetallicPatina] {shells.Count} gusci su: {string.Join(", ", names)}", this);
    }

    private GameObject CreateSkinnedShell(SkinnedMeshRenderer source)
    {
        GameObject shell = NewShell(source.transform);
        SkinnedMeshRenderer copy = shell.AddComponent<SkinnedMeshRenderer>();
        copy.sharedMesh = source.sharedMesh;
        copy.bones = source.bones;
        copy.rootBone = source.rootBone;
        copy.localBounds = source.localBounds;
        copy.updateWhenOffscreen = source.updateWhenOffscreen;
        copy.sharedMaterials = Fill(source.sharedMesh.subMeshCount);
        Quiet(copy);
        return shell;
    }

    private GameObject CreateMeshShell(Renderer source, Mesh mesh)
    {
        GameObject shell = NewShell(source.transform);
        shell.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer copy = shell.AddComponent<MeshRenderer>();
        copy.sharedMaterials = Fill(mesh.subMeshCount);
        Quiet(copy);
        return shell;
    }

    private static GameObject NewShell(Transform parent)
    {
        GameObject shell = new GameObject("PatinaShell");
        shell.layer = parent.gameObject.layer;
        shell.transform.SetParent(parent, false);
        shell.AddComponent<Shell>();
        return shell;
    }

    private Material[] Fill(int count)
    {
        Material[] materials = new Material[Mathf.Max(1, count)];
        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = material;
        }
        return materials;
    }

    private static void Quiet(Renderer renderer)
    {
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    void Update()
    {
        if (material == null)
        {
            return;
        }

        shown = Mathf.MoveTowards(shown, on ? 1f : 0f, Time.deltaTime / FadeTime);
        material.SetFloat(AlphaId, fullAlpha * shown);

        bool visible = shown > 0f;
        foreach (GameObject shell in shells)
        {
            if (shell != null && shell.activeSelf != visible)
            {
                shell.SetActive(visible);
            }
        }
    }
}
