using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// Monta CyberGiorgia, il boss finale, da models/CYBERGIORGIA/cybergiorgia.fbx:
//  - materiale giorgia_mat con la texture di Meshy, messo su tutta la mesh;
//  - importer: Idle e Running in loop, FreeFall e Shoot no; la Running va alla velocità che
//    tiene i piedi incollati a terra (MatchRunSpeed), la FreeFall atterra insieme al boss e si
//    rialza rallentata (ShapeFall);
//  - CyberGiorgia_Controller: FreeFall all'inizio, Idle quando atterra (parametro "Landed"),
//    Idle/Running col bool "Moving" e Shoot col trigger "Attack" (stato col tag "Attack"): sono
//    i parametri che usa EnemyRangedAttack, gli stessi di Sbirro3New_Controller;
//  - la granata: SbirroSfera e SbirroNuvola copiate in CyberGiorgiaSfera/CyberGiorgiaNuvola, con
//    la nuvola verde e col raggio GrenadeRadiusVsSbirro3 volte quello dello Sbirro3; le granate
//    partono dalla bocca del cannone (vedi PlaceFirePoint);
//  - CyberGiorgia.prefab: radice ai piedi con capsula, Rigidbody, EnemyHealth, EnemyRangedAttack
//    (spento: lo accende CyberGiorgiaBoss quando è a terra) e CyberGiorgiaBoss;
//  - nella scena aperta (quella con FloorBounds, cioè GameplayV2) un CyberGiorgiaSpawner al
//    centro del pavimento, se non c'è già. La scena va salvata a mano.
//
// È ripetibile, ma rifà il prefab e il controller da capo: i valori ritoccati a mano
// nell'Inspector del prefab tornano a quelli scritti qui sotto.
public static class CyberGiorgiaSetup
{
    private const string Folder = "Assets/Prefabs/models/CYBERGIORGIA";
    private const string ModelPath = Folder + "/cybergiorgia.fbx";
    private const string MaterialPath = Folder + "/giorgia_mat.mat";
    private const string TexturePath = Folder + "/Meshy_AI_cyber_businesswoman_v_biped_texture_0.png";
    private const string ControllerPath = Folder + "/CyberGiorgia_Controller.controller";

    private const string BossPrefabPath = "Assets/Prefabs/CyberGiorgia.prefab";
    private const string SizeTemplatePath = "Assets/Prefabs/Sbirro1.prefab";
    private const string SourceGrenadePath = "Assets/Prefabs/SbirroSfera.prefab";
    private const string SourceCloudPath = "Assets/Prefabs/SbirroNuvola.prefab";
    private const string GrenadePath = "Assets/Prefabs/CyberGiorgiaSfera.prefab";
    private const string CloudPath = "Assets/Prefabs/CyberGiorgiaNuvola.prefab";
    private const string LandingClipPath = "Assets/Audio/GIORGIA.mp3"; // suonato quando atterra

    // Le clip dentro l'FBX si chiamano "SmartRigArmature|<mossa>".
    private const string FreeFallClip = "FreeFall";
    private const string IdleClip = "Idle";
    private const string RunClip = "Running";
    private const string ShootClip = "Shoot";

    // Quanto è alta rispetto allo Sbirro1.
    private const float SizeVsSbirro1 = 1.4f;
    // Quanto è più estesa la nuvola della granata rispetto a quella dello Sbirro3 (raggio).
    // √2: il doppio dell'area della nuvola dello Sbirro3 (prima era 2, il doppio del raggio, cioè
    // quattro volte l'area, ed era troppo).
    private const float GrenadeRadiusVsSbirro3 = 1.41421356f;
    // Il fumo del boss: verde, con la stessa trasparenza di quello dello Sbirro3. Non si vede
    // (GrenadeShowSmoke): resta il colore nel caso lo si riaccenda.
    private static readonly Color GrenadeCloudColor = new Color(0.35f, 1f, 0.3f, 0.35f);
    // Niente fumo: la granata si vede col fungo atomico, della nuvola resta solo l'area del danno.
    private const bool GrenadeShowSmoke = false;
    // Velocità della granata rispetto a quella dello Sbirro3: più lenta, così si fa in tempo a
    // schivarla.
    private const float GrenadeSpeedVsSbirro3 = 0.75f;
    // All'impatto la granata fa saltare un fungo atomico (MiniNuke3D) di questa grandezza.
    private const float GrenadeExplosionScale = 1f;
    private const string ExplosionShaderPath = "Assets/Prefabs/particles/MiniNuke3D/MiniNukePuff.shader";

    // EnemyHealth / EnemyRangedAttack del boss.
    private const float MaxHealth = 720f;
    private const float MoveSpeed = 4f;
    private const float StopDistance = 12f;
    private const float FireRate = 0.5f;
    private const float FireAnimationMoment = 0.4f;

