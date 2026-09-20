using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Prepara le animazioni del nuovo modello del sound system (sound_system.fbx), che prende il
// posto di Colonna4. È lo stesso modello con i tentacoli tolti (ora sono un FBX a parte, in
// models/TentacoliSound) e in più i quattro LED_Cassa, quindi vale identico il ragionamento
// spiegato per esteso in Colonna4PrefabBuilder, qui in breve:
//
//   - Blender esporta ogni azione su ogni oggetto: 40 take = 5 oggetti (Faretto_01..04, Occhio)
//     x 8 azioni. La take buona di un oggetto è quella la cui azione porta il suo nome
//     (Faretto_01|Faretto_01Action.002, Occhio|OcchioAction.002); il resto sono combinazioni
//     senza senso, incluse le Sway_Tentacolo_* rimaste orfane dei tentacoli.
//   - Ogni take contiene però le curve di TUTTI gli oggetti. Riprodotte così, più clip che
//     scrivono sugli stessi transform si spartiscono il peso e il prop si muove sbagliato: per
//     questo ogni take viene ritagliata alle sole curve del suo oggetto e salvata pulita in
//     SoundSystem_Clips/.
//   - Le clip restano separate, ognuna in loop per conto suo, riprodotte tutte insieme dal
//     vecchio componente Animation via PlayAllAnimations. Niente Animator Controller: non c'è
//     nessuno stato da gestire.
//
// A modello sistemato e scena salvata, questo file e Colonna4PrefabBuilder si possono cancellare.
public static class SoundSystemAnimationSetup
{
    private const string FbxPath = "Assets/Prefabs/models/sound_system.fbx";
    private const string ClipsFolder = "Assets/Prefabs/SoundSystem_Clips";

