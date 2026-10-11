using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Ricollega i riferimenti del gauge (lancetta e segmenti della vita): Tools/HUD/Ricollega i
// riferimenti del gauge.
//
// ── Cosa era rotto ───────────────────────────────────────────────────────────────────────────
// Quando l'HUD è stato recuperato dal backup (vedi RestoreMissingHud, che infatti avvisava di
// controllare i campi "Missing"), il componente FuelGaugeHUD si è ripreso gli oggetti ma non
// tutti i riferimenti a quegli oggetti. In scena risultava:
//
//   needle: nessuno            -> la lancetta non si muoveva MAI. FuelGaugeHUD.Update calcola
//                                 l'angolo e poi lo scrive su un riferimento vuoto: nessun
//                                 errore in Console, semplicemente non succede niente.
//
//   lifeSegments: 8 caselle,   -> la barra vita mostrava numeri sbagliati. ApplyLife accende i
//   ma le prime 4 vuote           primi "lit" segmenti dell'ARRAY, e le caselle vuote vengono
//                                 saltate: con 3 colpi di vita e 8 caselle, a un terzo di vita
//                                 ne andrebbero accese 3, che però sono tutte fra quelle vuote.
//                                 Risultato: barra completamente spenta col Player ancora vivo,
//                                 e i 4 segmenti veri fermi sul colore con cui erano nati.
//
// Gli oggetti erano tutti lì (8 life_segment e 1 needle), quindi non c'era niente da ricostruire:
// bastava riagganciarli. Questo comando li ritrova per nome sotto l'HUD e riempie i campi.
//
// I segmenti vengono ordinati da sinistra a destra per posizione sullo schermo e non per ordine
// in Hierarchy: è l'ordine che conta davvero (la barra si svuota da destra) ed è quello che
// FuelGaugeHUD si aspetta, mentre l'ordine in Hierarchy può essere qualsiasi.
public static class FuelGaugeHudRepair
{
    private const string NeedleName = "needle";
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

        RectTransform needle = hud.GetComponentsInChildren<RectTransform>(true)
            .FirstOrDefault(rect => rect.name.ToLowerInvariant().Contains(NeedleName));

        Image[] segments = hud.GetComponentsInChildren<Image>(true)
            .Where(image => image.name.ToLowerInvariant().Contains(SegmentName))
            .OrderBy(image => image.rectTransform.position.x)
            .ToArray();

        hud.needle = needle;
        if (segments.Length > 0)
        {
            hud.lifeSegments = segments;
        }

        // I colori vanno riscritti da qui e non basta cambiarli nel codice: in scena erano già
        // salvati i vecchi valori (segmenti verdi e consumati grigio scuro), e un valore già
        // serializzato ha sempre la meglio sul default della classe. Finché non li si sovrascrive,
        // cambiare il codice non si vedrebbe.
        hud.lifeOn = Color.white;
        hud.lifeOff = new Color(1f, 1f, 1f, 0f); // trasparente: il segmento consumato sparisce

        EditorUtility.SetDirty(hud);
        EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);

        string message =
            $"Lancetta: {(needle != null ? "ricollegata" : "NON TROVATA, cerco un oggetto chiamato \"" + NeedleName + "\"")}.\n" +
            $"Segmenti vita: {segments.Length} ricollegati, tutti bianchi; ogni colpo ne fa sparire uno.";

        if (segments.Length == 0)
        {
            message += $"\n\nNon ho trovato oggetti chiamati \"{SegmentName}\" sotto l'HUD.";
        }

        EditorUtility.DisplayDialog("HUD", message, "OK");
        Debug.Log($"[HUD] {message.Replace('\n', ' ')}", hud);
    }
}
