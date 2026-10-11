using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools/Raver/Monta il modello animato su Raver Guard SoundSystem 26: mette raver3_animato
// (fatto in Blender, rig Generic) come figlio di quel Raver nella scena aperta, al posto della
// capsula. Tutto il resto (RaverChase, RaverAttack, RaverHealth, capsula di collisione) resta
// com'è: cambia solo quello che si vede.
//
// Come funziona:
//  - le take del file ("Armature|ballo", "Armature|pugno", "Armature|camminata"...) diventano
//    stati di un controller nuovo, chiamati col pezzo dopo la "|": niente parametri né
//    transizioni, è RaverAnimation a passare da uno all'altro;
//  - il modello viene alto quanto la capsula di collisione, con i piedi sul suo fondo;
//  - la capsula non si toglie (RaverDrugBuff vuole un Renderer sul Raver): si spegne soltanto.
//
// È ripetibile: rilanciarlo rifà il montaggio da capo senza accumulare figli.
public static class RaverModelSetup
{
    private const string Tag = "Raver";
    private const string TargetName = "Raver Guard SoundSystem 26";
    private const string ModelPath = "Assets/Prefabs/models/RAVERS/raver3_animato.fbx";
    private const string ControllerPath = "Assets/Prefabs/models/RAVERS/Raver3_Controller.controller";
    private const string ModelChildName = "Raver3Model";
    private const string DefaultState = "ballo";

    // Le clip che girano in loop; tutte le altre (il pugno) vanno una volta e si fermano.
    private static readonly string[] LoopingClips = { "ballo", "ballo_saltellante", "camminata", "camminata_normale" };

    [MenuItem("Tools/Raver/Monta il modello animato su Raver Guard SoundSystem 26")]
    private static void Run()
    {
        GameObject target = FindInOpenScene(TargetName);
        if (target == null)
        {
            Fail($"Nessun oggetto \"{TargetName}\" nella scena aperta.");
            return;
        }

        if (!SetLoopingClips())
        {
            return;
        }

        AnimatorController controller = BuildController();
        if (controller == null)
        {
            return;
        }

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        Transform root = target.transform;

        Transform old = root.Find(ModelChildName);
        if (old != null)
        {
            Undo.DestroyObjectImmediate(old.gameObject);
        }

        // Altezza e fondo della capsula, in coordinate mondo.
        CapsuleCollider capsule = target.GetComponent<CapsuleCollider>();
        float targetHeight = capsule != null ? capsule.height * root.lossyScale.y : 2f;
        float footY = capsule != null
            ? root.TransformPoint(capsule.center).y - targetHeight * 0.5f
            : root.position.y - 1f;

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root);
        Undo.RegisterCreatedObjectUndo(instance, "Monta modello Raver");
        instance.name = ModelChildName;
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        Vector3 baseScale = instance.transform.localScale;

        if (!TryMeasure(instance, out Bounds bounds) || bounds.size.y <= 0.0001f)
        {
            Undo.DestroyObjectImmediate(instance);
            Fail("Non riesco a misurare il modello: nessuna mesh trovata.");
            return;
        }

        float scaleFactor = targetHeight / bounds.size.y;
        instance.transform.localScale = baseScale * scaleFactor;
        TryMeasure(instance, out bounds);
        instance.transform.position += Vector3.up * (footY - bounds.min.y);

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

        if (target.GetComponent<RaverAnimation>() == null)
        {
            Undo.AddComponent<RaverAnimation>(target);
        }

        EditorSceneManager.MarkSceneDirty(target.scene);
        Selection.activeGameObject = target;
        Debug.Log($"[{Tag}] Modello montato su {TargetName}: alto {targetHeight:0.##}, scala {scaleFactor:0.###}, " +
                  $"controller {controller.name}. Salva la scena per tenerlo.");
    }

    private static bool SetLoopingClips()
    {
        ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
        {
            Fail($"Modello non trovato: {ModelPath}");
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
        importer.SaveAndReimport();
        return true;
    }

    // Controller rifatto da zero ogni volta: uno stato per clip, chiamato col nome corto.
    private static AnimatorController BuildController()
    {
        AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
            .OfType<AnimationClip>()
            .Where(clip => !clip.name.StartsWith("__preview__"))
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

        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
        {
            AssetDatabase.DeleteAsset(ControllerPath);
        }

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        AnimatorStateMachine machine = controller.layers[0].stateMachine;

        foreach (AnimationClip clip in clips)
        {
            AnimatorState state = machine.AddState(ShortName(clip.name));
            state.motion = clip;
            if (state.name == DefaultState)
            {
                machine.defaultState = state;
            }
        }

        AssetDatabase.SaveAssets();
        return controller;
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

    private static void Fail(string message)
    {
        EditorUtility.DisplayDialog(Tag, message, "OK");
        Debug.LogError($"[{Tag}] {message}");
    }
}