    // Dopo quanti secondi arriva (CyberGiorgiaSpawner.spawnDelay): 3 minuti, uno dopo gli
    // IdroSbirro. Per le prove 0 = subito.
    private const float SpawnDelay = 180f;

    [MenuItem("Tools/CyberGiorgia/Monta il boss")]
    private static void Setup()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
        {
            Fail($"Modello non trovato: {ModelPath}");
            return;
        }

        Material material = SetUpMaterial();
        ConfigureImporter();

        // Ricaricato DOPO ConfigureImporter: la reimportazione rifà il modello, e quello caricato
        // prima è la versione vecchia. Era successo: il modello vecchio non aveva l'Animator, il
        // tool ne aggiungeva uno col controller, e in gioco c'era anche quello dell'FBX nuovo,
        // vuoto, che era il primo a essere trovato: CyberGiorgia restava ferma nella posa di base.
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

        AnimatorController controller = BuildController();
        if (controller == null)
        {
            return;
        }

        GameObject grenade = BuildGrenade();
        if (grenade == null)
        {
            return;
        }

        if (!TryMeasureTemplateHeight(out float sbirro1Height))
        {
            return;
        }

        GameObject bossPrefab = BuildBossPrefab(model, material, controller, grenade, sbirro1Height * SizeVsSbirro1);
        if (bossPrefab == null)
        {
            return;
        }

