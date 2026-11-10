using System.Linq;
using UnityEditor;
using UnityEngine;

// Tools/Sbirro3/Stessa taglia dello Sbirro2: riscala il modello dentro Sbirro3.prefab perché il
// CORPO sia alto quanto quello dello Sbirro2, lasciando i piedi dove sono.
//
// NewSbirroModelSetup porta entrambi all'altezza dello Sbirro1 misurando tutto il modello, arma
// compresa: col lanciagranate che ne allarga l'ingombro, il maialino dello Sbirro3 veniva più
// piccolo di quello dello Sbirro2. Qui si misura solo il corpo, cioè la mesh legata a più ossa
// (arma, scudo e accessori seguono al massimo una mano), con lo scheletro a riposo del prefab.
//
// È ripetibile. Rilanciare "Tools/Sbirro3/Monta il nuovo modello animato" rimette la taglia di
// prima: dopo va rilanciato anche questo.
public static class Sbirro3SizeMatch
{
    private const string ReferencePrefabPath = "Assets/Prefabs/Characters/Sbirro2.prefab";
    private const string TargetPrefabPath = "Assets/Prefabs/Characters/Sbirro3.prefab";

    [MenuItem("Tools/Sbirro3/Stessa taglia dello Sbirro2")]
    private static void Run()
    {
        if (!TryReadBodyHeight(ReferencePrefabPath, out float referenceHeight))
        {
            Fail($"Non riesco a misurare il corpo dello Sbirro dentro {ReferencePrefabPath}.");
            return;
        }

        GameObject target = PrefabUtility.LoadPrefabContents(TargetPrefabPath);
        try
        {
            Animator animator = target.GetComponentInChildren<Animator>();
            Renderer body = animator != null ? FindBody(animator.gameObject) : null;
            if (body == null || !TryMeasure(new[] { body }, out Bounds bodyBounds)
                || !TryMeasure(animator.GetComponentsInChildren<Renderer>(), out Bounds before))
            {
                Fail($"Non riesco a misurare il corpo dello Sbirro dentro {TargetPrefabPath}.");
                return;
            }

            Transform model = animator.transform;
            float scaleFactor = referenceHeight / bodyBounds.size.y;

            // Fra Sbirro dello stesso stile ci si aspetta un ritocco, non un cambio di taglia: un
            // fattore grosso vuol dire più facilmente una misura sbagliata che uno Sbirro3 minuscolo.
            if ((scaleFactor < 0.5f || scaleFactor > 2f) && !EditorUtility.DisplayDialog("Sbirro3",
                    $"Corpo Sbirro2 alto {referenceHeight:0.##}, corpo Sbirro3 alto {bodyBounds.size.y:0.##}: " +
                    $"andrebbe scalato x{scaleFactor:0.##}. Sembra troppo, applico lo stesso?", "Applica", "Annulla"))
            {
                return;
            }

            model.localScale *= scaleFactor;

            // I piedi restano dove erano: si riallinea il fondo dell'ingombro.
            TryMeasure(animator.GetComponentsInChildren<Renderer>(), out Bounds after);
            model.position += Vector3.up * (before.min.y - after.min.y);

            PrefabUtility.SaveAsPrefabAsset(target, TargetPrefabPath);
            Debug.Log(
                $"[Sbirro3] Corpo portato da {bodyBounds.size.y:0.##} a {referenceHeight:0.##} (quello dello Sbirro2), " +
                $"scala del modello x{scaleFactor:0.###}.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(target);
        }
    }

    // Altezza in unità di mondo del corpo dello Sbirro dentro un prefab, compresa la scala della radice.
    private static bool TryReadBodyHeight(string prefabPath, out float height)
    {
        height = 0f;
        GameObject source = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Animator animator = source.GetComponentInChildren<Animator>();
            Renderer body = animator != null ? FindBody(animator.gameObject) : null;
            if (body == null || !TryMeasure(new[] { body }, out Bounds bounds))
            {
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

    private static Renderer FindBody(GameObject model)
    {
        // Il corpo è la mesh legata a più ossa: segue tutto lo scheletro, mentre arma, scudo e
        // accessori seguono al massimo una mano. Il numero di vertici non basta: il lanciagranate
        // dello Sbirro3 ne ha più del maialino, e scambiandolo per il corpo lo Sbirro3 veniva
        // ingrandito di due volte e mezza.
        return model.GetComponentsInChildren<Renderer>()
            .Where(renderer => MeshOf(renderer) != null)
            .OrderByDescending(renderer => renderer is SkinnedMeshRenderer skinned ? skinned.bones.Length : 0)
            .ThenByDescending(renderer => MeshOf(renderer).vertexCount)
            .FirstOrDefault();
    }

    private static Mesh MeshOf(Renderer renderer)
    {
        return renderer is SkinnedMeshRenderer skinned
            ? skinned.sharedMesh
            : renderer.GetComponent<MeshFilter>()?.sharedMesh;
    }

    // Ingombro in mondo dei vertici, dove Unity li disegna davvero. Per una mesh skinnata NON
    // basta la trasformazione del renderer (come fa NewSbirroModelSetup.TryMeasure): i vertici li
    // piazzano le ossa, e le bindpose possono contenere una scala (l'armatura di Blender esportata
    // a 0.01/100) che il renderer non vede. Con quella misura lo Sbirro3 veniva ingrandito di quasi
    // tre volte. Qui ogni vertice passa da osso × bindpose, pesato come nello skinning.
    // Lo scheletro è quello del prefab a riposo (nessun Animator gira in LoadPrefabContents).
    private static bool TryMeasure(Renderer[] renderers, out Bounds bounds)
    {
        bounds = default;
        bool found = false;

        foreach (Renderer renderer in renderers)
        {
            Mesh mesh = MeshOf(renderer);
            if (mesh == null || mesh.vertexCount == 0)
            {
                continue;
            }

            Vector3[] vertices = mesh.vertices;
            SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
            Transform[] bones = skinned != null ? skinned.bones : null;
            BoneWeight[] weights = mesh.boneWeights;
            Matrix4x4[] bindposes = mesh.bindposes;
            bool useBones = bones != null && bones.Length > 0 && bones.Length == bindposes.Length
                && weights.Length == vertices.Length && bones.All(bone => bone != null);

            Matrix4x4[] skin = null;
            if (useBones)
            {
                skin = new Matrix4x4[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                {
                    skin[i] = bones[i].localToWorldMatrix * bindposes[i];
                }
            }

            Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
            for (int v = 0; v < vertices.Length; v++)
            {
                Vector3 world;
                if (useBones)
                {
                    BoneWeight w = weights[v];
                    world = skin[w.boneIndex0].MultiplyPoint3x4(vertices[v]) * w.weight0
                          + skin[w.boneIndex1].MultiplyPoint3x4(vertices[v]) * w.weight1
                          + skin[w.boneIndex2].MultiplyPoint3x4(vertices[v]) * w.weight2
                          + skin[w.boneIndex3].MultiplyPoint3x4(vertices[v]) * w.weight3;
                }
                else
                {
                    world = toWorld.MultiplyPoint3x4(vertices[v]);
                }

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
        EditorUtility.DisplayDialog("Sbirro3", message, "OK");
        Debug.LogError($"[Sbirro3] {message}");
    }
}
