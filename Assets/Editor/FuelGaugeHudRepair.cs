using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Ricollega i riferimenti dell'HUD (segmenti della vita): Tools/HUD/Ricollega i
// riferimenti del gauge.
//
// ── Cosa era rotto ───────────────────────────────────────────────────────────────────────────
// Quando l'HUD è stato recuperato dal backup (vedi RestoreMissingHud, che infatti avvisava di
// controllare i campi "Missing"), il componente FuelGaugeHUD si è ripreso gli oggetti ma non
// tutti i riferimenti a quegli oggetti. In scena risultava:
//
//   lifeSegments: 8 caselle,   -> la barra vita mostrava numeri sbagliati. ApplyLife accende i
//   ma le prime 4 vuote           primi "lit" segmenti dell'ARRAY, e le caselle vuote vengono
//                                 saltate: con 3 colpi di vita e 8 caselle, a un terzo di vita
//                                 ne andrebbero accese 3, che però sono tutte fra quelle vuote.
//                                 Risultato: barra completamente spenta col Player ancora vivo,
//                                 e i 4 segmenti veri fermi sul colore con cui erano nati.
//
// Gli oggetti erano tutti lì, quindi non c'era niente da ricostruire:
// bastava riagganciarli. Questo comando li ritrova per nome sotto l'HUD e riempie i campi.
//
// I segmenti vengono ordinati da sinistra a destra per posizione sullo schermo e non per ordine
// in Hierarchy: è l'ordine che conta davvero (la barra si svuota da destra) ed è quello che
// FuelGaugeHUD si aspetta, mentre l'ordine in Hierarchy può essere qualsiasi.
public static class FuelGaugeHudRepair
{
    private const string SegmentName = "life_segment";

    [MenuItem("Tools/HUD/Ricollega i riferimenti del gauge")]
    private static void Repair()
    {
        FuelGaugeHUD hud = Object.FindAnyObjectByType<FuelGaugeHUD>();
        if (hud == null)
        {
            EditorUtility.DisplayDialog(
                "HUD",
                "Nessun FuelGaugeHUD nella scena aperta. Apri GameplayV2 e riprova.",
                "OK");
            return;
        }

        Undo.RecordObject(hud, "Ricollega i riferimenti del gauge");

        Image[] segments = hud.GetComponentsInChildren<Image>(true)
            .Where(image => image.name.ToLowerInvariant().Contains(SegmentName))
            .OrderBy(image => image.rectTransform.position.x)
            .ToArray();

        if (segments.Length > 0)
        {
            hud.lifeSegments = segments;
        }

        EditorUtility.SetDirty(hud);
        EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);

        string message =
            $"Segmenti vita: {segments.Length} ricollegati, tutti bianchi; ogni colpo ne fa sparire uno.";

        if (segments.Length == 0)
        {
            message += $"\n\nNon ho trovato oggetti chiamati \"{SegmentName}\" sotto l'HUD.";
        }

        EditorUtility.DisplayDialog("HUD", message, "OK");
        Debug.Log($"[HUD] {message.Replace('\n', ' ')}", hud);
    }

    // Il pad del controller veniva costruito solo in Play (FuelGaugeHUD.BuildController), quindi
    // in Game fuori dal Play si vedeva ancora la vecchia barra col fulmine e il numero degli
    // scatti. Qui il pad e le sue scritte diventano oggetti veri della scena, e il vecchio
    // contatore scatti (fulmine + numero) viene tolto: a runtime FuelGaugeHUD ritrova il pad per
    // nome ("controller") e non ne costruisce un altro. Rilanciarlo non crea doppioni: rimette
    // solo i segmenti vita centrati sotto il pad.
    [MenuItem("Tools/HUD/Salva il pad in scena")]
    private static void BakeController()
    {
        FuelGaugeHUD hud = Object.FindAnyObjectByType<FuelGaugeHUD>();
        if (hud == null)
        {
            EditorUtility.DisplayDialog("HUD", "Nessun FuelGaugeHUD nella scena aperta. Apri GameplayV2 e riprova.", "OK");
            return;
        }
        if (hud.controllerTexture == null)
        {
            EditorUtility.DisplayDialog("HUD", "Sul FuelGaugeHUD manca l'immagine del controller (Controller Texture).", "OK");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(hud.gameObject, "Salva il pad in scena");
        bool existed = hud.GetComponentsInChildren<RawImage>(true).Any(image => image.name == "controller");

        RectTransform controller = hud.BakeController();
        if (controller == null)
        {
            EditorUtility.DisplayDialog("HUD", "Non ho trovato i segmenti vita (\"" + SegmentName + "\") sotto l'HUD: niente su cui mettere il pad.", "OK");
            return;
        }
        if (!existed) Undo.RegisterCreatedObjectUndo(controller.gameObject, "Salva il pad in scena");

        int removed = RemoveDashCounter(hud);

        EditorUtility.SetDirty(hud);
        EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
        Selection.activeGameObject = controller.gameObject;

        string message = (existed ? "Il pad c'era già: segmenti vita ricentrati sotto." : "Pad salvato in scena (oggetto \"controller\").") +
                         (removed > 0 ? " Tolto il vecchio contatore scatti." : "") +
                         " Salva la scena (Ctrl+S).";
        Debug.Log("[HUD] " + message, controller);
    }

    // Fulmine e numero degli scatti, con il loro contenitore se resta vuoto ("SpeedUpCounter").
    private static int RemoveDashCounter(FuelGaugeHUD hud)
    {
        var doomed = hud.GetComponentsInChildren<Image>(true)
            .Where(image => image.name.ToLowerInvariant().Contains("bolt"))
            .Select(image => image.gameObject)
            .ToList();
        if (hud.dashCountLabel != null) doomed.Add(hud.dashCountLabel.gameObject);
        hud.dashCountLabel = null;

        int removed = 0;
        foreach (GameObject go in doomed.Distinct())
        {
            Transform parent = go.transform.parent;
            Undo.DestroyObjectImmediate(go);
            removed++;
            if (parent != null && parent != hud.transform && parent.childCount == 0 && parent.GetComponents<Component>().Length == 1)
            {
                Undo.DestroyObjectImmediate(parent.gameObject);
            }
        }
        return removed;
    }
}
