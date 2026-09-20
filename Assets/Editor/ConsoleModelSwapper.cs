using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Utility una tantum: sotto l'oggetto "Console" della scena, sostituisce il modello figlio
// "console_completa" con "capanna_console" (Assets/Prefabs/models/capanna_console.fbx),
// conservando posizione/rotazione/scala locali e l'ordine tra i figli. Passa dall'editor
// così è Unity a risolvere i fileID interni dell'FBX: farlo a mano nello YAML della scena
// romperebbe i riferimenti, dato che i due FBX hanno strutture interne diverse.
// A swap fatto questo file si può cancellare.
public static class ConsoleModelSwapper
{
    private const string OldChildName = "console_completa";
    private const string NewModelPath = "Assets/Prefabs/models/capanna_console.fbx";
    private const string NewChildName = "capanna_console";

    [MenuItem("Tools/Swap Console Model (console_completa → capanna_console)")]
    private static void Swap()
    {
        Transform oldChild = FindOldChild(out Transform consoleParent);
        if (oldChild == null)
        {
            EditorUtility.DisplayDialog(
                "Swap Console Model",
                $"Nessun figlio \"{OldChildName}\" trovato sotto un oggetto \"Console\" nella scena aperta. " +
                "Apri GameplayV2 e riprova.",
                "OK");
            return;
        }

        GameObject newModel = AssetDatabase.LoadAssetAtPath<GameObject>(NewModelPath);
        if (newModel == null)
        {
            EditorUtility.DisplayDialog("Swap Console Model", $"Modello non trovato: {NewModelPath}", "OK");
            return;
        }

        // TRS locale e posizione tra i figli da riportare sul nuovo modello.
        Vector3 localPosition = oldChild.localPosition;
        Quaternion localRotation = oldChild.localRotation;
        Vector3 localScale = oldChild.localScale;
        int siblingIndex = oldChild.GetSiblingIndex();

        GameObject newChild = (GameObject)PrefabUtility.InstantiatePrefab(newModel, consoleParent.gameObject.scene);
        Undo.RegisterCreatedObjectUndo(newChild, "Swap Console Model");
        newChild.name = NewChildName;

        newChild.transform.SetParent(consoleParent, false);
        newChild.transform.localPosition = localPosition;
        newChild.transform.localRotation = localRotation;
        newChild.transform.localScale = localScale;
        newChild.transform.SetSiblingIndex(siblingIndex);

        Undo.DestroyObjectImmediate(oldChild.gameObject);

        EditorSceneManager.MarkSceneDirty(consoleParent.gameObject.scene);
        Selection.activeGameObject = newChild;

        Debug.Log($"[ConsoleModelSwapper] \"{OldChildName}\" sostituito con \"{NewChildName}\" sotto \"{consoleParent.name}\". " +
                  "Salva la scena (Ctrl+S) se il risultato ti va bene; TRS locale conservato, regola a mano se il nuovo modello ha pivot/scala diversi.");
    }

    private static Transform FindOldChild(out Transform consoleParent)
    {
        consoleParent = null;

        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded)
            {
                continue;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.name.StartsWith(OldChildName) || t.parent == null)
                    {
                        continue;
                    }

                    if (t.parent.name == "Console")
                    {
                        consoleParent = t.parent;
                        return t;
                    }
                }
            }
        }

        // Fallback: qualunque genitore, se un solo "console_completa" esiste in scena.
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded)
            {
                continue;
            }

            Transform[] matches = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .Where(t => t.name.StartsWith(OldChildName) && t.parent != null)
                .ToArray();

            if (matches.Length == 1)
            {
                consoleParent = matches[0].parent;
                return matches[0];
            }
        }

        return null;
    }
}
