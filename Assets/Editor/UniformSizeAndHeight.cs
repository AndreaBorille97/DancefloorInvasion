using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Porta gli oggetti selezionati tutti alla stessa dimensione e alla stessa altezza:
//
//   Tools/Scena/Uniforma dimensione e altezza
//
// Nato per LEFT_DECO e RIGHT_DECO, le due facce ai lati del sound system, ma va bene per
// qualunque coppia (o gruppo) di oggetti che devono leggersi come una cosa sola.
//
// ── Perché non basta copiare la scala ────────────────────────────────────────────────────────
// LEFT_DECO e RIGHT_DECO sono due FBX diversi, con mesh di dimensioni diverse: la stessa
// localScale su tutti e due NON dà la stessa dimensione a schermo, dà solo lo stesso numero
// nell'Inspector. Per questo qui la dimensione si misura dall'ingombro reale (Renderer.bounds,
// in coordinate mondo) e la scala si ricava da quello.
//
// ── Cosa fa, esattamente ─────────────────────────────────────────────────────────────────────
// Prende il PIÙ ALTO fra i selezionati e lo lascia esattamente dov'è: è lui il riferimento, così
// almeno un oggetto non si muove e si capisce subito verso cosa sono andati gli altri.
//
// Gli altri:
//  - vengono scalati in modo UNIFORME (stesso fattore su X, Y e Z) finché non sono alti come il
//    riferimento. Uniforme e non solo in verticale apposta: sono due disegni, stirarli in
//    altezza li deformerebbe. Restano quindi della stessa altezza ma ognuno con le sue
//    proporzioni, che è quello che si intende per "della stessa dimensione" parlando di figure
//    diverse;
//  - vengono alzati o abbassati finché il centro del loro ingombro non è alla stessa quota di
//    quello del riferimento.
//
// Non tocca la posizione orizzontale: dove stanno sulla fila resta una scelta fatta in scena.
public static class UniformSizeAndHeight
{
    [MenuItem("Tools/Scena/Uniforma dimensione e altezza", false, 40)]
    private static void Uniforma()
    {
        const string titolo = "Uniforma dimensione e altezza";

        // Selection.transforms e non gameObjects: scarta già da sé gli oggetti che sono figli di
        // un altro oggetto selezionato, che verrebbero scalati due volte.
        List<Transform> selezionati = new List<Transform>();
        foreach (Transform t in Selection.transforms)
        {
            if (MisuraIngombro(t, out Bounds _))
            {
                selezionati.Add(t);
                continue;
            }

            Debug.LogWarning($"[Scena] '{t.name}' non ha nessuna mesh da misurare: lo salto.", t);
        }

        if (selezionati.Count < 2)
        {
            EditorUtility.DisplayDialog(
                titolo,
                "Seleziona almeno due oggetti con una mesh: il più alto fa da riferimento, gli " +
                "altri vengono portati alla sua dimensione e alla sua altezza.",
                "OK");
            return;
        }

        Transform riferimento = PiuAlto(selezionati);
        MisuraIngombro(riferimento, out Bounds ingombroRiferimento);
        float altezzaTarget = ingombroRiferimento.size.y;
        float quotaTarget = ingombroRiferimento.center.y;

        List<string> righe = new List<string>();
        foreach (Transform oggetto in selezionati)
        {
            if (oggetto == riferimento)
            {
                righe.Add($"  {oggetto.name}: riferimento, lasciato dov'è " +
                          $"(alto {altezzaTarget:0.##}, centro a quota {quotaTarget:0.##})");
                continue;
            }

            MisuraIngombro(oggetto, out Bounds prima);

            Undo.RecordObject(oggetto, titolo);

            // Fattore uniforme sui tre assi: l'oggetto cresce o cala mantenendo le sue
            // proporzioni. La rotazione non dà fastidio perché la scala è locale.
            if (prima.size.y > 0.0001f)
            {
                oggetto.localScale *= altezzaTarget / prima.size.y;
            }

            // L'ingombro va rimisurato DOPO la scalatura: scalando, il centro si sposta di suo
            // (il pivot di questi FBX non sta al centro della mesh), quindi correggere la quota
            // con la misura di prima lascerebbe l'oggetto fuori posto.
            MisuraIngombro(oggetto, out Bounds dopo);
            oggetto.position += Vector3.up * (quotaTarget - dopo.center.y);

            righe.Add($"  {oggetto.name}: da {prima.size.y:0.##} a {altezzaTarget:0.##} di altezza " +
                      $"(x{altezzaTarget / Mathf.Max(prima.size.y, 0.0001f):0.###}), " +
                      $"centro da quota {prima.center.y:0.##} a {quotaTarget:0.##}");
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        SceneView.RepaintAll();

        Debug.Log(
            $"[Scena] {titolo}: riferimento '{riferimento.name}'.\n" + string.Join("\n", righe) +
            "\nLa posizione orizzontale non è stata toccata. Ctrl+Z per annullare.",
            riferimento);
    }

    [MenuItem("Tools/Scena/Uniforma dimensione e altezza", true)]
    private static bool PuoUniformare()
    {
        return Selection.transforms.Length >= 2;
    }

    private static Transform PiuAlto(List<Transform> oggetti)
    {
        Transform piuAlto = oggetti[0];
        MisuraIngombro(piuAlto, out Bounds massimo);

        foreach (Transform oggetto in oggetti)
        {
            MisuraIngombro(oggetto, out Bounds ingombro);
            if (ingombro.size.y > massimo.size.y)
            {
                massimo = ingombro;
                piuAlto = oggetto;
            }
        }

        return piuAlto;
    }

    // L'ingombro reale dell'oggetto in coordinate mondo, mesh dei figli comprese. false se non
    // c'è niente da misurare: un oggetto senza mesh non ha una dimensione da uniformare.
    private static bool MisuraIngombro(Transform oggetto, out Bounds ingombro)
    {
        ingombro = new Bounds(oggetto.position, Vector3.zero);

        Renderer[] renderers = oggetto.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return false;
        }

        ingombro = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            ingombro.Encapsulate(renderers[i].bounds);
        }

        return true;
    }
}