        PlaceSpawner(bossPrefab);
    }

    private static Material SetUpMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        if (material == null || texture == null)
        {
            Debug.LogWarning($"[CyberGiorgia] Materiale ({MaterialPath}) o texture ({TexturePath}) mancanti: resta il materiale dell'FBX.");
            return material;
        }

        // URP legge _BaseMap, _MainTex resta allineato per chi legge quello.
        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture);
        }
        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", texture);
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    private static void ConfigureImporter()
    {
        ModelImporter importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        ModelImporterClipAnimation[] clips = importer.clipAnimations.Length > 0
            ? importer.clipAnimations
            : importer.defaultClipAnimations;

        bool changed = importer.clipAnimations.Length == 0;
        foreach (ModelImporterClipAnimation clip in clips)
        {
            // La FreeFall no: dentro c'è anche l'atterraggio e il rialzarsi, va fino in fondo una volta.
            bool loop = MoveOf(clip.name) == IdleClip || MoveOf(clip.name) == RunClip;
            if (clip.loopTime != loop)
            {
                clip.loopTime = loop;
                changed = true;
            }
        }

        if (changed)
        {
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }
    }

    private static AnimatorController BuildController()
    {
        AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
            .OfType<AnimationClip>()
            .Where(clip => !clip.name.StartsWith("__preview__"))
            .ToArray();

        AnimationClip fall = FindClip(clips, FreeFallClip);
        AnimationClip idle = FindClip(clips, IdleClip);
        AnimationClip run = FindClip(clips, RunClip);
        AnimationClip shoot = FindClip(clips, ShootClip);
        if (fall == null || idle == null || run == null || shoot == null)
        {
            Fail($"Mancano delle clip in {ModelPath} (trovate: {string.Join(", ", clips.Select(c => c.name))}).");
            return null;
        }

        // Dove stanno le anche al primo fotogramma dell'Idle: è lì che si fermano gli assi che una
        // clip sposta troppo (vedi MakeInPlace), così ogni clip parte dalla stessa posa a terra.
        string hipsPath = FindHipsPath(clips);
        var restPose = new System.Collections.Generic.Dictionary<string, float>();
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(idle))
        {
            AnimationCurve curve = AnimationUtility.GetEditorCurve(idle, binding);
            if (binding.path == hipsPath && curve != null && curve.length > 0)
            {
                restPose[binding.propertyName] = curve.keys[0].value;
            }
        }

        AnimationClip originalFall = fall;
        fall = MakeInPlace(fall, hipsPath, restPose);
        ShapeFall(originalFall, fall, hipsPath, restPose);
        idle = MakeInPlace(idle, hipsPath, restPose);
        run = MakeInPlace(run, hipsPath, restPose);
        shoot = MakeInPlace(shoot, hipsPath, restPose);
        IdleClipInPlace = idle;
        ShootClipInPlace = shoot;
        RunClipInPlace = run;

        // Se c'è già si svuota e si riempie di nuovo, senza cancellarlo: cancellato e ricreato,
        // il prefab caricato in memoria restava agganciato al controller vecchio, distrutto, e in
        // gioco l'Animator risultava senza controller (CyberGiorgia ferma nella posa di base).
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        }

        while (controller.parameters.Length > 0)
        {
            controller.RemoveParameter(0);
        }

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        foreach (ChildAnimatorState child in machine.states)
        {
            machine.RemoveState(child.state);
        }
        foreach (AnimatorStateTransition transition in machine.anyStateTransitions)
        {
            machine.RemoveAnyStateTransition(transition);
        }

        controller.AddParameter("Moving", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Landed", AnimatorControllerParameterType.Bool);
        // Velocità dello stato FreeFall, messa da CyberGiorgiaBoss: in aria quanto basta perché
        // nella clip tocchi terra quando tocca terra davvero, poi più lenta mentre si rialza.
        controller.AddParameter(new AnimatorControllerParameter
        {
            name = "FallSpeed",
            type = AnimatorControllerParameterType.Float,
            defaultFloat = 1f,
        });

        AnimatorState fallState = AddState(machine, "FreeFall", fall);
        fallState.speedParameter = "FallSpeed";
        fallState.speedParameterActive = true;
        AnimatorState idleState = AddState(machine, "Idle", idle);
        AnimatorState runState = AddState(machine, "Running", run);
        RunStateRef = runState;
        AnimatorState shootState = AddState(machine, "Shoot", shoot);
        shootState.tag = "Attack"; // EnemyRangedAttack aspetta il colpo nello stato con questo tag
        machine.defaultState = fallState;

        // Solo dopo che si è rialzata: a terra e a fine clip.
        AnimatorStateTransition landed = AddTransition(fallState, idleState, 0.15f);
        landed.AddCondition(AnimatorConditionMode.If, 0f, "Landed");
        landed.hasExitTime = true;
        landed.exitTime = 0.97f;
        AddTransition(idleState, runState, 0.15f).AddCondition(AnimatorConditionMode.If, 0f, "Moving");
        AddTransition(runState, idleState, 0.15f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Moving");
        AddTransition(idleState, shootState, 0.1f).AddCondition(AnimatorConditionMode.If, 0f, "Attack");
        AddTransition(runState, shootState, 0.1f).AddCondition(AnimatorConditionMode.If, 0f, "Attack");

        AnimatorStateTransition shootDone = AddTransition(shootState, idleState, 0.1f);
        shootDone.hasExitTime = true;
        shootDone.exitTime = 0.9f;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    // L'osso delle anche: il più in alto, fra quelli animati, che si chiama "...Hips".
    private static string FindHipsPath(AnimationClip[] clips)
    {
        return clips
            .SelectMany(clip => AnimationUtility.GetCurveBindings(clip))
            .Select(binding => binding.path)
            .Where(path => path.Split('/').Last().EndsWith("Hips"))
            .OrderBy(path => path.Split('/').Length)
            .FirstOrDefault();
    }

    // Le clip dell'FBX spostano le anche anche di metri (la caduta libera scende, la corsa va
    // avanti): con la radice ferma a terra la si vedeva sprofondare sotto la sabbia. Il movimento
    // vero lo fanno CyberGiorgiaBoss ed EnemyRangedAttack, quindi qui se ne fa una copia .anim in
    // cui, solo sulle anche, gli assi che si allontanano dal punto di partenza più di
    // MaxHipsDrift volte l'altezza delle anche restano fermi dove stanno nell'Idle (restPose).
    // La FreeFall parte con le anche quasi 4 volte più in alto dell'Idle: fermata sul suo primo
    // fotogramma, cadeva da 5 metri sopra la radice e all'atterraggio "saltava" giù. Le
    // oscillazioni piccole (il molleggio della corsa, il respiro dell'idle) restano.
    //
    // La copia si aggiorna sul posto se c'è già, per lo stesso motivo del controller.
    private const string ClipsFolder = Folder + "/Clips";
    private const float MaxHipsDrift = 0.25f;

    private static AnimationClip MakeInPlace(AnimationClip source, string hipsPath, System.Collections.Generic.Dictionary<string, float> restPose)
    {
        if (!AssetDatabase.IsValidFolder(ClipsFolder))
        {
            AssetDatabase.CreateFolder(Folder, "Clips");
        }

        string move = MoveOf(source.name);
        string path = $"{ClipsFolder}/{move}.anim";
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, path);
        }

        EditorUtility.CopySerialized(source, clip); // curve e impostazioni (loop compreso)
        clip.name = move;

        if (string.IsNullOrEmpty(hipsPath))
        {
            Debug.LogWarning("[CyberGiorgia] Nessun osso \"Hips\" animato: clip copiate così come sono.");
            EditorUtility.SetDirty(clip);
            return clip;
        }

        EditorCurveBinding[] hips = AnimationUtility.GetCurveBindings(clip)
            .Where(binding => binding.path == hipsPath && binding.propertyName.StartsWith("m_LocalPosition."))
            .ToArray();

        // Riferimento per "troppo": quanto stanno le anche dal loro genitore a inizio clip, nelle
        // unità della clip. Tutti e tre gli assi e non solo la Y: nei rig da Blender "su" può
        // essere la Z locale.
        float hipsHeight = Mathf.Sqrt(hips
            .Select(binding => AnimationUtility.GetEditorCurve(clip, binding))
            .Where(curve => curve != null && curve.length > 0)
            .Sum(curve => curve.keys[0].value * curve.keys[0].value));

        var report = new System.Collections.Generic.List<string>();
        foreach (EditorCurveBinding binding in hips)
        {
            AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null || curve.length < 2)
            {
                continue;
            }

            float start = curve.keys[0].value;
            float drift = curve.keys.Max(key => Mathf.Abs(key.value - start));
            bool flatten = hipsHeight > 0f && drift > hipsHeight * MaxHipsDrift;
            string axis = binding.propertyName.Substring(binding.propertyName.Length - 1);
            report.Add($"{axis} {drift:0.###}{(flatten ? " (fermato)" : "")}");

            if (flatten)
            {
                float end = curve.keys[curve.length - 1].time;
                float rest = restPose.TryGetValue(binding.propertyName, out float idleValue) ? idleValue : start;
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(curve.keys[0].time, end, rest));
            }
        }

        Debug.Log($"[CyberGiorgia] Clip {move}: spostamento anche (altezza {hipsHeight:0.###}): {string.Join(", ", report)}.");
        EditorUtility.SetDirty(clip);
        return clip;
    }

    // La FreeFall contiene la caduta, l'atterraggio (le anche al punto più basso) e il rialzarsi.
    // MakeInPlace le ha fermato le anche all'altezza dell'Idle, perché in aria scendevano di metri;
    // qui la Y delle anche si rifà così: ferma all'altezza dell'atterraggio fino a quel momento
    // (la caduta la fa CyberGiorgiaBoss), poi la curva vera del rialzarsi, spostata in modo che
    // finisca all'altezza dell'Idle. FallLandTime e FallLength vanno su CyberGiorgiaBoss, che
    // sincronizza l'atterraggio della clip con quello vero e rallenta il rialzarsi.
    private static float FallLandTime = 1f;
    private static float FallLength = 1f;

    private static void ShapeFall(AnimationClip original, AnimationClip clip, string hipsPath, System.Collections.Generic.Dictionary<string, float> restPose)
    {
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        FallLength = clip.length;
        FallLandTime = FallLength;

        EditorCurveBinding binding = AnimationUtility.GetCurveBindings(original)
            .FirstOrDefault(b => b.path == hipsPath && b.propertyName == "m_LocalPosition.y");
        AnimationCurve source = binding.path != null ? AnimationUtility.GetEditorCurve(original, binding) : null;
        if (source == null || source.length < 2 || !restPose.TryGetValue("m_LocalPosition.y", out float restY))
        {
            Debug.LogWarning("[CyberGiorgia] FreeFall: niente curva delle anche, non so dove atterra: resta tutta in aria.");
            return;
        }

        float start = source.keys[0].time;
        float end = source.keys[source.length - 1].time;
        float landTime = start;
        const int Samples = 200;
        for (int i = 0; i <= Samples; i++)
        {
            float time = Mathf.Lerp(start, end, i / (float)Samples);
            if (source.Evaluate(time) < source.Evaluate(landTime))
            {
                landTime = time;
            }
        }

        if (end - landTime < 0.05f)
        {
            Debug.Log("[CyberGiorgia] FreeFall: le anche scendono fino in fondo, nessun rialzarsi da rallentare.");
            return;
        }

        float offset = restY - source.Evaluate(end);
        float landY = source.Evaluate(landTime) + offset;
        var keys = new System.Collections.Generic.List<Keyframe>
        {
            new Keyframe(start, landY),
            new Keyframe(landTime, landY),
        };
        foreach (Keyframe key in source.keys)
        {
            if (key.time > landTime)
            {
                Keyframe shifted = key;
                shifted.value += offset;
                keys.Add(shifted);
            }
        }
        AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(keys.ToArray()));

        FallLandTime = landTime - start;
        EditorUtility.SetDirty(clip);
        Debug.Log($"[CyberGiorgia] FreeFall: tocca terra a {FallLandTime:0.00} s su {FallLength:0.00}, si rialza in {FallLength - FallLandTime:0.00} s (prima di rallentarlo).");
    }

    private static AnimatorState AddState(AnimatorStateMachine machine, string name, AnimationClip clip)
    {
        AnimatorState state = machine.AddState(name);
        state.motion = clip;
        return state;
    }

    private static AnimatorStateTransition AddTransition(AnimatorState from, AnimatorState to, float duration)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.duration = duration;
        return transition;
    }

    private static AnimationClip FindClip(AnimationClip[] clips, string move)
    {
        return clips.FirstOrDefault(clip => MoveOf(clip.name) == move);
    }

    // "SmartRigArmature|Running" -> "Running". Confronto esatto, così Running_BACKUP non conta.
    private static string MoveOf(string clipName)
    {
        int bar = clipName.LastIndexOf('|');
        return bar >= 0 ? clipName.Substring(bar + 1) : clipName;
    }

    // Copie della sfera e della nuvola dello Sbirro3: la sfera nuova lascia la nuvola nuova, con
    // il raggio moltiplicato. Da SbirroCloud.radius dipendono sia i puffi sia il trigger del danno.
    private static GameObject BuildGrenade()
    {
        if (!CopyPrefab(SourceCloudPath, CloudPath) || !CopyPrefab(SourceGrenadePath, GrenadePath))
        {
            return null;
        }

        GameObject sourceCloud = AssetDatabase.LoadAssetAtPath<GameObject>(SourceCloudPath);
        float sourceRadius = new SerializedObject(sourceCloud.GetComponent<SbirroCloud>()).FindProperty("radius").floatValue;

        GameObject cloud = PrefabUtility.LoadPrefabContents(CloudPath);
        try
        {
            cloud.name = "CyberGiorgiaNuvola";
            SerializedObject cloudSettings = new SerializedObject(cloud.GetComponent<SbirroCloud>());
            cloudSettings.FindProperty("radius").floatValue = sourceRadius * GrenadeRadiusVsSbirro3;
            cloudSettings.FindProperty("cloudColor").colorValue = GrenadeCloudColor;
            cloudSettings.FindProperty("showSmoke").boolValue = GrenadeShowSmoke;
            cloudSettings.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(cloud, CloudPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(cloud);
        }

        GameObject grenade = PrefabUtility.LoadPrefabContents(GrenadePath);
        try
        {
            grenade.name = "CyberGiorgiaSfera";
            SerializedObject grenadeSettings = new SerializedObject(grenade.GetComponent<ParabolicProjectile>());
            grenadeSettings.FindProperty("cloudPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(CloudPath);
            GameObject sourceGrenade = AssetDatabase.LoadAssetAtPath<GameObject>(SourceGrenadePath);
            float sourceSpeed = new SerializedObject(sourceGrenade.GetComponent<ParabolicProjectile>()).FindProperty("speed").floatValue;
            grenadeSettings.FindProperty("speed").floatValue = sourceSpeed * GrenadeSpeedVsSbirro3;
            grenadeSettings.FindProperty("explosionScale").floatValue = GrenadeExplosionScale;
            grenadeSettings.FindProperty("explosionShader").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>(ExplosionShaderPath);
            grenadeSettings.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(grenade, GrenadePath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(grenade);
        }

        Debug.Log($"[CyberGiorgia] Granata: nuvola di raggio {sourceRadius * GrenadeRadiusVsSbirro3:0.##} (Sbirro3: {sourceRadius:0.##}).");
        return AssetDatabase.LoadAssetAtPath<GameObject>(GrenadePath);
    }

    // Copia solo se manca: rilanciando il tool la copia resta la stessa (stesso GUID).
    private static bool CopyPrefab(string source, string destination)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(destination) != null)
        {
            return true;
        }

        if (!AssetDatabase.CopyAsset(source, destination))
        {
            Fail($"Non riesco a copiare {source} in {destination}.");
            return false;
        }
        return true;
    }

    private static GameObject BuildBossPrefab(GameObject model, Material material, AnimatorController controller, GameObject grenade, float targetHeight)
    {
        GameObject root = new GameObject("CyberGiorgia");
        try
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            instance.name = model.name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            // Misurata nella posa in cui sta a terra (primo fotogramma dell'Idle) e sulla mesh
            // deformata vera: l'ingombro fisso dell'FBX sta su un'altra posa, 5 metri più su dello
            // scheletro, e a misurare quello la si metteva sotto la sabbia.
            if (IdleClipInPlace != null)
            {
                IdleClipInPlace.SampleAnimation(instance, 0f);
            }

            if (!TryMeasurePosed(instance, out Bounds bounds))
            {
                Fail($"Il modello {model.name} non ha mesh da misurare.");
                return null;
            }

            // Alta targetHeight, con la pianta dei piedi sulla radice: la radice sta sul pavimento.
            instance.transform.localScale *= targetHeight / bounds.size.y;
            TryMeasurePosed(instance, out bounds);
            instance.transform.position += Vector3.up * (root.transform.position.y - bounds.min.y);
            TryMeasurePosed(instance, out bounds);

            if (material != null)
            {
                foreach (Renderer r in instance.GetComponentsInChildren<Renderer>())
                {
                    r.sharedMaterials = Enumerable.Repeat(material, r.sharedMaterials.Length).ToArray();
                }
            }

            // Uno solo, e col controller: chi cerca l'Animator (EnemyRangedAttack, CyberGiorgiaBoss)
            // prende il primo che trova.
            Animator[] animators = instance.GetComponentsInChildren<Animator>(true);
            Animator animator = animators.Length > 0 ? animators[0] : instance.AddComponent<Animator>();
            for (int i = 1; i < animators.Length; i++)
            {
                animators[i].enabled = false;
                Debug.LogWarning($"[CyberGiorgia] Animator in più su {animators[i].name}: spento.");
            }
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false; // il movimento lo fa EnemyRangedAttack
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // Capsula poco sopra il pavimento, così non ci striscia contro.
            CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
            float height = bounds.size.y;
            capsule.direction = 1;
            capsule.height = height;
            capsule.radius = Mathf.Max(0.35f, Mathf.Min(bounds.extents.x, bounds.extents.z, height * 0.18f));
            capsule.center = new Vector3(0f, height * 0.5f + 0.05f, 0f);

            root.AddComponent<Rigidbody>();

            EnemyHealth health = root.AddComponent<EnemyHealth>();
            SetValues(health, so => so.FindProperty("maxHealth").floatValue = MaxHealth);

            // Le granate partono da qui: all'altezza delle mani, un passo davanti.
            MatchRunSpeed(instance, root.transform, height, controller);
            Transform firePoint = PlaceFirePoint(instance, root.transform, height); // rimette anche la posa Idle

            EnemyRangedAttack ranged = root.AddComponent<EnemyRangedAttack>();
            SetValues(ranged, so =>
            {
                so.FindProperty("moveSpeed").floatValue = MoveSpeed;
                so.FindProperty("animationSpeedMultiplier").floatValue = 1f;
                so.FindProperty("alwaysTargetPlayer").boolValue = true;
                so.FindProperty("stopDistance").floatValue = StopDistance;
                so.FindProperty("retreatDistance").floatValue = 0f; // non scappa mai
                so.FindProperty("raverFleeRadius").floatValue = 0f;
                so.FindProperty("raverFleeCountThreshold").intValue = 0;
                so.FindProperty("projectilePrefab").objectReferenceValue = grenade;
                so.FindProperty("fireRate").floatValue = FireRate;
                so.FindProperty("firePoint").objectReferenceValue = firePoint;
                so.FindProperty("fireAnimationMoment").floatValue = FireAnimationMoment;
            });
            ranged.enabled = false; // lo accende CyberGiorgiaBoss quando è a terra

            CyberGiorgiaBoss boss = root.AddComponent<CyberGiorgiaBoss>();
            SetValues(boss, so =>
            {
                so.FindProperty("freeFallLandTime").floatValue = FallLandTime;
                so.FindProperty("freeFallLength").floatValue = FallLength;
                so.FindProperty("landingClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(LandingClipPath);
            });

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, BossPrefabPath);
            Debug.Log($"[CyberGiorgia] Prefab {BossPrefabPath}: alta {height:0.##} ({SizeVsSbirro1:0.##}x lo Sbirro1), " +
                      $"capsula raggio {capsule.radius:0.##}, controller {controller.name}.");
            ReportBindings(instance, controller);
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void SetValues(Object component, System.Action<SerializedObject> edit)
    {
        SerializedObject so = new SerializedObject(component);
        edit(so);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void PlaceSpawner(GameObject bossPrefab)
    {
        FloorBounds floor = Object.FindAnyObjectByType<FloorBounds>();
        if (floor == null)
        {
            Debug.LogWarning("[CyberGiorgia] Nella scena aperta non c'è FloorBounds: apri GameplayV2 e rilancia il tool per mettere lo spawner al centro della mappa.");
            return;
        }

        CyberGiorgiaSpawner spawner = Object.FindAnyObjectByType<CyberGiorgiaSpawner>();
        if (spawner == null)
        {
            Renderer floorRenderer = floor.GetComponentInChildren<Renderer>();
            Collider floorCollider = floor.GetComponentInChildren<Collider>();
            Bounds floorBounds = floorRenderer != null ? floorRenderer.bounds
                : floorCollider != null ? floorCollider.bounds
                : new Bounds(floor.transform.position, Vector3.zero);

            GameObject spawnerObject = new GameObject("CyberGiorgiaSpawner");
            spawnerObject.transform.position = new Vector3(floorBounds.center.x, floorBounds.max.y, floorBounds.center.z);
            spawner = spawnerObject.AddComponent<CyberGiorgiaSpawner>();
            Undo.RegisterCreatedObjectUndo(spawnerObject, "CyberGiorgia spawner");
        }

        SetValues(spawner, so =>
        {
            so.FindProperty("bossPrefab").objectReferenceValue = bossPrefab;
            so.FindProperty("spawnDelay").floatValue = SpawnDelay;
        });
        EditorSceneManager.MarkSceneDirty(spawner.gameObject.scene);
        Selection.activeGameObject = spawner.gameObject;
        Debug.Log($"[CyberGiorgia] Spawner in scena a {spawner.transform.position}: salva la scena.");
    }

    private static bool TryMeasureTemplateHeight(out float height)
    {
        height = 0f;
        GameObject source = PrefabUtility.LoadPrefabContents(SizeTemplatePath);
        try
        {
            Animator animator = source.GetComponentInChildren<Animator>();
            if (animator == null || !TryMeasure(animator.gameObject, out Bounds bounds))
            {
                Fail($"Non riesco a misurare il modello dentro {SizeTemplatePath}.");
                return false;
            }

            height = bounds.size.y;
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(source);
        }
    }

    // Le clip in place, preparate da BuildController: servono a BuildBossPrefab per misurare la
    // posa a terra (Idle) e trovare la bocca del cannone nel momento dello sparo (Shoot).
    private static AnimationClip IdleClipInPlace;
    private static AnimationClip ShootClipInPlace;
    private static AnimationClip RunClipInPlace;
    private static AnimatorState RunStateRef;

    // La corsa è sul posto (vedi MakeInPlace) e il movimento lo fa EnemyRangedAttack a MoveSpeed:
    // se la clip va più veloce, i piedi scivolano. Un piede che poggia a terra, in una corsa sul
    // posto, scorre all'indietro rispetto al corpo esattamente alla velocità a cui il corpo
    // dovrebbe avanzare: la si misura nei fotogrammi in cui un piede sta al suo punto più basso, e
    // lo stato Running va alla velocità che la porta a MoveSpeed.
    private static void MatchRunSpeed(GameObject instance, Transform root, float height, AnimatorController controller)
    {
        if (RunClipInPlace == null || RunStateRef == null)
        {
            return;
        }

        Transform[] feet = { FindBone(instance.transform, "LeftFoot"), FindBone(instance.transform, "RightFoot") };
        if (feet[0] == null || feet[1] == null)
        {
            Debug.LogWarning("[CyberGiorgia] Piedi non trovati nello scheletro: la corsa resta a velocità 1.");
            return;
        }

        const int Samples = 120;
        float length = RunClipInPlace.length;
        var positions = new Vector3[2, Samples + 1];
        for (int i = 0; i <= Samples; i++)
        {
            RunClipInPlace.SampleAnimation(instance, length * i / Samples);
            for (int f = 0; f < 2; f++)
            {
                positions[f, i] = root.InverseTransformPoint(feet[f].position);
            }
        }

        // A terra: entro il 3% dell'altezza del boss dal punto più basso di quel piede.
        float contactBand = height * 0.03f;
        float dt = length / Samples;
        float speedSum = 0f;
        int speedCount = 0;
        for (int f = 0; f < 2; f++)
        {
            float lowest = float.MaxValue;
            for (int i = 0; i <= Samples; i++)
            {
                lowest = Mathf.Min(lowest, positions[f, i].y);
            }

            for (int i = 0; i < Samples; i++)
            {
                if (positions[f, i].y > lowest + contactBand || positions[f, i + 1].y > lowest + contactBand)
                {
                    continue;
                }

                Vector3 step = positions[f, i + 1] - positions[f, i];
                step.y = 0f;
                speedSum += step.magnitude / dt;
                speedCount++;
            }
        }

        if (speedCount == 0 || speedSum <= 0f)
        {
            Debug.LogWarning("[CyberGiorgia] Corsa: nessun fotogramma con un piede a terra, resta a velocità 1.");
            return;
        }

        float clipGroundSpeed = speedSum / speedCount;
        RunStateRef.speed = Mathf.Clamp(MoveSpeed / clipGroundSpeed, 0.2f, 3f);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log($"[CyberGiorgia] Corsa: a velocità 1 i piedi a terra scorrono a {clipGroundSpeed:0.00} m/s, " +
                  $"lei si muove a {MoveSpeed:0.##} m/s: stato Running a velocità {RunStateRef.speed:0.00}.");
    }

    // Il punto da cui partono le granate: la bocca del cannone, figlio dell'osso della mano che lo
    // regge, così segue l'animazione di sparo. Il cannone si trova nella posa del colpo (Shoot al
    // FireAnimationMoment): per ogni mano si prende il prolungamento dell'avambraccio, oltre la
    // mano, e il vertice della mesh che ci arriva più lontano entro MuzzleSearchRadius dall'asse è
    // la bocca. Vince la mano che arriva più lontano: quella nuda finisce con le dita, quella col
    // cannone molto dopo. Senza mani riconoscibili, un punto fisso davanti al petto.
    private const float MuzzleSearchRadius = 0.08f; // in frazioni dell'altezza del boss

    private static Transform PlaceFirePoint(GameObject instance, Transform root, float height)
    {
        Transform firePoint = new GameObject("FirePoint").transform;
        firePoint.SetParent(root, false);
        firePoint.localPosition = new Vector3(0f, height * 0.6f, 0.7f);

        if (ShootClipInPlace == null)
        {
            return firePoint;
        }

        ShootClipInPlace.SampleAnimation(instance, ShootClipInPlace.length * FireAnimationMoment);
        Vector3[] vertices = PosedVertices(instance);

        Transform bestHand = null;
        Vector3 bestMuzzle = Vector3.zero;
        float bestReach = 0f;
        var report = new System.Collections.Generic.List<string>();
        foreach (string side in new[] { "Right", "Left" })
        {
            Transform hand = FindBone(instance.transform, side + "Hand");
            Transform forearm = FindBone(instance.transform, side + "ForeArm");
            if (hand == null || forearm == null)
            {
                continue;
            }

            Vector3 axis = (hand.position - forearm.position).normalized;
            float maxDistance = MuzzleSearchRadius * height;
            float reach = 0f;
            foreach (Vector3 vertex in vertices)
            {
                Vector3 offset = vertex - hand.position;
                float along = Vector3.Dot(offset, axis);
                if (along > reach && (offset - axis * along).magnitude <= maxDistance)
                {
                    reach = along;
                }
            }

            report.Add($"{side} {reach:0.00} m");
            if (reach > bestReach)
            {
                bestReach = reach;
                bestHand = hand;
                bestMuzzle = hand.position + axis * reach;
            }
        }

        if (bestHand != null)
        {
            firePoint.SetParent(bestHand, true);
            firePoint.position = bestMuzzle;
            Debug.Log($"[CyberGiorgia] Bocca del cannone su {bestHand.name}, oltre la mano di: {string.Join(", ", report)}.");
        }
        else
        {
            Debug.LogWarning("[CyberGiorgia] Mani non trovate nello scheletro: le granate partono da un punto fisso davanti al petto.");
        }

        // Il prefab resta nella posa a terra, non in quella del colpo.
        if (IdleClipInPlace != null)
        {
            IdleClipInPlace.SampleAnimation(instance, 0f);
        }
        return firePoint;
    }

    private static Transform FindBone(Transform root, string suffix)
    {
        return root.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.EndsWith(suffix));
    }

    // I vertici in mondo della mesh così com'è deformata adesso dalle ossa (BakeMesh).
    private static Vector3[] PosedVertices(GameObject target)
    {
        var result = new System.Collections.Generic.List<Vector3>();
        foreach (SkinnedMeshRenderer skin in target.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            Mesh baked = new Mesh();
            try
            {
                skin.BakeMesh(baked, true); // vertici già scalati: resta da applicare rotazione e posizione
                foreach (Vector3 vertex in baked.vertices)
                {
                    result.Add(skin.transform.position + skin.transform.rotation * vertex);
                }
            }
            finally
            {
                Object.DestroyImmediate(baked);
            }
        }
        return result.ToArray();
    }

    // Ingombro in mondo della mesh così com'è deformata adesso dalle ossa (BakeMesh), non quello
    // fisso salvato nell'FBX.
    private static bool TryMeasurePosed(GameObject target, out Bounds bounds)
    {
        Vector3[] vertices = PosedVertices(target);
        if (vertices.Length == 0)
        {
            return TryMeasure(target, out bounds); // niente mesh deformabili: ingombro dei Renderer
        }

        bounds = new Bounds(vertices[0], Vector3.zero);
        foreach (Vector3 vertex in vertices)
        {
            bounds.Encapsulate(vertex);
        }
        return true;
    }

    private static bool TryMeasure(GameObject target, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (Renderer r in target.GetComponentsInChildren<Renderer>())
        {
            if (!(r is MeshRenderer || r is SkinnedMeshRenderer))
            {
                continue;
            }

            if (!found)
            {
                bounds = r.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }
        return found;
    }

    // Le clip Generic pilotano le ossa per percorso: se mancano, il modello resta fermo o si
    // accartoccia senza errori in Console (vedi Sbirro2ModelSetup.ReportBindings).
    private static void ReportBindings(GameObject modelRoot, AnimatorController controller)
    {
        foreach (ChildAnimatorState child in controller.layers[0].stateMachine.states)
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
            int missing = paths.Count(path => modelRoot.transform.Find(path) == null);
            Debug.Log($"[CyberGiorgia] Stato \"{child.state.name}\" -> \"{clip.name}\": {paths.Length} ossa animate, " +
                      (missing == 0 ? "tutte agganciate." : $"{missing} NON agganciate."));
        }
    }

    private static void Fail(string message)
    {
        Debug.LogError($"[CyberGiorgia] {message}");
    }
}
