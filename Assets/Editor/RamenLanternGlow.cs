using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Le Point Light aggiunte da RamenLanternLights non si vedono in gioco: l'URP Asset del
// progetto ha "Additional Lights" in modalità Per Vertex, e le due lanterne ("Lanterna_Dx"/
// "Lanterna_Sx" sotto il modello "ramen") sono superfici chiuse, quindi una luce puntiforme al
// loro interno non illumina in modo percepibile la carta stessa (le normali della mesh
// guardano verso l'esterno). La soluzione robusta è l'emissione: rendiamo il materiale
// "Lanterna_Rossa" (incorporato nell'FBX ramen.fbx) auto-illuminante, così la lanterna appare
// accesa indipendentemente dalle luci di scena. Passa dall'editor perché il materiale è un
// sub-asset dell'FBX: va caricato e modificato tramite AssetDatabase, non a mano nello YAML.
// Fatto il fix questo file si può cancellare.
public static class RamenLanternGlow
{
    private const string MaterialName = "Lanterna_Rossa";
    private static readonly Color EmissionColor = new Color(1f, 0.15f, 0.06f) * 1.5f;

    [MenuItem("Tools/Make Ramen Lanterns Glow")]
    private static void MakeGlow()
    {
        Material[] materials = FindLanternMaterials();
        if (materials.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "Make Ramen Lanterns Glow",
                $"Nessun materiale \"{MaterialName}\" trovato sui renderer sotto \"Lanterna_Dx\"/\"Lanterna_Sx\" nella scena aperta. Apri GameplayV2 e riprova.",
                "OK");
            return;
        }

        foreach (Material mat in materials)
        {
            Undo.RecordObject(mat, "Make Ramen Lanterns Glow");
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", EmissionColor);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        AssetDatabase.SaveAssets();

        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (scene.isLoaded)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }
        }

        Debug.Log($"[RamenLanternGlow] Emissione rossa attivata su {materials.Length} materiale/i \"{MaterialName}\".");
    }

    private static Material[] FindLanternMaterials()
    {
        var lanternNodeNames = new[] { "Lanterna_Dx", "Lanterna_Sx" };
        var results = new HashSet<Material>();

        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded)
            {
                continue;
            }

            IEnumerable<Transform> lanterns = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .Where(t => lanternNodeNames.Contains(t.name));

            foreach (Transform lantern in lanterns)
            {
                foreach (Renderer renderer in lantern.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (Material mat in renderer.sharedMaterials)
                    {
                        if (mat != null && mat.name.StartsWith(MaterialName))
                        {
                            results.Add(mat);
                        }
                    }
                }
            }
        }

        return results.ToArray();
    }
}
