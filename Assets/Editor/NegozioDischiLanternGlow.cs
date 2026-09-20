using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Sotto il modello "negozio_dischi" (Assets/Prefabs/models/negozio_dischi.fbx) c'è una lanterna
// da insegna fatta di più pezzi: LanternTop/Bar/Bar.001-3/Ring/Wire/Bot/Glass, con un materiale
// "Lantern" (probabilmente sul vetro). Come per le lanterne del ramen, una Point Light da sola
// non basta a farla vedere accesa in gioco (URP ha le Additional Light in modalità Per Vertex e
// la mesh è una superficie chiusa) — serve rendere il materiale emissivo. Qui:
//   1. rende emissivo (giallo caldo) il materiale "Lantern" sui renderer sotto i pezzi Lantern*
//   2. aggiunge una Point Light gialla debole dentro al vetro (LanternGlass, o il primo pezzo
//      Lantern* trovato se il vetro non c'è) per un bagliore d'ambiente sulle superfici vicine
//
// ATTENZIONE fileID/nomi: la ricerca è ristretta ai discendenti di un GameObject chiamato
// esattamente "negozio_dischi" e ai nomi esatti dei pezzi Lantern*, non a un semplice
// StartsWith("Lantern") su tutta la scena — la prima versione di questo file lo faceva e
// beccava per errore anche "Lanterna_Dx"/"Lanterna_Sx" (le lanterne rosse del ramen, il cui
// nome inizia anch'esso per "Lantern"), ricolorandone di giallo il materiale "Lanterna_Rossa".
// Se càpita di nuovo, "Tools/Make Ramen Lanterns Glow" ripristina il rosso su quel materiale.
//
// Passa dall'editor perché il materiale è un sub-asset dell'FBX e i nodi Lantern* sono interni
// al modello: è Unity a risolvere i fileID dell'istanza prefab a runtime. Fatto il fix questo
// file si può cancellare.
public static class NegozioDischiLanternGlow
{
    private const string ModelRootName = "negozio_dischi";
    private static readonly string[] LanternNodeNames =
    {
        "LanternTop", "LanternBar", "LanternBar.001", "LanternBar.002", "LanternBar.003",
        "LanternRing", "LanternWire", "LanternBot", "LanternGlass",
    };

    private const string MaterialName = "Lantern";
    private const string LightChildName = "Luce Lanterna Gialla";
    private const string PreferredLightParent = "LanternGlass";

    private static readonly Color EmissionColor = new Color(1f, 0.85f, 0.35f) * 1.5f;
    private static readonly Color LightColor = new Color(1f, 0.82f, 0.45f);
    private const float Intensity = 1.4f;
    private const float Range = 2.5f;

    [MenuItem("Tools/Add Negozio Dischi Lantern Glow")]
    private static void AddGlow()
    {
        Transform[] lanternParts = FindLanternParts();
        if (lanternParts.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "Add Negozio Dischi Lantern Glow",
                $"Nessun nodo lanterna trovato sotto un oggetto \"{ModelRootName}\" nella scena aperta. Apri GameplayV2 e riprova.",
                "OK");
            return;
        }

        int litMaterials = MakeMaterialsGlow(lanternParts);
        bool lightAdded = AddLight(lanternParts);

        foreach (Transform part in lanternParts)
        {
            EditorSceneManager.MarkSceneDirty(part.gameObject.scene);
        }

        AssetDatabase.SaveAssets();

        Debug.Log($"[NegozioDischiLanternGlow] Emissione gialla su {litMaterials} materiale/i \"{MaterialName}\". " +
                  $"Luce aggiunta: {(lightAdded ? "sì" : "no (già presente)")}. Salva la scena (Ctrl+S) se il risultato ti va bene.");
    }

    private static Transform[] FindLanternParts()
    {
        var results = new List<Transform>();

        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded)
            {
                continue;
            }

            IEnumerable<Transform> shopRoots = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .Where(t => t.name == ModelRootName);

            foreach (Transform shopRoot in shopRoots)
            {
                results.AddRange(shopRoot.GetComponentsInChildren<Transform>(true)
                    .Where(t => LanternNodeNames.Contains(t.name)));
            }
        }

        return results.ToArray();
    }

    private static int MakeMaterialsGlow(Transform[] lanternParts)
    {
        var materials = new HashSet<Material>();

        foreach (Transform part in lanternParts)
        {
            foreach (Renderer renderer in part.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material mat in renderer.sharedMaterials)
                {
                    if (mat != null && mat.name.StartsWith(MaterialName))
                    {
                        materials.Add(mat);
                    }
                }
            }
        }

        foreach (Material mat in materials)
        {
            Undo.RecordObject(mat, "Add Negozio Dischi Lantern Glow");
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", EmissionColor);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        return materials.Count;
    }

    private static bool AddLight(Transform[] lanternParts)
    {
        Transform lightParent = lanternParts.FirstOrDefault(t => t.name == PreferredLightParent) ?? lanternParts[0];

        if (lightParent.Find(LightChildName) != null)
        {
            return false;
        }

        var lightGO = new GameObject(LightChildName);
        Undo.RegisterCreatedObjectUndo(lightGO, "Add Negozio Dischi Lantern Glow");
        lightGO.transform.SetParent(lightParent, false);
        lightGO.transform.localPosition = Vector3.zero;

        Light light = lightGO.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = LightColor;
        light.intensity = Intensity;
        light.range = Range;
        light.shadows = LightShadows.None;

        return true;
    }
}
