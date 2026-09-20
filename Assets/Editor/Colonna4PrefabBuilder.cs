using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Setup del prop scenografico "Colonna4".
//
// L'FBX contiene 136 "take", ma NON sono 136 animazioni diverse: Blender ha esportato ogni
// azione su ogni oggetto, cioe' 8 oggetti x 17 azioni. I nomi sono "Oggetto|Azione", e la take
// buona per ogni oggetto e' quella la cui azione porta il suo stesso nome:
//
//     Faretto_01 -> Faretto_01Action        Occhio           -> OcchioAction.002
//     Faretto_02 -> Faretto_02Action        Tentacolo_Centro -> Sway_Tentacolo_Centro (vuota)
//     Faretto_03 -> Faretto_03Action        Tentacolo_Dx     -> Sway_Tentacolo_Dx     (vuota)
//     Faretto_04 -> Faretto_04Action        Tentacolo_Sx     -> Sway_Tentacolo_Sx     (vuota)
//
// Tutto il resto sono duplicati (le ".001"/".002" nate duplicando gli oggetti in Blender) e
// combinazioni oggetto/azione senza senso.
//
// Attenzione: ogni take contiene le curve di TUTTI gli oggetti, non solo del suo (gli altri
// fermi nella posa base). Riprodotte cosi' come sono, cinque clip che scrivono sugli stessi
// transform si spartiscono il peso e il risultato e' una media di pose - il prop si muove, ma
// sbagliato. Per questo ogni take viene RITAGLIATA: si tengono solo le curve del suo oggetto e
// si salva una clip pulita in Colonna4_Clips/. Cosi' le clip sono davvero disgiunte e possono
// girare insieme.
//
// Le clip durano tutte diversamente (650/770/890/1010/215 frame), quindi restano separate,
// ognuna in loop per conto suo: fonderle in una sola congelerebbe le piu' corte ad aspettare
// il giro della piu' lunga. Le riproduce PlayAllAnimations con il vecchio componente Animation.
// Niente Animator Controller: le animazioni non cambiano mai, non c'e' nessuno stato da gestire.
public static class Colonna4PrefabBuilder
{
    private const string FbxPath = "Assets/Prefabs/models/Colonna4.fbx";
    private const string PrefabPath = "Assets/Prefabs/Colonna4.prefab";
    private const string ClipsFolder = "Assets/Prefabs/Colonna4_Clips";

    [MenuItem("Tools/Colonna4/Ricrea il prefab")]
    private static void BuildPrefab()
    {
        AnimationClip[] clips = PrepareClips();
        if (clips == null) return;

        GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        instance.name = "Colonna4";
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        Setup(instance, clips);

        PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath, out bool ok);
        Object.DestroyImmediate(instance);

        if (!ok)
        {
            EditorUtility.DisplayDialog("Colonna4", "Salvataggio del prefab fallito.", "OK");
            return;
        }

