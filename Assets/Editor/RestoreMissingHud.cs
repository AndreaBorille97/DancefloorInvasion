using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// L'intera HUD (due GameObject radice "Canvas" — uno dei due contiene "Minimap" come figlio,
// l'altro il tachimetro/vita/GameOver) è sparita da GameplayV2.unity rispetto all'ultimo commit
// (a68e25a), prima ancora di iniziare a lavorare sulle luci delle lanterne. Assets/_Recovery/
// GameplayV2_HUD_backup.unity è una copia di quella versione (estratta con "git show HEAD:...").
//
// Qui apriamo quel backup come scena aggiuntiva, spostiamo i due "Canvas" nella scena attiva
// (SceneManager.MoveGameObjectToScene, la stessa cosa che fa un copia-incolla in Hierarchy ma
// scriptabile) e richiudiamo il backup senza salvarlo. Passa dall'editor invece che dallo YAML
// a mano perché è Unity a dover re-istanziare correttamente RectTransform/Canvas/riferimenti agli
// script, che nel frattempo (MinimapUI.cs, PlayerHealth.cs, PlayerSpeedAmmo.cs) sono cambiati:
// dopo il ripristino controlla nell'Inspector eventuali campi "Missing" e nel Console eventuali
// warning, perché i field serializzati vecchi potrebbero non combaciare più 1:1 col codice nuovo.
// A ripristino avvenuto e verificato questo file (e il backup) si possono cancellare.
public static class RestoreMissingHud
{
    private const string BackupScenePath = "Assets/_Recovery/GameplayV2_HUD_backup.unity";
    private const string RootObjectName = "Canvas";

    [MenuItem("Tools/Restore Missing HUD (Canvas + Minimap)")]
    private static void Restore()
    {
        Scene targetScene = SceneManager.GetActiveScene();
        if (!targetScene.IsValid())
        {
            EditorUtility.DisplayDialog("Restore Missing HUD", "Nessuna scena attiva aperta.", "OK");
            return;
        }

        int existingCanvases = targetScene.GetRootGameObjects().Count(go => go.name == RootObjectName);
        if (existingCanvases > 0)
        {
            bool proceed = EditorUtility.DisplayDialog(
                "Restore Missing HUD",
                $"Nella scena attiva {existingCanvases} oggetto/i \"Canvas\" già presente/i. Continuare potrebbe creare doppioni. Procedere comunque?",
                "Procedi", "Annulla");
            if (!proceed)
            {
                return;
            }
        }

        Scene backupScene = EditorSceneManager.OpenScene(BackupScenePath, OpenSceneMode.Additive);
        if (!backupScene.IsValid())
        {
            EditorUtility.DisplayDialog("Restore Missing HUD", $"Impossibile aprire {BackupScenePath}", "OK");
            return;
        }

        GameObject[] toMove = backupScene.GetRootGameObjects()
            .Where(go => go.name == RootObjectName)
            .ToArray();

        if (toMove.Length == 0)
        {
            EditorUtility.DisplayDialog("Restore Missing HUD", $"Nessun oggetto \"{RootObjectName}\" trovato nel backup.", "OK");
            EditorSceneManager.CloseScene(backupScene, true);
            return;
        }

        foreach (GameObject go in toMove)
        {
            Undo.RegisterCreatedObjectUndo(go, "Restore Missing HUD");
            SceneManager.MoveGameObjectToScene(go, targetScene);
        }

        EditorSceneManager.CloseScene(backupScene, true);
        EditorSceneManager.MarkSceneDirty(targetScene);
        Selection.objects = toMove;

        Debug.Log($"[RestoreMissingHud] Ripristinati {toMove.Length} oggetti \"{RootObjectName}\" " +
                  "(uno contiene \"Minimap\" come figlio) da " + BackupScenePath + ". " +
                  "Controlla nell'Inspector eventuali riferimenti \"Missing\" sugli script (i campi potrebbero essere cambiati nel frattempo), poi salva la scena (Ctrl+S).");
    }
}
