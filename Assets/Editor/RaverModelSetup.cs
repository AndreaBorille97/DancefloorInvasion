using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools/Raver/Monta il modello animato...: mette un modello di Raver fatto in Blender (rig
// Generic) come figlio di un Raver nella scena aperta, al posto della capsula. Tutto il resto
// (RaverChase, RaverAttack, RaverHealth, capsula di collisione) resta com'è: cambia solo quello
// che si vede. Ci sono due voci:
//  - raver3 su Raver Guard SoundSystem 26;
//  - raver7 su Raver Guard Console 0 e Raver Guard SoundSystem 20. Ha le stesse identiche
//    animazioni del raver3, quindi lo stesso RaverAnimation lo guida allo stesso modo;
//  - raver6 su Raver Guard Console 1 e Raver Guard SoundSystem 34, montato come il raver7;
//  - raver1 su Raver Guard Console 5 e Raver Guard SoundSystem 39, montato come il raver7;
//  - raver4 su Raver Guard Console 7 e Raver Guard SoundSystem 32, montato come il raver7;
//  - raver2 su Raver Guard Console 9 e Raver Guard SoundSystem 22, montato come il raver7;
//  - raver5 su Raver Guard Console 4 e Raver Guard SoundSystem 36, montato come il raver7;
//  - raver8 su Raver Guard Console 11 e Raver Guard SoundSystem 30, montato come il raver7;
//  - i Raver rimasti, con una voce sola: raver6 su Console 10, raver3 su Console 6, raver4 su
//    SoundSystem 21, raver7 su SoundSystem 24 e raver2 su SoundSystem 37. Scelti in modo che
//    due Raver vicini (nella fila della console e in quella del sound system) non abbiano lo
//    stesso modello;
//  - il giocoliere (cartella giocoliere) sul Raver GIOCOLIERE, montato come il raver7 ma
//    lasciato girato com'è in scena.
//
// Come funziona:
//  - le take del file ("Armature|ballo", "Armature|pugno", "Armature|camminata"...) diventano
//    stati di un controller nuovo, chiamati col pezzo dopo la "|": niente parametri né
//    transizioni, è RaverAnimation a passare da uno all'altro;
//  - il modello viene alto ModelScale volte la capsula di collisione, con i piedi sulla sabbia
//    sotto il Raver (non sul fondo della capsula, che può stare un po' sopra o sotto);
//  - se il modello ha un material da fare (raver7), viene creato copiando quello del raver3 e
//    cambiandogli la texture, e messo su tutte le sue mesh;
//  - la capsula non si toglie (RaverDrugBuff vuole un Renderer sul Raver): si spegne soltanto.
//
// È ripetibile: rilanciarlo rifà il montaggio da capo senza accumulare figli.
public static class RaverModelSetup
{
    private const string Tag = "Raver";
    private const string DefaultState = "ballo";
    private const string Folder = "Assets/Prefabs/models/RAVERS/";

    // Quanto è grande il modello rispetto alla capsula di collisione (1 = alto quanto lei, per
    // il raver3; gli altri in proporzione, vedi MatchRaver3Skeleton). La capsula non cambia.
    private const float ModelScale = 1.5f;

    // Le clip che girano in loop; tutte le altre (il pugno) vanno una volta e si fermano.
    private static readonly string[] LoopingClips = { "ballo", "ballo_saltellante", "camminata", "camminata_normale", "no_testa_bassa" };

    private struct ModelSpec
    {
        public string ModelPath;
        public string ControllerPath;
        public string ChildName;
        // Material da creare per il modello (vuoto = si tiene quello che ha già). Si parte da
        // TemplateMaterialPath e gli si cambia soltanto la texture.
        public string MaterialPath;
        public string TexturePath;
        // true = alto quanto il raver3 già montato in scena, misurando lo scheletro (vedi
        // MatchRaver3Skeleton); false = alto quanto la capsula, misurando la mesh.
        public bool MatchRaver3;
        // true = il ballo va alla velocità con cui è stato animato in Blender; false = viene
        // accelerato o rallentato per stare a tempo coi bpm di RaverAnimation.
        public bool DanceAtBlenderSpeed;
        // true = il Raver non difende un obiettivo (il giocoliere): resta girato come è stato
        // messo in scena, invece di finire di spalle al SoundSystem o alla Console più vicini.
        public bool KeepRaverRotation;
    }

    private const string TemplateMaterialPath = Folder + "raver3/raver3_mat.mat";
    private const string Raver3ChildName = "Raver3Model";

    private static readonly ModelSpec Raver3 = new ModelSpec
    {
        ModelPath = Folder + "raver3/raver3_animato.fbx",
        ControllerPath = Folder + "raver3/Raver3_Controller.controller",
        ChildName = Raver3ChildName,
    };

    private static readonly ModelSpec Raver7 = new ModelSpec
    {
        ModelPath = Folder + "raver7/RAVER7_animato.fbx",
        ControllerPath = Folder + "raver7/Raver7_Controller.controller",
        ChildName = "Raver7Model",
        MaterialPath = Folder + "raver7/raver7_mat.mat",
        TexturePath = Folder + "raver7/raver7_texture.png",
        MatchRaver3 = true,
        DanceAtBlenderSpeed = true,
    };

    // Stesse animazioni del raver3 e del raver7: montato esattamente come il raver7.
    private static readonly ModelSpec Raver6 = new ModelSpec
    {
        ModelPath = Folder + "raver6/RAVER6_animato.fbx",
        ControllerPath = Folder + "raver6/Raver6_Controller.controller",
        ChildName = "Raver6Model",
        MaterialPath = Folder + "raver6/raver6_mat.mat",
        TexturePath = Folder + "raver6/Meshy_AI_Island_Vibes_Wanderer_biped_texture_0.png",
        MatchRaver3 = true,
        DanceAtBlenderSpeed = true,
    };

    // Stesse animazioni del raver3 e del raver7: montato come il raver7 e il raver6.
    private static readonly ModelSpec Raver1 = new ModelSpec
    {
        ModelPath = Folder + "raver1/RAVER1_animato.fbx",
        ControllerPath = Folder + "raver1/Raver1_Controller.controller",
        ChildName = "Raver1Model",
        MaterialPath = Folder + "raver1/raver1_mat.mat",
        TexturePath = Folder + "raver1/Meshy_AI_Raze_Inkforge_biped_texture_0.png",
        MatchRaver3 = true,
        DanceAtBlenderSpeed = true,
    };

    // Stesse animazioni degli altri (ballo, camminata, pugno): montato come raver1, 6 e 7.
    private static readonly ModelSpec Raver4 = new ModelSpec
    {
        ModelPath = Folder + "raver4/RAVER4_animato.fbx",
        ControllerPath = Folder + "raver4/Raver4_Controller.controller",
        ChildName = "Raver4Model",
        MaterialPath = Folder + "raver4/raver4_mat.mat",
        TexturePath = Folder + "raver4/Meshy_AI_Neon_Shadow_Punk_biped_texture_0.png",
        MatchRaver3 = true,
        DanceAtBlenderSpeed = true,
    };

    // raver2, raver5 e raver8: stesse animazioni degli altri (in più hanno "ballo_vecchio", che
    // diventa uno stato che RaverAnimation non usa). Montati come raver1, 4, 6 e 7.
    private static readonly ModelSpec Raver2 = new ModelSpec
    {
        ModelPath = Folder + "raver2/RAVER2_animato.fbx",
        ControllerPath = Folder + "raver2/Raver2_Controller.controller",
        ChildName = "Raver2Model",
        MaterialPath = Folder + "raver2/raver2_mat.mat",
        TexturePath = Folder + "raver2/Meshy_AI_Tropical_Rebel_biped_texture_0.png",
        MatchRaver3 = true,
        DanceAtBlenderSpeed = true,
    };

    private static readonly ModelSpec Raver5 = new ModelSpec
    {
        ModelPath = Folder + "raver5/RAVER5_animato.fbx",
        ControllerPath = Folder + "raver5/Raver5_Controller.controller",
        ChildName = "Raver5Model",
        MaterialPath = Folder + "raver5/raver5_mat.mat",
        TexturePath = Folder + "raver5/Meshy_AI_Nyx_Varela_biped_texture_0.png",
        MatchRaver3 = true,
        DanceAtBlenderSpeed = true,
    };

    private static readonly ModelSpec Raver8 = new ModelSpec
    {
        ModelPath = Folder + "raver8/RAVER8_animato.fbx",
        ControllerPath = Folder + "raver8/Raver8_Controller.controller",
        ChildName = "Raver8Model",
        MaterialPath = Folder + "raver8/raver8_mat.mat",
        TexturePath = Folder + "raver8/Meshy_AI_Violet_Rebel_biped_texture_0.png",
        MatchRaver3 = true,
        DanceAtBlenderSpeed = true,
    };

    // Rifà tutti i montaggi qui sopra in una volta, raver3 per primo (gli altri prendono
    // l'altezza da lui, vedi MatchRaver3Skeleton). Controller e material vengono aggiornati sul
    // posto, quindi si può rilanciare quante volte si vuole.
    [MenuItem("Tools/Raver/Rimonta tutti i modelli dei Raver")]
    private static void RunAll()
    {
        Run(Raver3, "Raver Guard SoundSystem 26", "Raver Guard Console 6");
        Run(Raver7, "Raver Guard Console 0", "Raver Guard SoundSystem 20", "Raver Guard SoundSystem 24");
        Run(Raver6, "Raver Guard Console 1", "Raver Guard SoundSystem 34", "Raver Guard Console 10");
        Run(Raver1, "Raver Guard Console 5", "Raver Guard SoundSystem 39");
        Run(Raver4, "Raver Guard Console 7", "Raver Guard SoundSystem 32", "Raver Guard SoundSystem 21");
        Run(Raver2, "Raver Guard Console 9", "Raver Guard SoundSystem 22", "Raver Guard SoundSystem 37");
        Run(Raver5, "Raver Guard Console 4", "Raver Guard SoundSystem 36");
        Run(Raver8, "Raver Guard Console 11", "Raver Guard SoundSystem 30");
        Run(Giocoliere, "GIOCOLIERE");
    }

    // Il raver3 va per primo: gli altri prendono l'altezza da lui (MatchRaver3Skeleton).
    [MenuItem("Tools/Raver/Monta i modelli sui Raver rimasti")]
    private static void RunRemaining()
    {
        Run(Raver3, "Raver Guard Console 6");
        Run(Raver6, "Raver Guard Console 10");
        Run(Raver4, "Raver Guard SoundSystem 21");
        Run(Raver7, "Raver Guard SoundSystem 24");
        Run(Raver2, "Raver Guard SoundSystem 37");
    }

    // Il giocoliere: stesse animazioni degli altri più "no_testa_bassa", che RaverAnimation fa
    // andare quando ha i poi a terra perché qualcuno gli è troppo vicino.
    private static readonly ModelSpec Giocoliere = new ModelSpec
    {
        ModelPath = Folder + "giocoliere/giocoliere_animato.fbx",
        ControllerPath = Folder + "giocoliere/Giocoliere_Controller.controller",
        ChildName = "GiocoliereModel",
        MaterialPath = Folder + "giocoliere/giocoliere_mat.mat",
        TexturePath = Folder + "giocoliere/Meshy_AI_Red_Dread_Corsair_biped_texture_0.png",
        MatchRaver3 = true,
        DanceAtBlenderSpeed = true,
        KeepRaverRotation = true,
    };

    [MenuItem("Tools/Raver/Monta il modello sul GIOCOLIERE")]
    private static void RunGiocoliere()
    {
        Run(Giocoliere, "GIOCOLIERE");
    }

    [MenuItem("Tools/Raver/Monta raver2 su Raver Guard Console 9 e SoundSystem 22")]
    private static void RunRaver2()
    {
        Run(Raver2, "Raver Guard Console 9", "Raver Guard SoundSystem 22");
    }

    [MenuItem("Tools/Raver/Monta raver5 su Raver Guard Console 4 e SoundSystem 36")]
    private static void RunRaver5()
    {
        Run(Raver5, "Raver Guard Console 4", "Raver Guard SoundSystem 36");
    }

    [MenuItem("Tools/Raver/Monta raver8 su Raver Guard Console 11 e SoundSystem 30")]
    private static void RunRaver8()
    {
        Run(Raver8, "Raver Guard Console 11", "Raver Guard SoundSystem 30");
    }

    [MenuItem("Tools/Raver/Monta raver4 su Raver Guard Console 7 e SoundSystem 32")]
    private static void RunRaver4()
    {
        Run(Raver4, "Raver Guard Console 7", "Raver Guard SoundSystem 32");
    }

    [MenuItem("Tools/Raver/Monta raver1 su Raver Guard Console 5 e SoundSystem 39")]
    private static void RunRaver1()
    {
        Run(Raver1, "Raver Guard Console 5", "Raver Guard SoundSystem 39");
    }

    [MenuItem("Tools/Raver/Monta raver6 su Raver Guard Console 1 e SoundSystem 34")]
    private static void RunRaver6()
    {
        Run(Raver6, "Raver Guard Console 1", "Raver Guard SoundSystem 34");
    }

    [MenuItem("Tools/Raver/Monta il modello animato su Raver Guard SoundSystem 26")]
    private static void RunRaver3()
    {
        Run(Raver3, "Raver Guard SoundSystem 26");
    }

    [MenuItem("Tools/Raver/Monta raver7 su Raver Guard Console 0 e SoundSystem 20")]
    private static void RunRaver7()
    {
        Run(Raver7, "Raver Guard Console 0", "Raver Guard SoundSystem 20");
    }

    private static void Run(ModelSpec spec, params string[] targetNames)
    {
        GameObject[] targets = targetNames.Select(FindInOpenScene).ToArray();
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null)
            {
                Fail($"Nessun oggetto \"{targetNames[i]}\" nella scena aperta.");
                return;
            }
        }

        if (!SetLoopingClips(spec.ModelPath))
        {
            return;
        }

        AnimatorController controller = BuildController(spec);
        if (controller == null)
        {
            return;
        }

        Material material = null;
        if (!string.IsNullOrEmpty(spec.MaterialPath))
        {
            material = BuildMaterial(spec);
            if (material == null)
            {
                return;
            }
        }

        foreach (GameObject target in targets)
        {
            if (!Mount(spec, target, controller, material))
            {
                return;
            }
        }

        Selection.objects = targets;
    }

    private static bool Mount(ModelSpec spec, GameObject target, AnimatorController controller, Material material)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath);
        Transform root = target.transform;

        Transform old = root.Find(spec.ChildName);
        if (old != null)
        {
            Undo.DestroyObjectImmediate(old.gameObject);
        }

        // Altezza e fondo della capsula, in coordinate mondo. Il fondo serve solo se sotto il
        // Raver non si trova la sabbia (vedi FindGroundY).
        CapsuleCollider capsule = target.GetComponent<CapsuleCollider>();
        float targetHeight = capsule != null ? capsule.height * root.lossyScale.y : 2f;
        float capsuleBottomY = capsule != null
            ? root.TransformPoint(capsule.center).y - targetHeight * 0.5f
            : root.position.y - 1f;

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root);
        Undo.RegisterCreatedObjectUndo(instance, "Monta modello Raver");
        instance.name = spec.ChildName;
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        Vector3 baseScale = instance.transform.localScale;

        if (!spec.KeepRaverRotation)
        {
            FaceAwayFromObjective(target);
        }
        if (!AlignModelFront(instance))
        {
            Debug.LogWarning($"[{Tag}] Non trovo piedi e punte in {spec.ChildName}: non so dove " +
                             "guarda, lo lascio girato com'è nel file.");
        }

        if (!TryMeasure(instance, out Bounds bounds) || bounds.size.y <= 0.0001f)
        {
            Undo.DestroyObjectImmediate(instance);
            Fail("Non riesco a misurare il modello: nessuna mesh trovata.");
            return false;
        }

        float scaleFactor = targetHeight / bounds.size.y;
        instance.transform.localScale = baseScale * scaleFactor;

        if (spec.MatchRaver3 && !MatchRaver3Skeleton(spec, instance, targetHeight))
        {
            Debug.LogWarning($"[{Tag}] Non riesco a confrontare lo scheletro col raver3: " +
                             $"{spec.ChildName} resta alto quanto la capsula.");
        }

        instance.transform.localScale *= ModelScale;

        if (!TryFindGroundY(target, out float groundY))
        {
            Debug.LogWarning($"[{Tag}] Sotto {target.name} non trovo la sabbia: {spec.ChildName} " +
                             "poggia sul fondo della capsula.");
            groundY = capsuleBottomY;
        }
        if (TryMeasureSoleY(instance, out float soleY))
        {
            instance.transform.position += Vector3.up * (groundY - soleY);
        }

        // I poi di fuoco del giocoliere hanno la base a terra (vedi PoiFireDance): da lì si
        // misurano le teste quando le fiamme sono appoggiate al suolo.
        PoiFireDance poi = target.GetComponentInChildren<PoiFireDance>(true);
        if (poi != null)
        {
            Undo.RecordObject(poi.transform, "Monta modello Raver");
            Vector3 poiPosition = poi.transform.position;
            poiPosition.y = groundY;
            poi.transform.position = poiPosition;
        }

        if (material != null)
        {
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
            }
        }

        Animator animator = instance.GetComponent<Animator>();
        if (animator == null)
        {
            animator = instance.AddComponent<Animator>();
        }
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false; // il Raver lo muove RaverChase

        MeshRenderer placeholder = target.GetComponent<MeshRenderer>();
        if (placeholder != null)
        {
            Undo.RecordObject(placeholder, "Monta modello Raver");
            placeholder.enabled = false;
        }

        RaverAnimation raverAnimation = target.GetComponent<RaverAnimation>();
        if (raverAnimation == null)
        {
            raverAnimation = Undo.AddComponent<RaverAnimation>(target);
        }

        SerializedObject animationSettings = new SerializedObject(raverAnimation);
        animationSettings.FindProperty("danceAtBlenderSpeed").boolValue = spec.DanceAtBlenderSpeed;
        animationSettings.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(target.scene);
        Debug.Log($"[{Tag}] Modello {spec.ChildName} montato su {target.name}: capsula alta {targetHeight:0.##}, " +
                  $"scala {instance.transform.localScale.y:0.###}, piedi a quota {groundY:0.##}, controller {controller.name}. Salva la scena per tenerlo.");
        return true;
    }

    // Quota della sabbia sotto il Raver: il primo collider solido sotto il centro della capsula
    // che non sia del Raver stesso né di un altro personaggio (Raver o Sbirro). Il pavimento
    // (Floor) ha un BoxCollider, quindi lo si trova coi raggi anche fuori dal Play.
    private static bool TryFindGroundY(GameObject target, out float groundY)
    {
        groundY = 0f;
        Physics.SyncTransforms();

        Collider own = target.GetComponent<Collider>();
        Vector3 origin = own != null ? own.bounds.center : target.transform.position + Vector3.up;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 50f, ~0, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits.OrderBy(h => h.distance))
        {
            Transform other = hit.collider.transform;
            if (other.IsChildOf(target.transform)
                || other.GetComponentInParent<RaverChase>() != null
                || other.GetComponentInParent<EnemyHealth>() != null)
            {
                continue;
            }

            groundY = hit.point.y;
            return true;
        }

        return false;
    }

    // Quota della pianta dei piedi del modello mentre balla: il punto più basso della mesh lungo
    // tutto il ballo, cioè quando il piede tocca terra. Misurato sulla mesh deformata e non
    // sulle ossa, che stanno dentro la caviglia, né sulla mesh a riposo, che non è la posa con
    // cui sta di guardia. Su una copia temporanea, per non lasciare la posa nella scena.
    private static bool TryMeasureSoleY(GameObject instance, out float soleY)
    {
        soleY = float.MaxValue;
        AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(
                PrefabUtility.GetCorrespondingObjectFromSource(instance)))
            .OfType<AnimationClip>()
            .Where(c => ShortName(c.name) == DefaultState)
            .OrderBy(c => c.name.Count(ch => ch == '|'))
            .FirstOrDefault(); // come BuildController: la meno annidata

        GameObject copy = Object.Instantiate(instance, instance.transform.parent);
        copy.hideFlags = HideFlags.HideAndDontSave;
        Mesh baked = new Mesh();
        try
        {
            const int Samples = 16;
            for (int i = 0; i < Samples; i++)
            {
                if (clip != null)
                {
                    clip.SampleAnimation(copy, clip.length * i / Samples);
                }

                foreach (SkinnedMeshRenderer skinned in copy.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    skinned.BakeMesh(baked, true);
                    Matrix4x4 toWorld = Matrix4x4.TRS(skinned.transform.position, skinned.transform.rotation, Vector3.one);
                    foreach (Vector3 vertex in baked.vertices)
                    {
                        soleY = Mathf.Min(soleY, toWorld.MultiplyPoint3x4(vertex).y);
                    }
                }

                if (clip == null)
                {
                    break;
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(baked);
            Object.DestroyImmediate(copy);
        }

        if (soleY == float.MaxValue)
        {
            // Niente mesh skinnate: si ripiega sull'ingombro a riposo.
            if (!TryMeasure(instance, out Bounds bounds))
            {
                return false;
            }
            soleY = bounds.min.y;
        }

        return true;
    }

    // Altezza della mesh del modello alla sua scala d'importazione (posa di legatura).
    private static bool TryMeasureImportHeight(string modelPath, out float height)
    {
        height = 0f;
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (asset == null)
        {
            return false;
        }

        GameObject copy = Object.Instantiate(asset);
        copy.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            if (!TryMeasure(copy, out Bounds bounds))
            {
                return false;
            }
            height = bounds.size.y;
            return height > 0.0001f;
        }
        finally
        {
            Object.DestroyImmediate(copy);
        }
    }

    // RaverAnimation dà per scontato che, a rotazione di partenza, il modello guardi dove guarda
    // il Raver (il suo +Z): da fermo lo gira dalla parte opposta a quella verso cui RaverChase
    // tiene girato il Raver, cioè di spalle all'obiettivo. I file però non guardano tutti dalla
    // stessa parte, quindi il davanti si misura sullo scheletro: è dove puntano le dita dei
    // piedi rispetto alla caviglia, nella posa di legatura.
    private static bool AlignModelFront(GameObject instance)
    {
        Transform[] bones = instance.GetComponentsInChildren<Transform>();
        Vector3 front = Vector3.zero;
        foreach (string side in new[] { "Left", "Right" })
        {
            Transform foot = bones.FirstOrDefault(t => t.name == side + "Foot");
            Transform toe = bones.FirstOrDefault(t => t.name == side + "ToeBase");
            if (foot != null && toe != null)
            {
                front += toe.position - foot.position;
            }
        }

        Transform root = instance.transform.parent;
        Vector3 localFront = root.InverseTransformDirection(front);
        localFront.y = 0f;
        if (localFront.sqrMagnitude < 0.000001f)
        {
            return false;
        }

        float yaw = Vector3.SignedAngle(localFront, Vector3.forward, Vector3.up);
        instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        return true;
    }

    // Gira il Raver di spalle a quello che difende, così già in scena (e al primo fotogramma di
    // gioco, prima che RaverChase lo giri verso l'obiettivo e RaverAnimation tenga il modello
    // dall'altra parte) il modello ha l'obiettivo dietro. L'obiettivo è quello di RaverChase:
    // il suo defendedObjective, o se è vuoto il SoundSystem o la Console più vicini, come fa lui.
    private static void FaceAwayFromObjective(GameObject target)
    {
        Transform objective = FindDefendedObjective(target);
        if (objective == null)
        {
            Debug.LogWarning($"[{Tag}] Nessun SoundSystem o Console in scena: {target.name} resta girato com'è.");
            return;
        }

        // Il centro della sagoma e non il pivot, come RaverChase.ObjectiveCenter.
        Collider objectiveCollider = objective.GetComponentInChildren<Collider>();
        Vector3 center = objectiveCollider != null ? objectiveCollider.bounds.center : objective.position;
        Vector3 away = target.transform.position - center;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Undo.RecordObject(target.transform, "Monta modello Raver");
        target.transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
    }

    private static bool SetLoopingClips(string modelPath)
    {
        ModelImporter importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (importer == null)
        {
            Fail($"Modello non trovato: {modelPath}");
            return false;
        }

        ModelImporterClipAnimation[] clips = importer.clipAnimations.Length > 0
            ? importer.clipAnimations
            : importer.defaultClipAnimations;
        if (clips.Length == 0)
        {
            Fail("Il file non ha animazioni.");
            return false;
        }

        for (int i = 0; i < clips.Length; i++)
        {
            clips[i].loopTime = LoopingClips.Contains(ShortName(clips[i].name));
        }
        importer.clipAnimations = clips;

        // Blender esporta anche la camera e le luci della scena: nel Raver non servono, e una
        // luce in più su ogni Raver illuminerebbe tutto quello che gli sta intorno.
        importer.importCameras = false;
        importer.importLights = false;

        importer.SaveAndReimport();
        return true;
    }

    // Controller rifatto da zero ogni volta: uno stato per clip, chiamato col nome corto.
    private static AnimatorController BuildController(ModelSpec spec)
    {
        // Una clip per nome corto. Se il file ne ha due con lo stesso nome vince quella meno
        // annidata, cioè l'azione del modello: un export di Blender può portarsi dietro le azioni
        // di un altro modello come "Armature|Armature|ballo", accanto a "Armature|ballo" (era
        // successo col primo RAVER1_animato), e lo stato "ballo" prendeva la prima della lista.
        AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(spec.ModelPath)
            .OfType<AnimationClip>()
            .Where(clip => !clip.name.StartsWith("__preview__"))
            .GroupBy(clip => ShortName(clip.name))
            .Select(group => group.OrderBy(clip => clip.name.Count(c => c == '|')).First())
            .ToArray();

        foreach (string needed in new[] { "ballo", "pugno", "camminata" })
        {
            if (!clips.Any(clip => ShortName(clip.name) == needed))
            {
                Fail($"Nel file manca la clip \"{needed}\". Clip trovate: " +
                     string.Join(", ", clips.Select(clip => clip.name)));
                return null;
            }
        }

        // Se il controller c'è già si svuota e si riempie, senza cancellarlo: cancellato e rifatto
        // avrebbe un altro GUID, e i Raver già montati con questo modello lo perderebbero.
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(spec.ControllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(spec.ControllerPath);
        }

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        foreach (ChildAnimatorState old in machine.states)
        {
            machine.RemoveState(old.state);
        }

        foreach (AnimationClip clip in clips)
        {
            AnimatorState state = machine.AddState(ShortName(clip.name));
            state.motion = clip;
            if (state.name == DefaultState)
            {
                machine.defaultState = state;
            }
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    // Copia del material del raver3 (stesso shader e stesse impostazioni) con la texture del
    // modello. Se esiste già viene aggiornato e non ricreato, così i Raver che lo usano non
    // perdono il riferimento.
    private static Material BuildMaterial(ModelSpec spec)
    {
        Material template = AssetDatabase.LoadAssetAtPath<Material>(TemplateMaterialPath);
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(spec.TexturePath);
        if (template == null || texture == null)
        {
            Fail($"Mi servono {TemplateMaterialPath} e {spec.TexturePath}, ma non li trovo.");
            return null;
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(spec.MaterialPath);
        if (material == null)
        {
            material = new Material(template);
            AssetDatabase.CreateAsset(material, spec.MaterialPath);
        }
        else
        {
            material.CopyPropertiesFromMaterial(template);
        }

        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture); // URP/Lit
        }
        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", texture); // Standard/fallback
        }

        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return material;
    }

    private static GameObject FindInOpenScene(string name)
    {
        return Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(t => t.name == name && !EditorUtility.IsPersistent(t))
            .Select(t => t.gameObject)
            .FirstOrDefault();
    }

    // "Armature|pugno" -> "pugno".
    private static string ShortName(string clipName)
    {
        int bar = clipName.LastIndexOf('|');
        return bar >= 0 ? clipName.Substring(bar + 1) : clipName;
    }

    // Posa in cui si confrontano i due scheletri: la camminata all'inizio, a braccia giù.
    // Conta solo che sia la stessa per tutti e due.
    private const string MeasureClip = "camminata";

    // Rende instance alto quanto lo sarebbe il raver3 montato su questo Raver (cioè alto quanto
    // la capsula, prima di ModelScale). Si confrontano gli scheletri (stesse ossa nei due
    // modelli) e non le mesh: il raver7 ha mesh in più e non si può sapere quali sporgano, e la
    // posa con cui il file si apre non è detto sia la stessa. Così il confronto è a parità di
    // posa e di ossa, e non dipende da come è fatta la mesh. Il raver3 di riferimento si calcola
    // dalla capsula e non si legge dalla scena: così non conta se e come è già montato.
    private static bool MatchRaver3Skeleton(ModelSpec spec, GameObject instance, float targetHeight)
    {
        // Si misurano solo le ossa che ha anche il raver3: un modello con ossa in più (il raver6 ha
        // Bone_006/012/018, capelli o accessori) le avrebbe contate nell'altezza, venendo fuori
        // della taglia sbagliata.
        HashSet<string> sharedBones = GetBoneNames(Raver3.ModelPath);
        if (sharedBones == null
            || !TryMeasureImportHeight(Raver3.ModelPath, out float raver3MeshHeight)
            || !TryMeasureSkeleton(Raver3.ModelPath, sharedBones, out float raver3Height, out _, out _)
            || !TryMeasureSkeleton(spec.ModelPath, sharedBones, out float height, out _, out float baseScale)
            || height <= 0.0001f)
        {
            return false;
        }

        // Le misure sono sul modello alla sua scala d'importazione: raver3K è quanto il raver3
        // viene ingrandito per stare alto quanto la capsula, k quanto va ingrandito questo.
        float raver3K = targetHeight / raver3MeshHeight;
        float k = raver3Height * raver3K / height;

        Transform root = instance.transform.parent;
        instance.transform.localScale = Vector3.one * (baseScale * k / root.lossyScale.y);

        Debug.Log($"[{Tag}] Scheletro raver3 {raver3Height:0.###}, {spec.ChildName} {height:0.###} " +
                  $"(alla scala d'importazione): scala portata a {instance.transform.localScale.y:0.###}.");
        return true;
    }

    // Lo scheletro del modello: il genitore dell'osso "Hips". Non si cerca per nome perché non
    // tutti i file lo chiamano allo stesso modo ("Armature" nel raver3 e raver7,
    // "SmartRigArmature" nel raver6); cercato per nome, il raver6 non lo trovava e finiva
    // alto quanto la capsula invece che quanto il raver3.
    private static Transform FindArmature(GameObject model)
    {
        Transform hips = model.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Hips");
        return hips != null ? hips.parent : null;
    }

    // Nomi delle ossa sotto lo scheletro del modello (null se non ha uno scheletro, vedi FindArmature).
    private static HashSet<string> GetBoneNames(string modelPath)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        Transform armature = asset != null
            ? FindArmature(asset)
            : null;
        if (armature == null)
        {
            return null;
        }

        return new HashSet<string>(armature.GetComponentsInChildren<Transform>()
            .Where(t => t != armature)
            .Select(t => t.name));
    }

    // Altezza dello scheletro (osso più alto meno osso più basso) e quota dell'osso più basso
    // rispetto all'origine del modello, su una copia temporanea messa nella posa MeasureClip.
    // Contano solo le ossa in onlyBones.
    private static bool TryMeasureSkeleton(string modelPath, HashSet<string> onlyBones, out float height, out float foot, out float baseScale)
    {
        height = foot = 0f;
        baseScale = 1f;

        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(modelPath)
            .OfType<AnimationClip>()
            .Where(c => ShortName(c.name) == MeasureClip)
            .OrderBy(c => c.name.Count(ch => ch == '|'))
            .FirstOrDefault(); // come BuildController: la meno annidata
        if (asset == null || clip == null)
        {
            return false;
        }

        GameObject copy = Object.Instantiate(asset);
        copy.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            baseScale = copy.transform.localScale.y;
            clip.SampleAnimation(copy, 0f);

            Transform armature = FindArmature(copy);
            if (armature == null)
            {
                return false;
            }

            float min = float.MaxValue;
            float max = float.MinValue;
            foreach (Transform bone in armature.GetComponentsInChildren<Transform>())
            {
                if (bone == armature || !onlyBones.Contains(bone.name))
                {
                    continue;
                }
                min = Mathf.Min(min, bone.position.y);
                max = Mathf.Max(max, bone.position.y);
            }

            if (min > max)
            {
                return false;
            }

            height = max - min;
            foot = min;
            return true;
        }
        finally
        {
            Object.DestroyImmediate(copy);
        }
    }

    // Ingombro misurato sulla mesh (posa di legatura), non su Renderer.bounds che per una mesh
    // skinnata dipende dalla posa: stesso metodo di NewSbirroModelSetup.
    private static bool TryMeasure(GameObject target, out Bounds bounds)
    {
        bounds = new Bounds(target.transform.position, Vector3.zero);
        bool found = false;

        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>())
        {
            Mesh mesh = renderer is SkinnedMeshRenderer skinned
                ? skinned.sharedMesh
                : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null || mesh.bounds.size.sqrMagnitude <= 0.000001f)
            {
                continue;
            }

            Bounds local = mesh.bounds;
            Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 offset = new Vector3(
                    (corner & 1) == 0 ? -local.extents.x : local.extents.x,
                    (corner & 2) == 0 ? -local.extents.y : local.extents.y,
                    (corner & 4) == 0 ? -local.extents.z : local.extents.z);
                Vector3 world = toWorld.MultiplyPoint3x4(local.center + offset);

                if (!found)
                {
                    bounds = new Bounds(world, Vector3.zero);
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(world);
                }
            }
        }

        return found;
    }

    // Quello che il Raver difende, come lo sceglie RaverChase: il suo defendedObjective, o se è
    // vuoto il SoundSystem o la Console più vicini. Null se in scena non ce n'è nessuno.
    private static Transform FindDefendedObjective(GameObject raver)
    {
        RaverChase chase = raver.GetComponent<RaverChase>();
        Transform objective = chase != null
            ? new SerializedObject(chase).FindProperty("defendedObjective").objectReferenceValue as Transform
            : null;
        if (objective != null)
        {
            return objective;
        }

        return Object.FindObjectsByType<SoundSystem>(FindObjectsSortMode.None).Select(s => s.transform)
            .Concat(Object.FindObjectsByType<DJConsoleHealth>(FindObjectsSortMode.None).Select(c => c.transform))
            .OrderBy(t => (t.position - raver.transform.position).sqrMagnitude)
            .FirstOrDefault();
    }

    private static void Fail(string message)
    {
        EditorUtility.DisplayDialog(Tag, message, "OK");
        Debug.LogError($"[{Tag}] {message}");
    }

    // ---- Seconda fila di guardie ------------------------------------------------------------
    //
    // Tools/Raver/Aggiungi la seconda fila di guardie: per ogni obiettivo (Console, SoundSystem)
    // aggiunge dietro le guardie che ci sono già la metà di quante sono (arrotondando per
    // eccesso). Ognuna sta nello spazio fra due guardie vicine della prima fila, SecondRowSpacing
    // metri più lontano dall'obiettivo, così si vede fra le due davanti invece di stare loro
    // dietro coperta. Sono Raver del prefab come le altre (stesso comportamento: RaverChase le
    // fa difendere l'obiettivo più vicino) e prendono un modello diverso da quelli delle guardie
    // entro NeighbourRadius. Rilanciarlo toglie la seconda fila e la rifà da capo.

    private const string GuardPrefix = "Raver Guard ";
    private const string SecondRowMark = " Fila2 ";
    private const float SecondRowSpacing = 1.6f;
    // Se lì c'è già qualcosa (un gazebo, una tenda, un'altra guardia) si prova un po' più
    // vicino o più lontano, nell'ordine; se è occupato ovunque quella guardia si salta.
    private static readonly float[] SpacingAttempts = { SecondRowSpacing, SecondRowSpacing * 1.4f, SecondRowSpacing * 0.75f };
    private const float NeighbourRadius = 4f;

    [MenuItem("Tools/Raver/Aggiungi la seconda fila di guardie")]
    private static void RunSecondRow()
    {
        foreach (GameObject old in SceneObjects().Where(go => go.name.StartsWith(GuardPrefix) && go.name.Contains(SecondRowMark)).ToArray())
        {
            Undo.DestroyObjectImmediate(old);
        }
        Physics.SyncTransforms();

        List<GameObject> firstRow = SceneObjects()
            .Where(go => go.name.StartsWith(GuardPrefix) && go.GetComponent<RaverChase>() != null)
            .ToList();
        List<GameObject> created = new List<GameObject>();

        foreach (IGrouping<Transform, GameObject> group in firstRow.GroupBy(FindDefendedObjective))
        {
            if (group.Key == null)
            {
                continue;
            }
            created.AddRange(BuildSecondRow(group.Key, group.ToList()));
        }

        if (created.Count == 0)
        {
            Fail("Non ho trovato posto per nessuna guardia della seconda fila.");
            return;
        }

        // Modelli: si sceglie una guardia alla volta, tenendo conto anche di quelle appena
        // scelte, e poi si monta tutto con un Run per modello (ognuno reimporta il suo FBX).
        Dictionary<GameObject, string> models = firstRow.ToDictionary(g => g, MountedModelName);
        Dictionary<string, int> uses = GuardModels.ToDictionary(spec => spec.ChildName,
            spec => models.Values.Count(name => name == spec.ChildName));
        Dictionary<GameObject, ModelSpec> chosen = new Dictionary<GameObject, ModelSpec>();
        foreach (GameObject guard in created)
        {
            Vector3 position = guard.transform.position;
            HashSet<string> nearby = new HashSet<string>(models
                .Where(pair => pair.Value != null && Flat(pair.Key.transform.position - position).magnitude <= NeighbourRadius)
                .Select(pair => pair.Value));

            ModelSpec spec = GuardModels
                .OrderBy(s => nearby.Contains(s.ChildName) ? 1 : 0)
                .ThenBy(s => uses[s.ChildName])
                .First();
            if (nearby.Contains(spec.ChildName))
            {
                Debug.LogWarning($"[{Tag}] Attorno a {guard.name} ci sono già tutti i modelli: gli do {spec.ChildName}.");
            }

            chosen[guard] = spec;
            models[guard] = spec.ChildName;
            uses[spec.ChildName]++;
        }

        foreach (IGrouping<string, KeyValuePair<GameObject, ModelSpec>> bySpec in chosen.GroupBy(pair => pair.Value.ChildName))
        {
            Run(bySpec.First().Value, bySpec.Select(pair => pair.Key.name).ToArray());
        }

        Selection.objects = created.ToArray();
        Debug.Log($"[{Tag}] Seconda fila: {created.Count} guardie aggiunte. Salva la scena per tenerle.");
    }

    // Le guardie della seconda fila davanti a objective, già create in scena (senza modello).
    private static List<GameObject> BuildSecondRow(Transform objective, List<GameObject> guards)
    {
        List<GameObject> created = new List<GameObject>();
        if (guards.Count < 2)
        {
            return created;
        }

        // In fila da un'estremità all'altra: per angolo attorno all'obiettivo, misurato rispetto
        // alla direzione media delle guardie, così la fila non si spezza dove l'angolo fa il giro.
        Vector3 center = ObjectiveBounds(objective).center;
        Vector3 mean = Flat(guards.Aggregate(Vector3.zero, (sum, g) => sum + Flat(g.transform.position - center).normalized));
        if (mean.sqrMagnitude < 0.0001f)
        {
            mean = Vector3.forward;
        }
        List<GameObject> ordered = guards
            .OrderBy(g => Vector3.SignedAngle(mean, Flat(g.transform.position - center), Vector3.up))
            .ToList();

        bool isConsole = objective.GetComponent<DJConsoleHealth>() != null;
        string kind = isConsole ? "Console" : "SoundSystem";
        int count = Mathf.CeilToInt(guards.Count / 2f);
        int gaps = ordered.Count - 1;
        HashSet<int> usedGaps = new HashSet<int>();

        for (int k = 0; k < count; k++)
        {
            // Spazi presi a intervalli regolari lungo la fila.
            int gap = Mathf.Clamp(Mathf.RoundToInt((k + 0.5f) * gaps / count - 0.5f), 0, gaps - 1);
            if (!usedGaps.Add(gap))
            {
                continue;
            }

            GameObject a = ordered[gap];
            GameObject b = ordered[gap + 1];
            Vector3 middle = (a.transform.position + b.transform.position) * 0.5f;
            Vector3 outward = Flat(OutwardFrom(objective, a.transform.position) + OutwardFrom(objective, b.transform.position));
            if (outward.sqrMagnitude < 0.0001f)
            {
                continue;
            }
            outward.Normalize();

            Vector3? spot = null;
            foreach (float spacing in SpacingAttempts)
            {
                Vector3 candidate = middle + outward * spacing;
                candidate.y = a.transform.position.y;
                if (!IsSpotBlocked(a, candidate))
                {
                    spot = candidate;
                    break;
                }
            }
            if (spot == null)
            {
                Debug.LogWarning($"[{Tag}] Dietro a {a.name} e {b.name} è tutto occupato: lì non metto nessuno.");
                continue;
            }

            GameObject prefab = PrefabUtility.GetCorrespondingObjectFromSource(a);
            GameObject guard = (GameObject)PrefabUtility.InstantiatePrefab(prefab, a.scene);
            Undo.RegisterCreatedObjectUndo(guard, "Seconda fila di guardie");
            guard.transform.SetParent(a.transform.parent, true);
            guard.transform.SetPositionAndRotation(spot.Value, a.transform.rotation);
            guard.name = $"{GuardPrefix}{kind}{SecondRowMark}{created.Count}";
            created.Add(guard);
            Physics.SyncTransforms(); // la prossima deve vedere questa come ingombro
        }

        return created;
    }

    // Se la capsula di source, messa in position, toccherebbe qualcosa di solido: un oggetto
    // di scena o un altro personaggio. Il pavimento non conta: né quello che c'è sotto (la
    // capsula può affondarci un po'), né quello che sta tutto sotto il fondo della capsula.
    private static bool IsSpotBlocked(GameObject source, Vector3 position)
    {
        CapsuleCollider capsule = source.GetComponent<CapsuleCollider>();
        if (capsule == null)
        {
            return false;
        }

        Vector3 scale = source.transform.lossyScale;
        float radius = capsule.radius * Mathf.Max(scale.x, scale.z);
        float height = Mathf.Max(capsule.height * scale.y, radius * 2f);
        Vector3 center = position + source.transform.rotation * Vector3.Scale(capsule.center, scale);
        Vector3 axis = Vector3.up * (height * 0.5f - radius);
        float bottom = center.y - height * 0.5f;

        Collider ground = Physics.Raycast(center + Vector3.up * height, Vector3.down, out RaycastHit hit, 50f, ~0, QueryTriggerInteraction.Ignore)
                          && hit.point.y < center.y
            ? hit.collider
            : null;

        return Physics.OverlapCapsule(center - axis, center + axis, radius, ~0, QueryTriggerInteraction.Ignore)
            .Any(c => c != ground && c.bounds.max.y > bottom + 0.1f);
    }

    // Direzione orizzontale che va dalla superficie dell'obiettivo verso position.
    private static Vector3 OutwardFrom(Transform objective, Vector3 position)
    {
        Vector3 closest = objective.GetComponentsInChildren<Collider>()
            .Where(c => !c.isTrigger)
            .Select(c => c.bounds.ClosestPoint(position))
            .DefaultIfEmpty(objective.position)
            .OrderBy(p => (p - position).sqrMagnitude)
            .First();
        Vector3 outward = Flat(position - closest);
        return outward.sqrMagnitude > 0.0001f ? outward.normalized : Flat(position - ObjectiveBounds(objective).center).normalized;
    }

    private static Bounds ObjectiveBounds(Transform objective)
    {
        Collider[] colliders = objective.GetComponentsInChildren<Collider>();
        if (colliders.Length == 0)
        {
            return new Bounds(objective.position, Vector3.zero);
        }

        Bounds bounds = colliders[0].bounds;
        foreach (Collider c in colliders)
        {
            bounds.Encapsulate(c.bounds);
        }
        return bounds;
    }

    // Il modello montato sulla guardia (il ChildName del suo figlio), null se non ne ha.
    private static string MountedModelName(GameObject guard)
    {
        foreach (Transform child in guard.transform)
        {
            if (GuardModels.Any(spec => spec.ChildName == child.name))
            {
                return child.name;
            }
        }
        return null;
    }

    private static IEnumerable<GameObject> SceneObjects()
    {
        return Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(t => !EditorUtility.IsPersistent(t))
            .Select(t => t.gameObject);
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    // I modelli fra cui scegliere per le guardie (il giocoliere no: è suo). In fondo alla
    // classe apposta: i campi statici si inizializzano nell'ordine in cui sono scritti, e qui
    // sopra i ModelSpec sono già pronti.
    private static readonly ModelSpec[] GuardModels = { Raver1, Raver2, Raver3, Raver4, Raver5, Raver6, Raver7, Raver8 };
}
