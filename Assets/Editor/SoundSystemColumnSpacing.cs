using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Allarga o stringe una fila di oggetti nella scena aperta — nata per le colonne del sound
// system, ma va bene per qualunque fila:
//
//   Tools/Sound System/Distanzia le colonne selezionate
//   Tools/Sound System/Riavvicina le colonne selezionate
//   Tools/Sound System/Elenca i pezzi del sound system
//
// Sposta e basta. Nessuna mesh viene allargata, nessuna scala viene toccata: gli oggetti restano
// esattamente quelli che sono, cambia solo quanto spazio c'è fra uno e l'altro.
//
// ── Perché va a selezione e non per nome ─────────────────────────────────────────────────────
// Dentro sound_system.fbx ci sono un centinaio di pezzi con nomi che non dicono cosa si vede a
// schermo: "Traliccio_Torre_01" per esempio NON è una colonna, è il traliccio, e chiamandolo per
// nome si spostavano trave e LED invece delle casse. Indovinare i nomi da fuori non funziona,
// mentre in scena si clicca esattamente la cosa che si vuole muovere. Per capire come si chiama
// quello che hai cliccato c'è il terzo comando, che elenca tutto in Console.
//
// ── Come si usa ──────────────────────────────────────────────────────────────────────────────
// Seleziona UN oggetto per colonna (in Scene view o in Hierarchy, Ctrl+clic per aggiungerne) e
// dai il comando: la fila si allarga di PassoDistanza fra un oggetto e il successivo, simmetrica
// rispetto al suo centro. Ridallo quante volte vuoi, Ctrl+Z annulla l'ultima passata.
//
// Se una colonna è fatta di più pezzi separati, seleziona il loro genitore comune invece dei
// singoli pezzi: spostando un genitore i figli vengono via da soli. In Console il comando dice
// quanti oggetti ha mosso, ed è il modo per accorgersene — se hai quattro colonne e ti dice 12,
// hai selezionato i pezzi e non le colonne, e ti ritrovi i pezzi sparpagliati in fila.
//
// ── Su che linea li allarga ──────────────────────────────────────────────────────────────────
// Sull'asse su cui sono già allineati, ricavato dai due oggetti più lontani fra loro. Così
// funziona comunque, qualunque sia la rotazione della scena, senza dover scrivere da nessuna
// parte "sono in fila lungo X".
//
// Dove sta un oggetto lo misura dall'ingombro della sua mesh (Renderer.bounds), NON dal suo
// transform.position: in questo FBX i pivot dei pezzi sono tutti nello stesso punto, all'origine
// del modello, e la distanza fra uno e l'altro sta nei vertici. È il modo in cui Blender esporta
// quando gli oggetti non hanno l'origine spostata sulla propria geometria, e andando di
// transform.position i pezzi risultano tutti sovrapposti.
//
// Lo spostamento invece resta una traslazione del transform: quella funziona lo stesso, il pivot
// non conta, e soprattutto non va a toccare la mesh.
public static class SoundSystemColumnSpacing
{
    // Quanto spazio in più (in unità di mondo) fra un oggetto e il successivo, ad ogni passata.
    // È lo spazio REALE che si vede in scena, non un valore in scala locale: il modello è scalato
    // parecchio (il prop per 2.57 dentro un sound system a 0.63), quindi ragionare in locale qui
    // darebbe numeri che non corrispondono a niente di guardabile.
    private const float PassoDistanza = 1f;

    [MenuItem("Tools/Sound System/Distanzia le colonne selezionate", false, 20)]
    private static void Distanzia()
    {
        Sposta(PassoDistanza, "Distanzia le colonne selezionate");
    }

    [MenuItem("Tools/Sound System/Riavvicina le colonne selezionate", false, 21)]
    private static void Riavvicina()
    {
        Sposta(-PassoDistanza, "Riavvicina le colonne selezionate");
    }

    // Grigiano le due voci finché non c'è abbastanza roba selezionata: con un oggetto solo non
    // esiste nessuna fila da allargare.
    [MenuItem("Tools/Sound System/Distanzia le colonne selezionate", true)]
    private static bool PuoDistanziare()
    {
        return Selection.transforms.Length >= 2;
    }

    [MenuItem("Tools/Sound System/Riavvicina le colonne selezionate", true)]
    private static bool PuoRiavvicinare()
    {
        return Selection.transforms.Length >= 2;
    }

