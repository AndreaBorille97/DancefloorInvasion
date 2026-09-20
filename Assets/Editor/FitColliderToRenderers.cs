using UnityEditor;
using UnityEngine;

// Utility per ricreare un collider fisico su un oggetto la cui geometria di gioco (i vecchi
// "muro1..muro4" del sound system, ognuno con un BoxCollider) è stata sostituita da un
// modello puramente visivo (Colonna4.fbx, senza alcun collider, vedi Colonna4PrefabBuilder):
// senza un Collider nei figli, EnemyChase.FindNearestCollider non trova nulla e punta dritto
// al Transform (il pivot, sepolto dentro il modello) invece che alla sua superficie -> il
// nemico compenetra il modello cercando di raggiungerne il centro. In più EnemyAttack, basato
// su OnCollisionEnter/Stay, non rileva mai il contatto senza un vero Collider non-trigger.
//
// Seleziona in Hierarchy il GameObject radice del bersaglio (es. "sound system") e lancia
// "Tools/Fit Collider To Renderers": calcola i bounds mondiali combinati di tutti i Renderer
// nei figli e aggiunge (o ridimensiona, se già presente) un BoxCollider sulla radice per farli
// coincidere, gestendo correttamente scala e rotazione del Transform.
public static class FitColliderToRenderers
{
    [MenuItem("Tools/Fit Collider To Renderers")]
    private static void Fit()
    {
        GameObject target = Selection.activeGameObject;
        if (target == null)
        {
            EditorUtility.DisplayDialog("Fit Collider To Renderers", "Seleziona un GameObject in Hierarchy.", "OK");
            return;
        }

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            EditorUtility.DisplayDialog("Fit Collider To Renderers", $"Nessun Renderer trovato sotto '{target.name}'.", "OK");
            return;
        }

        Bounds worldBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            worldBounds.Encapsulate(renderers[i].bounds);
        }

        // I bounds sono un AABB in world space: per riportarli in local space della radice
        // (dove BoxCollider.center/size vanno espressi) trasformiamo tutti gli 8 vertici, non
        // solo centro/size, altrimenti una rotazione non identitaria del Transform darebbe un
        // box sbagliato.
        Transform t = target.transform;
        Vector3 localMin = Vector3.positiveInfinity;
        Vector3 localMax = Vector3.negativeInfinity;
        Vector3 c = worldBounds.center;
        Vector3 e = worldBounds.extents;
        for (int xi = -1; xi <= 1; xi += 2)
        {
            for (int yi = -1; yi <= 1; yi += 2)
            {
                for (int zi = -1; zi <= 1; zi += 2)
                {
                    Vector3 worldCorner = c + new Vector3(e.x * xi, e.y * yi, e.z * zi);
                    Vector3 localCorner = t.InverseTransformPoint(worldCorner);
                    localMin = Vector3.Min(localMin, localCorner);
                    localMax = Vector3.Max(localMax, localCorner);
                }
            }
        }

        BoxCollider box = target.GetComponent<BoxCollider>();
        if (box == null)
        {
            box = Undo.AddComponent<BoxCollider>(target);
        }
        else
        {
            Undo.RecordObject(box, "Fit Collider To Renderers");
        }

        box.center = (localMin + localMax) * 0.5f;
        box.size = localMax - localMin;

        EditorUtility.SetDirty(target);
        Debug.Log($"[FitColliderToRenderers] BoxCollider su '{target.name}': center={box.center}, size={box.size} (da {renderers.Length} Renderer).");
    }
}
