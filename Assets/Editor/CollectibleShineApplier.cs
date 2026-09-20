using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Utility una tantum: attacca CollectibleShine a tutte le ammo, cioè a ogni prefab che ha
// addosso uno script di raccolta (quelli che si chiamano "...Pickup"). Da lì in poi ogni
// munizione sta sollevata da terra, gira, ondeggia, scintilla e fa il suo sbuffo di stelline
// quando viene presa, senza che nessuno degli script di raccolta debba saperne niente.
//
// Riconoscere le ammo dal nome del componente invece che da un elenco scritto a mano vuol dire
// che un'ammo aggiunta domani viene trattata come le altre semplicemente rilanciando questa voce
// di menu. È ripetibile: i prefab che ce l'hanno già vengono lasciati stare.
//
// A lavoro fatto questo file si può cancellare.
public static class CollectibleShineApplier
{
    private const string PrefabFolder = "Assets/Prefabs";

    [MenuItem("Tools/Add Collectible Shine to all ammo prefabs")]
    private static void Apply()
    {
        List<string> updated = new List<string>();
        List<string> alreadyDone = new List<string>();

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null || !IsPickup(asset))
            {
                continue;
            }

            if (asset.GetComponent<CollectibleShine>() != null)
            {
                alreadyDone.Add(asset.name);
                continue;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                contents.AddComponent<CollectibleShine>();
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            updated.Add(asset.name);
        }

        AssetDatabase.SaveAssets();

        string already = alreadyDone.Count > 0 ? $" Ce l'avevano già: {string.Join(", ", alreadyDone)}." : "";
        Debug.Log(updated.Count > 0
            ? $"[CollectibleShineApplier] CollectibleShine aggiunto a: {string.Join(", ", updated)}.{already}"
            : $"[CollectibleShineApplier] Nessun prefab da aggiornare.{already}");
    }

    // Un'ammo è un prefab che ha addosso uno script di raccolta. Si guarda il nome del tipo e non
    // una classe base comune perché gli script di pickup non ne hanno una: sono nati ognuno per
    // conto suo, e riscriverli tutti solo per poterli riconoscere sarebbe il modo più laborioso
    // di ottenere la stessa cosa.
    private static bool IsPickup(GameObject prefab)
    {
        foreach (MonoBehaviour behaviour in prefab.GetComponentsInChildren<MonoBehaviour>(true))
        {
            // null = script mancante sul prefab: si salta invece di far saltare tutta l'utility.
            if (behaviour != null && behaviour.GetType().Name.EndsWith("Pickup"))
            {
                return true;
            }
        }

        return false;
    }
}