    private static void Sposta(float passo, string titolo)
    {
        // Selection.transforms e non gameObjects: scarta già da sé gli oggetti che sono figli di
        // un altro oggetto selezionato, quindi selezionando per sbaglio sia un genitore sia un
        // suo figlio quest'ultimo non viene spostato due volte.
        List<Transform> colonne = new List<Transform>(Selection.transforms);
        if (colonne.Count < 2)
        {
            EditorUtility.DisplayDialog(
                titolo,
                "Seleziona almeno due oggetti: sono loro la fila da allargare.\n\n" +
                "Uno per colonna. Se una colonna è fatta di più pezzi, seleziona il loro " +
                "genitore comune invece dei singoli pezzi.",
                "OK");
            return;
        }

        Vector3 asse = MisuraAsse(colonne);
        if (asse == Vector3.zero)
        {
            EditorUtility.DisplayDialog(
                titolo,
                "Gli oggetti selezionati occupano tutti lo stesso spazio sul piano orizzontale: " +
                "non c'è nessuna linea lungo cui allargarli.",
                "OK");
            return;
        }

        // Ordinati lungo la linea su cui stanno, così il primo e l'ultimo vanno ai due estremi e
        // quelli in mezzo si spartiscono lo spazio in proporzione. Partendo dall'ordine in cui
        // sono stati selezionati si incrocerebbero fra loro.
        colonne.Sort((a, b) => Vector3.Dot(CentroDi(a), asse).CompareTo(Vector3.Dot(CentroDi(b), asse)));

        float larghezzaPrima = MisuraLarghezza(colonne, asse);

        // Simmetrico rispetto al centro della fila: la fila si allarga sul posto invece di
        // scivolare tutta da una parte.
        float centro = (colonne.Count - 1) * 0.5f;
        List<string> nomi = new List<string>();
        for (int i = 0; i < colonne.Count; i++)
        {
            Undo.RecordObject(colonne[i], titolo);

            // position e non localPosition: lo spostamento è in unità di mondo, e sopra questi
            // oggetti ci sono due scale diverse (il prop e il sound system) più una rotazione.
            // Assegnando la posizione di mondo se ne occupa Unity, senza doverle rifare a mano.
            colonne[i].position += asse * ((i - centro) * passo);
            nomi.Add(colonne[i].name);
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        SceneView.RepaintAll();

        Debug.Log(
            $"[Sound System] {titolo}: {colonne.Count} oggetti spostati, " +
            $"{passo:+0.##;-0.##} unità di spazio fra uno e l'altro. La fila passa da " +
            $"{larghezzaPrima:0.##} a {MisuraLarghezza(colonne, asse):0.##} unità di larghezza.\n" +
            "In ordine lungo la fila: " + string.Join(", ", nomi) + "\n" +
            "Ridai il comando per allargare ancora, Ctrl+Z per annullare quest'ultima passata.");
    }

    // Elenca in Console tutto quello che c'è sotto il sound system, con la posizione reale di
    // ogni pezzo: serve a sapere come si chiama quello che si vede a schermo, visto che i nomi
    // dell'FBX da soli non lo dicono (vedi il commento in testa).
    [MenuItem("Tools/Sound System/Elenca i pezzi del sound system", false, 40)]
    private static void Elenca()
    {
        SoundSystem soundSystem = Object.FindAnyObjectByType<SoundSystem>();
        if (soundSystem == null)
        {
            EditorUtility.DisplayDialog(
                "Elenca i pezzi del sound system",
                "Nella scena aperta non c'è nessun sound system (nessun GameObject con lo script SoundSystem).",
                "OK");
            return;
        }

        List<string> righe = new List<string>();
        Descrivi(soundSystem.transform, soundSystem.transform, righe);

        Debug.Log(
            $"[Sound System] {righe.Count} pezzi sotto '{soundSystem.name}' " +
            "(fra parentesi il centro dell'ingombro in coordinate mondo):\n" +
            string.Join("\n", righe),
            soundSystem);
    }

    private static void Descrivi(Transform radice, Transform nodo, List<string> righe)
    {
        if (nodo != radice)
        {
            string rientro = new string(' ', 2 * Profondita(radice, nodo));
            Vector3 centro = CentroDi(nodo);
            string ingombro = nodo.GetComponentInChildren<Renderer>() != null
                ? $"({centro.x:0.##}, {centro.y:0.##}, {centro.z:0.##})"
                : "(nessuna mesh)";
            righe.Add($"{rientro}{nodo.name}  {ingombro}");
        }

        foreach (Transform figlio in nodo)
        {
            Descrivi(radice, figlio, righe);
        }
    }

    private static int Profondita(Transform radice, Transform nodo)
    {
        int profondita = 0;
        for (Transform t = nodo.parent; t != null && t != radice; t = t.parent)
        {
            profondita++;
        }

        return profondita;
    }

    // Dove sta davvero un oggetto: il centro del suo ingombro in coordinate mondo, non il pivot
    // (vedi il commento in testa: in questo modello i pivot sono tutti sovrapposti). Ripiega sul
    // transform solo se l'oggetto non ha nessuna mesh da misurare.
    private static Vector3 CentroDi(Transform oggetto)
    {
        Renderer[] renderers = oggetto.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return oggetto.position;
        }

        Bounds ingombro = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            ingombro.Encapsulate(renderers[i].bounds);
        }

        return ingombro.center;
    }

    // La direzione orizzontale della fila, presa fra i due oggetti più lontani fra loro: sono
    // quelli dei due estremi, quindi la loro congiungente è la linea su cui stanno tutti. Il
    // verso non conta, lo spostamento è simmetrico.
    private static Vector3 MisuraAsse(List<Transform> colonne)
    {
        float distanzaMassima = 0f;
        Vector3 asse = Vector3.zero;

        for (int i = 0; i < colonne.Count; i++)
        {
            for (int j = i + 1; j < colonne.Count; j++)
            {
                Vector3 fra = CentroDi(colonne[j]) - CentroDi(colonne[i]);
                fra.y = 0f;
                if (fra.sqrMagnitude > distanzaMassima)
                {
                    distanzaMassima = fra.sqrMagnitude;
                    asse = fra;
                }
            }
        }

        return asse.sqrMagnitude < 0.0001f ? Vector3.zero : asse.normalized;
    }

    // Quanto è larga la fila lungo il suo asse, solo per dirlo nel log.
    private static float MisuraLarghezza(List<Transform> colonne, Vector3 asse)
    {
        float minimo = float.MaxValue;
        float massimo = float.MinValue;

        foreach (Transform colonna in colonne)
        {
            float lungoAsse = Vector3.Dot(CentroDi(colonna), asse);
            minimo = Mathf.Min(minimo, lungoAsse);
            massimo = Mathf.Max(massimo, lungoAsse);
        }

        return massimo - minimo;
    }
}