        AssetDatabase.SaveAssets();
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
        Debug.Log($"[Colonna4] Prefab ricreato con {clips.Length} clip.");
    }

    [MenuItem("Tools/Colonna4/Sistema le istanze nella scena aperta")]
    private static void FixScene()
    {
        AnimationClip[] clips = PrepareClips();
        if (clips == null) return;

        int sistemate = 0;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!IsColonna4Root(t.gameObject)) continue;
                Setup(t.gameObject, clips);
                EditorUtility.SetDirty(t.gameObject);
                sistemate++;
            }
        }

        if (sistemate > 0) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[Colonna4] {sistemate} istanze sistemate nella scena aperta. Ricordati di salvare la scena.");
    }

    // Diagnostica: per ogni oggetto della colonna dice quali take lo fanno davvero muovere e di
    // quanto. Serve quando il movimento non torna, per capire in che take sta l'animazione buona.
    [MenuItem("Tools/Colonna4/Analizza le take dell'FBX")]
    private static void Analyze()
    {
        Dictionary<string, string> percorsi = PercorsiOggetti();
        if (percorsi == null) return;

        AnimationClip[] takes = CaricaTake();
        var righe = new List<string>();

        foreach (string oggetto in NomiOggetti(takes).OrderBy(n => n))
        {
            if (!percorsi.TryGetValue(oggetto, out string percorso))
            {
                righe.Add($"  {oggetto}: NESSUN transform con questo nome nel modello!");
                continue;
            }

            var mosse = takes
                .Select(t => new { take = t, info = Movimento(t, percorso) })
                .Where(x => x.info.curveMosse > 0)
                .OrderByDescending(x => x.info.escursione)
                .ToList();

            righe.Add($"  {oggetto}  (percorso \"{percorso}\") -> {mosse.Count} take su {takes.Length} lo muovono");
            foreach (var m in mosse.Take(5))
            {
                righe.Add($"      {m.take.name}   {m.info.curveMosse} curve, escursione max {m.info.escursione:0.###}, {m.take.length:0.00}s");
            }
        }

        Debug.Log("[Colonna4] Analisi take:\n" + string.Join("\n", righe));
    }

    // La radice di un'istanza della colonna: viene dall'FBX (o dal prefab) e sopra di lei non
    // c'e' un altro pezzo della stessa istanza, cosi' i figli del modello non vengono toccati.
    private static bool IsColonna4Root(GameObject go)
    {
        string sorgente = SorgentePrefab(go);
        if (sorgente != FbxPath && sorgente != PrefabPath) return false;
        return go.transform.parent == null || SorgentePrefab(go.transform.parent.gameObject) != sorgente;
    }

    private static string SorgentePrefab(GameObject go)
    {
        Object source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(go);
        return source == null ? null : AssetDatabase.GetAssetPath(source);
    }

    // Rig su Legacy (e' quello che il componente Animation sa riprodurre), clip scritte nella
    // root (percorsi delle curve relativi all'oggetto che porta l'Animation) e curve non
    // ricampionate: il ricampionamento converte gli Euler di Blender in quaternioni e le
    // rotazioni oltre i 180 gradi ci arrivano storte.
    private static bool SistemaImport()
    {
        var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (importer == null)
        {
            EditorUtility.DisplayDialog("Colonna4", $"FBX non trovato: {FbxPath}", "OK");
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

    // Percorso di ogni transform del modello relativo alla root, che e' il riferimento dei
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

    private static AnimationClip[] PrepareClips()
    {
        Dictionary<string, string> percorsi = PercorsiOggetti();
        if (percorsi == null) return null;

        AnimationClip[] takes = CaricaTake();
        if (takes.Length == 0)
        {
            EditorUtility.DisplayDialog("Colonna4",
                "Nessuna take trovata nell'FBX: controlla 'Import Animation' nelle impostazioni " +
                "di import di Colonna4.fbx.", "OK");
            return null;
        }

        if (AssetDatabase.IsValidFolder(ClipsFolder)) AssetDatabase.DeleteAsset(ClipsFolder);
        AssetDatabase.CreateFolder("Assets/Prefabs", "Colonna4_Clips");

        var risultato = new List<AnimationClip>();
        var report = new List<string>();

        foreach (string oggetto in NomiOggetti(takes).OrderBy(n => n))
        {
            AnimationClip take = TakeDellOggetto(takes, oggetto);
            if (take == null)
            {
                report.Add($"  {oggetto}: nessuna take con l'azione omonima, saltato.");
                continue;
            }

            if (!percorsi.TryGetValue(oggetto, out string percorso))
            {
                report.Add($"  {oggetto}: nel modello non c'e' nessun transform con questo nome, saltato.");
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

        Debug.Log($"[Colonna4] {risultato.Count} clip pronte in {ClipsFolder}:\n" + string.Join("\n", report));

        if (risultato.Count == 0)
        {
            EditorUtility.DisplayDialog("Colonna4", "Nessuna clip utilizzabile. Guarda la Console.", "OK");
            return null;
        }
        return risultato.ToArray();
    }

    // Gli oggetti animati sono i prefissi delle take: "Faretto_01|Faretto_01Action" -> "Faretto_01".
    private static IEnumerable<string> NomiOggetti(AnimationClip[] takes)
    {
        return takes.Select(t => Oggetto(t.name)).Distinct();
    }

    // Fra le take di un oggetto tiene quella la cui azione porta il suo nome, scartando i
    // doppioni ".001"/".002".
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

    private struct InfoMovimento
    {
        public int curveMosse;
        public float escursione;
    }

    private static InfoMovimento Movimento(AnimationClip take, string percorso)
    {
        var info = new InfoMovimento();
        foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(take))
        {
            if (!Appartiene(b.path, percorso)) continue;
            AnimationCurve curva = AnimationUtility.GetEditorCurve(take, b);
            if (curva == null || curva.length < 2) continue;
            float min = curva[0].value, max = min;
            for (int i = 1; i < curva.length; i++)
            {
                min = Mathf.Min(min, curva[i].value);
                max = Mathf.Max(max, curva[i].value);
            }
            if (max - min <= 0.0001f) continue;
            info.curveMosse++;
            info.escursione = Mathf.Max(info.escursione, max - min);
        }
        return info;
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

    private static void Setup(GameObject go, AnimationClip[] clips)
    {
        // Animator rimasto dai tentativi precedenti: senza controller non fa nulla, e in piu'
        // si mangerebbe le curve prima che l'Animation possa applicarle.
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
