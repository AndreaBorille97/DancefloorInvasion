using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools/Player/Monta il modello animato: mette il modello models/player/TwinStickPlayer/PLAYER.fbx
// (rig Generic, scheletro Mixamo, Idle + una corsa in avanti sul posto, casse sulla testa con i
// punti di sparo Muzzle_L/Muzzle_R) come figlio del Player nella scena aperta, al posto della
// capsula. Tutto il resto (PlayerMovement, sparo, poteri, collider) resta com'è: cambia solo
// quello che si vede. Stesso schema di RaverModelSetup:
//  - le clip vanno in loop;
//  - niente Animator Controller: le anima TwinStickLocomotion (Playables), che gira le gambe
//    dove si va e il busto (con le casse) dove si mira; PlayerShooting fa partire i due anelli
//    di ogni onda da Muzzle_L e Muzzle_R;
//  - il modello viene alto ModelScale volte la capsula (come i Raver), con i piedi sulla sabbia
//    sotto il Player e girato dove guarda il Player (il suo +Z), misurato sullo scheletro;
//  - capsula e sfera del Player non si tolgono, si spengono soltanto i loro Renderer;
//  - se il materiale importato non ha la texture, se ne fa uno con la texture della cartella.
//
// È ripetibile: rilanciarlo rifà il montaggio da capo senza accumulare figli.
public static class PlayerModelSetup
{
    private const string Tag = "Player";
    private const string Folder = "Assets/Art/Models/player/TwinStickPlayer/";
    private const string ModelPath = Folder + "PLAYER.fbx";
    private const string MaterialPath = Folder + "PLAYER_mat.mat";
    private const string TexturePath = Folder + "Meshy_AI_Crimson_Sentinel_Mech_biped_texture_0.png";
    private const string ChildName = "PlayerModel";
    private const string RunClipName = "Running"; // le altre corse nel file (Running_backup_*, Running_original) sono scarti
    private const string IdleClipName = "Idle";

    // Come RaverModelSetup.ModelScale: Player e Raver hanno la stessa capsula, così i modelli
    // vengono della stessa taglia.
    private const float ModelScale = 1.5f;

