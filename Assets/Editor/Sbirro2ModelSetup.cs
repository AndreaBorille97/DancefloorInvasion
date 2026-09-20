using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Monta sbirro_2_animato.fbx dentro Sbirro2.prefab al posto del segnaposto (la capsula più la
// sfera della testa), prendendo dallo Sbirro1 quello che deve restare uguale — taglia, appoggio
// a terra, capsula di collisione — e costruendo per lo Sbirro2 un controller gemello di quello
// dello Sbirro1, ma con le SUE clip.
//
// Si passa dall'editor e non dallo YAML del prefab per lo stesso motivo di ConsoleModelSwapper:
// i fileID interni di un FBX (mesh, materiali, ossa, clip) li risolve Unity, scriverli a mano
// vuol dire indovinarli.
//
// ── Perché non si riusa direttamente Sbirro1_Controller ──────────────────────────────────────
// Sembrerebbe la scorciatoia ovvia (le mosse sono le stesse), ma non funziona, ed è stato
// provato: il modello arriva in gioco accartocciato. Le clip sono Generic, non Humanoid, quindi
// non descrivono "alza il braccio": contengono, fotogramma per fotogramma, la posizione e la
// rotazione LOCALE di ogni singolo osso. Applicate a uno scheletro con proporzioni anche solo
// leggermente diverse, quelle posizioni staccano le ossa dalla posa in cui la mesh è stata
// legata, e la pelle collassa.
//
// sbirro_2_animato.fbx si porta dietro le proprie clip delle stesse mosse
// (Armature.001|Sbirro2|camminata e |manganellata), che sono fatte apposta per il suo scheletro.
// Quindi: clip del modello, e un controller a parte che le usa con gli stessi stati, la stessa
// transizione e lo stesso parametro "Attack" di quello dello Sbirro1 (viene proprio copiato da
// lì, così restano gemelli anche in futuro).
//
// Corollario importante: l'armatura NON va rinominata. Le clip di un rig Generic si agganciano
// alle ossa per percorso ("Armature.001/Hips/..."), e i percorsi delle clip dello Sbirro2
// partono da "Armature.001" così com'è chiamata nell'FBX. Rinominarla in "Armature" per farla
// somigliare a quella dello Sbirro1 romperebbe l'aggancio di tutte le sue curve.
//
// È ripetibile: rilanciarlo rifà il montaggio da capo senza accumulare figli.
public static class Sbirro2ModelSetup
{
    private const string TemplatePrefabPath = "Assets/Prefabs/Sbirro1.prefab";
    private const string TemplateControllerPath = "Assets/Prefabs/models/Animations/Sbirro1_Controller.controller";
    private const string ControllerPath = "Assets/Prefabs/models/Animations/Sbirro2_Controller.controller";
    private const string TargetPrefabPath = "Assets/Prefabs/Sbirro2.prefab";
    private const string ModelPath = "Assets/Prefabs/models/sbirro_2_animato.fbx";
    private const string MaterialPath = "Assets/Texture/Materials/sbirro_2_texture.mat";
    private const string TexturePath = "Assets/Texture/sbirro2_texture.png";

    // Come si chiama, dentro l'FBX, il materiale del corpo: è quello che va sostituito con il
    // materiale del progetto. Gli altri (Acciaio, Nylon, Plastica_Nera, Policarbonato,
    // Manganello_Nero) sono tinte piatte dello scudo e dell'equipaggiamento e restano come sono.
    private const string BodyMaterialName = "Sbirro2";

    private const string PlaceholderChildName = "Sphere";

    // Quanto è grande lo Sbirro2 rispetto allo Sbirro1: 1 = identico, 1.1 = un 10% più alto.
    // Cresce di pari passo anche la capsula di collisione, altrimenti il modello sborderebbe dal
    // proprio ingombro fisico e gli altri gli entrerebbero dentro di quella differenza.
    private const float SizeVsSbirro1 = 1.1f;

    // Quanto tirare su il colore del corpo, che di suo viene spento. Tre leve che fanno cose
    // diverse, ed è la seconda quella che conta per la pelle:
    //  - BaseColorBoost moltiplica la texture: 1 = come importata, 1.15 = 15% più chiara;
    //  - WarmthBoost sbilancia quel moltiplicatore verso il rosso e via dal blu. Schiarire e
    //    basta non toglie l'aria cadaverica, anzi: la pelle spenta è GRIGIA, cioè ha rosso e blu
    //    troppo vicini, e schiarirla la sbianca soltanto. Allargando quella distanza il rosa
    //    torna rosa. 0 = nessuna correzione, 0.1 = il rosso sale del 10% e il blu scende del 10%;
    //  - EmissionBoost aggiunge un filo della texture come luce propria, con la stessa tinta
    //    calda: solleva la parte in ombra, che è dove i colori si spengono davvero.
    private const float BaseColorBoost = 1.15f;
    private const float WarmthBoost = 0.1f;
    private const float EmissionBoost = 0.16f;

    // Lucentezza del corpo. Quella di default (0.5) mette sulla divisa un velo di riflesso
    // grigiastro che slava i colori: un tessuto riflette poco.
    private const float BodySmoothness = 0.25f;

    // Le mosse, riconosciute dal nome della clip. Servono ad accoppiare le clip dello Sbirro2 con
    // gli stati del controller dello Sbirro1, che puntano alle clip omonime dello Sbirro1.
    private const string WalkMove = "camminata";
    private const string AttackMove = "manganellata";

    // Quello che va copiato dallo Sbirro1. Sono valori sciolti e non riferimenti perché il prefab
    // di partenza viene scaricato dalla memoria prima di toccare quello di arrivo.
    private struct Template
    {
        public float ModelWorldHeight;     // quanto è alto il modello in unità di mondo
        public float FootWorldOffset;      // quanto stanno sotto al pivot i piedi, in unità di mondo
        public float CapsuleWorldRadius;
        public float CapsuleWorldHeight;
        public Vector3 CapsuleWorldCenter;
        public int CapsuleDirection;
    }

    [MenuItem("Tools/Sbirro2/Monta il modello animato")]
    private static void Setup()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            Fail($"Modello non trovato: {ModelPath}");
            return;
        }

        Material bodyMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (bodyMaterial == null)
        {
            Fail($"Materiale non trovato: {MaterialPath}");
            return;
        }

        SetUpBodyMaterial(bodyMaterial);
        ConfigureImporter(bodyMaterial);

        AnimatorController controller = BuildController();
        if (controller == null)
        {
            return;
        }

        if (!TryReadTemplate(out Template template))
        {
            return;
        }

        GameObject target = PrefabUtility.LoadPrefabContents(TargetPrefabPath);
        try
        {
            Transform root = target.transform;
            float rootScale = root.localScale.x;

            // Via il segnaposto: la sfera della testa sparisce, e la capsula del corpo resta come
            // componente ma senza mesh, esattamente com'è messo lo Sbirro1.
            MeshFilter placeholderMesh = target.GetComponent<MeshFilter>();
            if (placeholderMesh != null)
            {
                placeholderMesh.sharedMesh = null;
            }

            foreach (Transform child in root.Cast<Transform>().ToArray())
            {
                if (child.name == PlaceholderChildName || child.name == model.name)
                {
                    Object.DestroyImmediate(child.gameObject);
                }
            }

            // Istanza nuova di zecca: i nomi degli oggetti sono quelli dell'FBX, compresa
            // l'armatura, che deve restare "Armature.001" perché le sue clip la cercano così.
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root);
            instance.name = model.name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            Vector3 baseScale = instance.transform.localScale;

            if (!TryMeasure(instance, out Bounds bounds))
            {
                Fail($"Il modello {model.name} non ha Renderer con geometria: non riesco a misurarlo.");
                return;
            }

            // Altezza dello Sbirro1 moltiplicata per SizeVsSbirro1: la scala nativa dei due FBX
            // può benissimo essere diversa, quindi non si copia un numero, si misura e si scala.
            float targetHeight = template.ModelWorldHeight * SizeVsSbirro1;
            float scaleFactor = targetHeight / bounds.size.y;
            instance.transform.localScale = baseScale * scaleFactor;

            // Piedi a terra: si rimisura dopo la scalatura e si alza il modello quel tanto che
            // porta il suo punto più basso dove sta quello dello Sbirro1.
            TryMeasure(instance, out bounds);
            float footNow = bounds.min.y - root.position.y;
            instance.transform.position += Vector3.up * (template.FootWorldOffset - footNow);

            Animator animator = instance.GetComponent<Animator>();
            if (animator == null)
            {
                animator = instance.AddComponent<Animator>();
            }
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false; // il movimento lo fa EnemyChase, non la clip

            // La capsula: si parte dalle misure dello Sbirro1 in unità di mondo (non dai suoi
            // valori locali, perché i due prefab hanno scale di radice diverse) e si applica lo
            // stesso SizeVsSbirro1 del modello, così l'ingombro fisico resta incollato a quello
            // che si vede. Non viene invece adattata all'ingombro reale del modello: lo scudo
            // antisommossa la allargherebbe di parecchio, e uno Sbirro2 che tiene tutti a
            // distanza di scudo si comporterebbe in modo diverso dagli altri.
            CapsuleCollider capsule = target.GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                capsule.radius = template.CapsuleWorldRadius * SizeVsSbirro1 / rootScale;
                capsule.height = template.CapsuleWorldHeight * SizeVsSbirro1 / rootScale;
                capsule.center = template.CapsuleWorldCenter * SizeVsSbirro1 / rootScale;
                capsule.direction = template.CapsuleDirection;
            }

            PrefabUtility.SaveAsPrefabAsset(target, TargetPrefabPath);

            Debug.Log(
                $"[Sbirro2] Modello montato. Scala {scaleFactor:0.###}, altezza {targetHeight:0.##} " +
                $"({SizeVsSbirro1:0.##}x lo Sbirro1, che sta a {template.ModelWorldHeight:0.##}), " +
                $"controller {controller.name} con le clip del modello. " +
                $"Materiali rimasti incorporati nell'FBX: {string.Join(", ", ReadMaterialNames())} " +
                $"(\"{BodyMaterialName}\" sostituito con {bodyMaterial.name}).");

            ReportBindings(instance, controller);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(target);
        }
    }

    // Il controller dello Sbirro2: copia di quello dello Sbirro1 (stessi stati, stessa
    // transizione, stesso parametro "Attack", che è quello che EnemyAttack fa scattare) con le
    // clip sostituite da quelle del modello dello Sbirro2. Se esiste già non viene ricopiato, solo
    // riagganciato alle clip: eventuali ritocchi fatti a mano restano.
    private static AnimatorController BuildController()
    {
        AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().ToArray();
        if (clips.Length == 0)
        {
            Fail($"Nessuna clip dentro {ModelPath}: controlla che nell'importer Animation sia acceso.");
            return null;
        }

        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) == null)
        {
            if (!AssetDatabase.CopyAsset(TemplateControllerPath, ControllerPath))
            {
                Fail($"Non riesco a copiare {TemplateControllerPath} in {ControllerPath}.");
                return null;
            }
        }

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Fail($"Controller non caricabile: {ControllerPath}");
            return null;
        }

        foreach (AnimatorControllerLayer layer in controller.layers)
        {
            foreach (ChildAnimatorState child in layer.stateMachine.states)
            {
                // Si capisce a quale mossa serve ogni stato da come si chiama la clip che ha
                // adesso (quella dello Sbirro1), e gli si mette la clip omonima dello Sbirro2.
                string move = MoveOf(child.state.motion != null ? child.state.motion.name : child.state.name);
                if (move == null)
                {
                    continue;
                }

                AnimationClip replacement = clips.FirstOrDefault(clip => MoveOf(clip.name) == move);
                if (replacement != null)
                {
                    child.state.motion = replacement;
                }
                else
                {
                    Debug.LogWarning($"[Sbirro2] Nessuna clip \"{move}\" nel modello: lo stato {child.state.name} resta com'era.");
                }
            }
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    // Controllo decisivo, e l'unico che dice davvero se le animazioni funzioneranno: ogni curva
    // di una clip Generic è indirizzata a un osso per PERCORSO ("Armature.001/Hips/..."). Qui si
    // prende ogni percorso e si verifica che sotto al modello ci sia davvero quell'oggetto.
    // Zero mancanti = le clip pilotano lo scheletro giusto. Tante mancanti = l'aggancio fallisce
    // (di solito perché la clip viene da un altro rig, o perché un oggetto è stato rinominato),
    // e il modello resta fermo o si accartoccia, senza un solo errore in Console.
    private static void ReportBindings(GameObject modelRoot, AnimatorController controller)
    {
        foreach (AnimatorControllerLayer layer in controller.layers)
        {
            foreach (ChildAnimatorState child in layer.stateMachine.states)
            {
                if (!(child.state.motion is AnimationClip clip))
                {
                    continue;
                }

                string[] paths = AnimationUtility.GetCurveBindings(clip)
                    .Select(binding => binding.path)
                    .Where(path => !string.IsNullOrEmpty(path))
                    .Distinct()
                    .ToArray();

                string[] missing = paths.Where(path => modelRoot.transform.Find(path) == null).ToArray();

                string detail = missing.Length == 0
                    ? "tutte agganciate"
                    : $"NON agganciate: {missing.Length} (es. {string.Join(" | ", missing.Take(3))})";

                Debug.Log($"[Sbirro2] Stato \"{child.state.name}\" -> clip \"{clip.name}\": {paths.Length} ossa animate, {detail}.");
            }
        }
    }

    private static string MoveOf(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        string lower = name.ToLowerInvariant();
        if (lower.Contains(WalkMove))
        {
            return WalkMove;
        }

        return lower.Contains(AttackMove) ? AttackMove : null;
    }

    // Le misure dello Sbirro1: serve solo che lo Sbirro2 risulti della stessa taglia e con lo
    // stesso ingombro fisico. Le animazioni no, quelle se le porta da sé (vedi BuildController).
    private static bool TryReadTemplate(out Template template)
    {
        template = default;

        GameObject source = PrefabUtility.LoadPrefabContents(TemplatePrefabPath);
        try
        {
            Animator animator = source.GetComponentInChildren<Animator>();
            if (animator == null)
            {
                Fail($"{TemplatePrefabPath} non ha un Animator nei figli: non trovo il suo modello.");
                return false;
            }

            if (!TryMeasure(animator.gameObject, out Bounds bounds))
            {
                Fail($"Non riesco a misurare il modello dentro {TemplatePrefabPath}.");
                return false;
            }

            template.ModelWorldHeight = bounds.size.y;
            template.FootWorldOffset = bounds.min.y - source.transform.position.y;

            CapsuleCollider capsule = source.GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                float scale = source.transform.localScale.x;
                template.CapsuleWorldRadius = capsule.radius * scale;
                template.CapsuleWorldHeight = capsule.height * scale;
                template.CapsuleWorldCenter = capsule.center * scale;
                template.CapsuleDirection = capsule.direction;
            }

            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(source);
        }
    }

    // Il materiale del corpo: la texture giusta e il colore tirato su.
    //
    // La texture va riassegnata perché il materiale è nato duplicando quello dello Sbirro1 e si
    // portava dietro la SUA texture: il modello nuovo usciva vestito da Sbirro1 senza che niente
    // segnalasse l'errore.
    private static void SetUpBodyMaterial(Material material)
    {
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        if (texture == null)
        {
            Debug.LogWarning($"[Sbirro2] Texture non trovata in {TexturePath}: lascio il materiale com'è.");
            return;
        }

        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture);
        }

        // URP legge _BaseMap, ma _MainTex resta in giro per compatibilità: tenerli allineati
        // evita sorprese a seconda di chi legge il materiale.
        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", texture);
        }

        // Sopra 1 il colore base non tinge, moltiplica: è una schiarita. Sbilanciato verso il
        // rosso moltiplica di più il canale rosso che il blu, e siccome la distanza fra i due è
        // quello che l'occhio legge come saturazione, la pelle si riscalda invece di sbiancarsi.
        // Va messo da codice perché il selettore di colore dell'Inspector non lascia superare 1.
        Color tint = new Color(
            BaseColorBoost * (1f + WarmthBoost),
            BaseColorBoost,
            BaseColorBoost * (1f - WarmthBoost),
            1f);

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", tint);
        }

        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", BodySmoothness);
        }

        // Un filo di emissione con la texture stessa: solleva la parte in ombra, che è dove i
        // colori si spengono. Serve anche togliere il contrassegno "emissione nera", altrimenti
        // Unity la considera spenta e non la disegna affatto.
        if (material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            if (material.HasProperty("_EmissionMap"))
            {
                material.SetTexture("_EmissionMap", texture);
            }
            // Stessa tinta calda del colore base, altrimenti la luce aggiunta in ombra sarebbe
            // neutra e rimetterebbe lì il grigio che stiamo togliendo.
            material.SetColor("_EmissionColor", tint * EmissionBoost);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
    }

    // Due cose sull'importer del modello, in un reimport solo:
    //  - il corpo usa il materiale del progetto invece di quello incorporato nell'FBX. Si passa
    //    dal remap e non da un override sul Renderer del prefab, così sopravvive a un reimport;
    //  - la camminata va in loop. Le durate non si scrivono a mano: si parte da come Unity legge
    //    le take dal file (defaultClipAnimations) e si cambia solo quel flag.
    private static void ConfigureImporter(Material bodyMaterial)
    {
        ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
        {
            return;
        }

        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), BodyMaterialName), bodyMaterial);

        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        for (int i = 0; i < clips.Length; i++)
        {
            clips[i].loopTime = MoveOf(clips[i].name) == WalkMove;
        }
        importer.clipAnimations = clips;

        importer.SaveAndReimport();
    }

    private static string[] ReadMaterialNames()
    {
        return AssetDatabase.LoadAllAssetsAtPath(ModelPath)
            .OfType<Material>()
            .Select(material => material.name)
            .OrderBy(name => name)
            .ToArray();
    }

    // Ingombro dei Renderer sotto target, saltando quelli senza geometria (un Renderer vuoto ha
    // un ingombro nullo piantato sul pivot e falserebbe la misura verso il basso).
    private static bool TryMeasure(GameObject target, out Bounds bounds)
    {
        bounds = new Bounds(target.transform.position, Vector3.zero);
        bool found = false;

        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>())
        {
            if (renderer.bounds.size.sqrMagnitude <= 0.000001f)
            {
                continue;
            }

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
                continue;
            }

            bounds.Encapsulate(renderer.bounds);
        }

        return found;
    }

    private static void Fail(string message)
    {
        EditorUtility.DisplayDialog("Sbirro2", message, "OK");
        Debug.LogError($"[Sbirro2] {message}");
    }
}
