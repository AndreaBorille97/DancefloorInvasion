using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Utility una tantum: sotto il modello "ramen" (Assets/Prefabs/models/ramen.fbx) ci sono due
// lanterne di carta, i nodi "Lanterna_Dx" e "Lanterna_Sx" (materiale "Lanterna_Rossa"). Sono
// mesh statiche, quindi da sole non emettono luce: qui aggiungiamo una Point Light figlia a
// ciascuna, rossa e debole, per simulare il bagliore della carta illuminata dall'interno.
// Passa dall'editor (invece che a mano nello YAML della scena) perché "Lanterna_Dx"/"Lanterna_Sx"
// sono nodi interni all'FBX: è Unity a risolvere i fileID dell'istanza prefab a runtime.
// A luci aggiunte questo file si può cancellare.
public static class RamenLanternLights
{
    private const string LightChildName = "Luce Lanterna Rossa";
    private static readonly string[] LanternNodeNames = { "Lanterna_Dx", "Lanterna_Sx" };

    private static readonly Color LanternRed = new Color(1f, 0.12f, 0.06f);
    private const float Intensity = 1.4f;
    private const float Range = 2.5f;

    [MenuItem("Tools/Add Ramen Lantern Lights")]
    private static void AddLights()
    {
        Transform[] lanterns = FindLanternNodes();
        if (lanterns.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "Add Ramen Lantern Lights",
                "Nessun nodo \"Lanterna_Dx\"/\"Lanterna_Sx\" trovato nella scena aperta. Apri GameplayV2 e riprova.",
                "OK");
            return;
        }

        int added = 0, skipped = 0;

        foreach (Transform lantern in lanterns)
        {
            if (lantern.Find(LightChildName) != null)
            {
                skipped++;
                continue;
            }

            var lightGO = new GameObject(LightChildName);
            Undo.RegisterCreatedObjectUndo(lightGO, "Add Ramen Lantern Lights");
            lightGO.transform.SetParent(lantern, false);
            lightGO.transform.localPosition = Vector3.zero;

            Light light = lightGO.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = LanternRed;
            light.intensity = Intensity;
            light.range = Range;
            light.shadows = LightShadows.None;

            added++;
        }

        foreach (Transform lantern in lanterns)
        {
            EditorSceneManager.MarkSceneDirty(lantern.gameObject.scene);
        }

        Debug.Log($"[RamenLanternLights] {added} luce/i aggiunta/e, {skipped} già presenti. " +
                  "Salva la scena (Ctrl+S) se il risultato ti va bene.");
    }

    private static Transform[] FindLanternNodes()
    {
        var results = new System.Collections.Generic.List<Transform>();

        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded)
            {
                continue;
            }

            results.AddRange(scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .Where(t => LanternNodeNames.Contains(t.name)));
        }

        return results.ToArray();
    }
}