    [MenuItem("Tools/Player/Monta il modello animato")]
    private static void Run()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null || player.GetComponent<PlayerMovement>() == null)
        {
            Fail("Nessun Player (tag Player con PlayerMovement) nella scena aperta.");
            return;
        }

        if (!SetLoopingClips())
        {
            return;
        }

        AnimationClip run = FindClip(RunClipName);
        AnimationClip idle = FindClip(IdleClipName);
        if (run == null || idle == null)
        {
            return;
        }

        if (Mount(player, run, idle))
        {
            Selection.activeGameObject = player;
        }
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

        foreach (ModelImporterClipAnimation clip in clips)
        {
            clip.loopTime = true;
        }
        importer.clipAnimations = clips;
        importer.importCameras = false;
        importer.importLights = false;
        importer.SaveAndReimport();
        return true;
    }

    private static AnimationClip FindClip(string name)
    {
        AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
            .OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview"))
            .ToArray();

        // Le take si chiamano "SmartRigArmature.001|Running": conta il pezzo dopo l'ultima "|".
        AnimationClip clip = clips.FirstOrDefault(c => c.name == name || c.name.EndsWith("|" + name));
        if (clip == null)
        {
            Fail($"Clip \"{name}\" non trovata in {ModelPath}. Clip presenti: " +
                 string.Join(", ", clips.Select(c => c.name)));
        }
        return clip;
    }

    private static bool Mount(GameObject player, AnimationClip run, AnimationClip idle)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        Transform root = player.transform;

        Transform old = root.Find(ChildName);
        if (old != null)
        {
            Undo.DestroyObjectImmediate(old.gameObject);
        }

        CapsuleCollider capsule = player.GetComponent<CapsuleCollider>();
        float capsuleHeight = capsule != null ? capsule.height * root.lossyScale.y : 2f;
        float capsuleBottomY = capsule != null
            ? root.TransformPoint(capsule.center).y - capsuleHeight * 0.5f
            : root.position.y - 1f;

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root);
        Undo.RegisterCreatedObjectUndo(instance, "Monta modello Player");
        instance.name = ChildName;
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        Vector3 baseScale = instance.transform.localScale;

        if (!AlignModelFront(instance))
        {
            Debug.LogWarning($"[{Tag}] Non trovo testa o piedi nello scheletro: non so dove guarda " +
                             "il modello, lo lascio girato com'è nel file.");
        }

        if (!TryMeasure(instance, out Bounds bounds) || bounds.size.y <= 0.0001f)
        {
            Undo.DestroyObjectImmediate(instance);
            Fail("Non riesco a misurare il modello: nessuna mesh trovata.");
            return false;
        }
        instance.transform.localScale = baseScale * (capsuleHeight * ModelScale / bounds.size.y);

        if (!TryFindGroundY(player, out float groundY))
        {
            Debug.LogWarning($"[{Tag}] Sotto il Player non trovo la sabbia: il modello poggia sul fondo della capsula.");
            groundY = capsuleBottomY;
        }
        if (TryMeasureSoleY(instance, out float soleY))
        {
            instance.transform.position += Vector3.up * (groundY - soleY);
        }

        FixMaterial(instance);

        Animator animator = instance.GetComponent<Animator>();
        if (animator == null)
        {
            animator = instance.AddComponent<Animator>();
        }
        animator.runtimeAnimatorController = null;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // Capsula e sfera del Player: restano (collider), ma non si vedono più.
        foreach (MeshRenderer placeholder in player.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (placeholder.transform.IsChildOf(instance.transform))
            {
                continue;
            }
            Undo.RecordObject(placeholder, "Monta modello Player");
            placeholder.enabled = false;
        }

        // Il vecchio modello lo animava PlayerModelAnimation, script tolto dal progetto: sul
        // Player della scena ne resta il componente vuoto ("Missing Script").
        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(player);

        TwinStick.TwinStickLocomotion locomotion = player.GetComponent<TwinStick.TwinStickLocomotion>();
        if (locomotion == null)
        {
            locomotion = Undo.AddComponent<TwinStick.TwinStickLocomotion>(player);
        }
        Undo.RecordObject(locomotion, "Monta modello Player");
        locomotion.animator = animator;
        locomotion.model = instance.transform;
        locomotion.runClip = run;
        locomotion.idleClip = idle;
        locomotion.torsoBones = null; // le ritrova da sola sul modello nuovo

        bool hasSpeakers = TwinStick.TwinStickLocomotion.FindBone(instance.transform, "Muzzle_L") != null
                           && TwinStick.TwinStickLocomotion.FindBone(instance.transform, "Muzzle_R") != null;
        if (!hasSpeakers)
        {
            Debug.LogWarning($"[{Tag}] Nel modello non trovo Muzzle_L e Muzzle_R: le onde partiranno dal centro del Player.");
        }

        EditorSceneManager.MarkSceneDirty(player.scene);
        Debug.Log($"[{Tag}] Modello montato sul Player: capsula alta {capsuleHeight:0.##}, scala " +
                  $"{instance.transform.localScale.y:0.###}, piedi a quota {groundY:0.##}. " +
                  "Salva la scena per tenerlo.");
        return true;
    }

    // Il davanti del modello: dove guarda la testa (osso headfront rispetto a Head), o in
    // mancanza dove puntano le dita dei piedi rispetto alla caviglia. Lo si gira sul +Z del
    // Player, che è dove il Player mira.
    private static bool AlignModelFront(GameObject instance)
    {
        Transform[] bones = instance.GetComponentsInChildren<Transform>();
        Transform Bone(string name) => bones.FirstOrDefault(t => t.name == name || t.name.EndsWith(":" + name));

        Vector3 front = Vector3.zero;
        Transform head = Bone("Head");
        Transform headFront = Bone("headfront");
        if (head != null && headFront != null)
        {
            front = headFront.position - head.position;
        }
        else
        {
            foreach (string side in new[] { "Left", "Right" })
            {
                Transform foot = Bone(side + "Foot");
                Transform toe = Bone(side + "ToeBase");
                if (foot != null && toe != null)
                {
                    front += toe.position - foot.position;
                }
            }
        }

        Vector3 localFront = instance.transform.parent.InverseTransformDirection(front);
        localFront.y = 0f;
        if (localFront.sqrMagnitude < 0.000001f)
        {
            return false;
        }

        float yaw = Vector3.SignedAngle(localFront, Vector3.forward, Vector3.up);
        instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        return true;
    }

    // Se il materiale arrivato con l'fbx non ha la texture (succede coi file di Meshy, che la
    // tengono a parte), se ne fa una copia con la texture della cartella.
    private static void FixMaterial(GameObject instance)
    {
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
        if (texture == null || renderers.Length == 0)
        {
            return;
        }

        Material imported = renderers[0].sharedMaterial;
        if (imported != null && imported.mainTexture != null)
        {
            return;
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = imported != null
                ? new Material(imported)
                : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.mainTexture = texture;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", Color.white);
        }
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();

        foreach (Renderer renderer in renderers)
        {
            Undo.RecordObject(renderer, "Monta modello Player");
            renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
        }
    }

    // Quota della sabbia sotto il Player: il primo collider solido sotto il centro della capsula
    // che non sia del Player né di un altro personaggio. Come RaverModelSetup.TryFindGroundY.
    private static bool TryFindGroundY(GameObject player, out float groundY)
    {
        groundY = 0f;
        Physics.SyncTransforms();

        Collider own = player.GetComponent<Collider>();
        Vector3 origin = own != null ? own.bounds.center : player.transform.position + Vector3.up;
        foreach (RaycastHit hit in Physics.RaycastAll(origin, Vector3.down, 50f, ~0, QueryTriggerInteraction.Ignore)
                     .OrderBy(h => h.distance))
        {
            Transform other = hit.collider.transform;
            if (other.IsChildOf(player.transform)
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

    // Quota della pianta dei piedi nell'Idle: il punto più basso della mesh deformata lungo la
    // clip. Su una copia temporanea, per non lasciare la posa nella scena.
    private static bool TryMeasureSoleY(GameObject instance, out float soleY)
    {
        soleY = float.MaxValue;
        AnimationClip idle = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
            .OfType<AnimationClip>()
            .FirstOrDefault(c => c.name == "Idle" || c.name.EndsWith("|Idle"));

        GameObject copy = Object.Instantiate(instance, instance.transform.parent);
        copy.hideFlags = HideFlags.HideAndDontSave;
        Mesh baked = new Mesh();
        try
        {
            const int Samples = 8;
            for (int i = 0; i < Samples; i++)
            {
                if (idle != null)
                {
                    idle.SampleAnimation(copy, idle.length * i / Samples);
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

                if (idle == null)
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
            if (!TryMeasure(instance, out Bounds bounds))
            {
                return false;
            }
            soleY = bounds.min.y;
        }

        return true;
    }

    // Ingombro misurato sulla mesh (posa di legatura), come RaverModelSetup.TryMeasure.
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