    [MenuItem("Tools/Sound System/Prepara le animazioni del nuovo modello")]
    private static void Setup()
    {
        AnimationClip[] clips = PreparaClip();
        if (clips == null) return;

        int sistemate = 0;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!IsRadiceModello(t.gameObject)) continue;
                Monta(t.gameObject, clips);
                EditorUtility.SetDirty(t.gameObject);
                sistemate++;
            }
        }

        if (sistemate > 0)
        {
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[SoundSystem] {clips.Length} clip pronte, {sistemate} istanze del modello sistemate " +
                      "nella scena aperta. Ricordati di salvare la scena.");
        }
        else
        {
            Debug.LogWarning($"[SoundSystem] {clips.Length} clip pronte in {ClipsFolder}, ma nella scena aperta " +
                             $"non c'è nessuna istanza di {FbxPath}: trascinacela e rilancia questo comando.");
        }
    }

    // La radice di un'istanza del modello: viene dall'FBX e sopra di lei non c'è un altro pezzo
    // della stessa istanza, così i figli del modello non vengono toccati.
    private static bool IsRadiceModello(GameObject go)
    {
        if (SorgentePrefab(go) != FbxPath) return false;
        return go.transform.parent == null || SorgentePrefab(go.transform.parent.gameObject) != FbxPath;
    }

    private static string SorgentePrefab(GameObject go)
    {
        Object source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(go);
        return source == null ? null : AssetDatabase.GetAssetPath(source);
    }

    // Rig su Legacy (è quello che il componente Animation sa riprodurre), clip scritte nella
    // root (percorsi delle curve relativi all'oggetto che porta l'Animation) e curve non
    // ricampionate: il ricampionamento converte gli Euler di Blender in quaternioni e le
    // rotazioni oltre i 180 gradi ci arrivano storte. Fuori anche la camera: l'FBX ne contiene
    // una, e importata finirebbe in scena come seconda Camera.
    private static bool SistemaImport()
    {
        var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (importer == null)
        {
            EditorUtility.DisplayDialog("Sound System", $"FBX non trovato: {FbxPath}", "OK");
            return false;
        }

        bool dirty = false;
        if (!importer.importAnimation) { importer.importAnimation = true; dirty = true; }
        if (importer.animationType != ModelImporterAnimationType.Legacy)
        {
            importer.animationType = ModelImporterAnimationType.Legacy;
            dirty = true;
        }
        if (importer.resampleCurves) { importer.resampleCurves = false; dirty = true; }
        if (importer.animationCompression != ModelImporterAnimationCompression.Off)
        {
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            dirty = true;
        }
        if (importer.importCameras) { importer.importCameras = false; dirty = true; }
#pragma warning disable 618
        if (importer.generateAnimations != ModelImporterGenerateAnimations.InRoot)
        {
            importer.generateAnimations = ModelImporterGenerateAnimations.InRoot;
            dirty = true;
        }
#pragma warning restore 618
        if (dirty) importer.SaveAndReimport();
        return true;
    }

    private static AnimationClip[] CaricaTake()
    {
        return AssetDatabase.LoadAllAssetsAtPath(FbxPath)
            .OfType<AnimationClip>()
            .Where(c => (c.hideFlags & HideFlags.HideInHierarchy) == 0 && !c.name.StartsWith("__preview__"))
            .ToArray();
    }

    // Percorso di ogni transform del modello relativo alla root, che è il riferimento dei
    // percorsi nelle curve.
    private static Dictionary<string, string> PercorsiOggetti()
    {
        if (!SistemaImport()) return null;

        GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        var temp = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        var percorsi = new Dictionary<string, string>();
        foreach (Transform t in temp.GetComponentsInChildren<Transform>(true))
        {
            if (!percorsi.ContainsKey(t.name)) percorsi[t.name] = AnimationUtility.CalculateTransformPath(t, temp.transform);
        }
        Object.DestroyImmediate(temp);
        return percorsi;
    }

    private static AnimationClip[] PreparaClip()
    {
        Dictionary<string, string> percorsi = PercorsiOggetti();
        if (percorsi == null) return null;

        AnimationClip[] takes = CaricaTake();
        if (takes.Length == 0)
        {
            EditorUtility.DisplayDialog("Sound System",
                "Nessuna take trovata nell'FBX: controlla 'Import Animation' nelle impostazioni " +
                $"di import di {FbxPath}.", "OK");
            return null;
        }

        if (AssetDatabase.IsValidFolder(ClipsFolder)) AssetDatabase.DeleteAsset(ClipsFolder);
        AssetDatabase.CreateFolder("Assets/Prefabs", "SoundSystem_Clips");

        var risultato = new List<AnimationClip>();
        var report = new List<string>();

        foreach (string oggetto in takes.Select(t => Oggetto(t.name)).Distinct().OrderBy(n => n))
        {
            AnimationClip take = TakeDellOggetto(takes, oggetto);
            if (take == null)
            {
                report.Add($"  {oggetto}: nessuna take con l'azione omonima, saltato.");
                continue;
            }

            if (!percorsi.TryGetValue(oggetto, out string percorso))
            {
                report.Add($"  {oggetto}: nel modello non c'è nessun transform con questo nome, saltato.");
                continue;
            }

            AnimationClip ritagliata = Ritaglia(take, percorso, oggetto, out int tenute, out int scartate);
            if (ritagliata == null)
            {
                report.Add($"  {oggetto}: la take '{take.name}' non contiene nessuna curva che lo muova " +
                           "(azione vuota nell'export), saltato. Va riesportato da Blender.");
                continue;
            }

            risultato.Add(ritagliata);
            report.Add($"  {oggetto}: '{take.name}' -> {tenute} curve tenute, {scartate} scartate " +
                       $"(erano di altri oggetti), {ritagliata.length:0.00}s");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[SoundSystem] {risultato.Count} clip in {ClipsFolder}:\n" + string.Join("\n", report));

        if (risultato.Count == 0)
        {
            EditorUtility.DisplayDialog("Sound System", "Nessuna clip utilizzabile. Guarda la Console.", "OK");
            return null;
        }
        return risultato.ToArray();
    }

    // Fra le take di un oggetto tiene quella la cui azione porta il suo nome, scartando i
    // doppioni ".001"/".002" quando esiste anche la versione senza suffisso.
    private static AnimationClip TakeDellOggetto(AnimationClip[] takes, string oggetto)
    {
        return takes
            .Where(c => Oggetto(c.name) == oggetto && c.length > 0f && Azione(c.name).Contains(oggetto))
            .OrderBy(c => HaSuffissoNumerico(Azione(c.name)) ? 1 : 0)
            .ThenBy(c => c.name.Length)
            .FirstOrDefault();
    }

    // Copia della take con le sole curve dell'oggetto indicato (e dei suoi figli), salvata come
    // clip legacy in loop. Ritorna null se non resta niente che si muova.
    private static AnimationClip Ritaglia(AnimationClip take, string percorso, string nome, out int tenute, out int scartate)
    {
        tenute = 0;
        scartate = 0;
        var copia = new AnimationClip { frameRate = take.frameRate };
        bool qualcosaSiMuove = false;

        foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(take))
        {
            if (!Appartiene(b.path, percorso)) { scartate++; continue; }
            AnimationCurve curva = AnimationUtility.GetEditorCurve(take, b);
            AnimationUtility.SetEditorCurve(copia, b, curva);
            if (Muove(curva)) qualcosaSiMuove = true;
            tenute++;
        }

        foreach (EditorCurveBinding b in AnimationUtility.GetObjectReferenceCurveBindings(take))
        {
            if (!Appartiene(b.path, percorso)) { scartate++; continue; }
            AnimationUtility.SetObjectReferenceCurve(copia, b, AnimationUtility.GetObjectReferenceCurve(take, b));
            tenute++;
            qualcosaSiMuove = true;
        }

        if (!qualcosaSiMuove)
        {
            Object.DestroyImmediate(copia);
            return null;
        }

        copia.legacy = true;
        copia.wrapMode = WrapMode.Loop;
        AssetDatabase.CreateAsset(copia, $"{ClipsFolder}/{nome}.anim");
        return copia;
    }

    private static bool Appartiene(string pathCurva, string percorsoOggetto)
    {
        return pathCurva == percorsoOggetto || pathCurva.StartsWith(percorsoOggetto + "/");
    }

    private static bool Muove(AnimationCurve curva)
    {
        if (curva == null || curva.length < 2) return false;
        float min = curva[0].value, max = min;
        for (int i = 1; i < curva.length; i++)
        {
            min = Mathf.Min(min, curva[i].value);
            max = Mathf.Max(max, curva[i].value);
        }
        return max - min > 0.0001f;
    }

    private static string Oggetto(string takeName)
    {
        int i = takeName.IndexOf('|');
        return i < 0 ? takeName : takeName.Substring(0, i);
    }

    private static string Azione(string takeName)
    {
        int i = takeName.IndexOf('|');
        return i < 0 ? takeName : takeName.Substring(i + 1);
    }

    private static bool HaSuffissoNumerico(string azione)
    {
        int i = azione.LastIndexOf('.');
        return i >= 0 && azione.Length - i == 4 && azione.Substring(i + 1).All(char.IsDigit);
    }

    // Animation legacy con le clip ritagliate + PlayAllAnimations che le fa partire tutte.
    private static void Monta(GameObject go, AnimationClip[] clips)
    {
        // Animator lasciato dall'import Generic: senza controller non fa nulla, e in più si
        // mangerebbe le curve prima che l'Animation possa applicarle.
        foreach (Animator animator in go.GetComponents<Animator>())
        {
            animator.enabled = false;
            Object.DestroyImmediate(animator, true);
        }

        Animation anim = go.GetComponent<Animation>();
        if (anim == null) anim = go.AddComponent<Animation>();

        AnimationUtility.SetAnimationClips(anim, clips);
        anim.clip = clips[0];
        anim.playAutomatically = false;   // le fa partire tutte PlayAllAnimations
        anim.wrapMode = WrapMode.Loop;
        anim.cullingType = AnimationCullingType.AlwaysAnimate;

        if (go.GetComponent<PlayAllAnimations>() == null) go.AddComponent<PlayAllAnimations>();
    }
}
