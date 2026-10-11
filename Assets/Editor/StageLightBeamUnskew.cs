using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools/Luci/Raddrizza i fari a cono: toglie i fari a cono da sotto genitori con scala non
// uniforme (per esempio Floor, che è (202, -5.3, 214)) e li rimette in scena a scala 1.
//
// Perché serve: sotto una scala così il cono non è più un cono ma un ventaglio schiacciato, e
// qualunque rotazione gli si dia viene schiacciata anche lei. Il fascio che segue un faretto
// animato (StageLightBeam) inclina di 24° insieme al faretto, ma sotto Floor l'inclinazione
// verticale si riduce di ~40 volte e resta solo lo spostamento su e giù della lente.
//
// Il comando conserva quello che si vede adesso: stesso punto di partenza, stessa direzione
// visibile, stessa lunghezza e stessa larghezza in fondo, ricalcolate in metri veri e scritte in
// Lunghezza, Raggio all'attacco e Apertura. I fari finiscono sotto un oggetto "Fari a cono" in
// radice di scena. Si annulla con Ctrl+Z.
public static class StageLightBeamUnskew
{
    private const float UniformTolerance = 0.01f;

    [MenuItem("Tools/Luci/Raddrizza i fari a cono")]
    private static void Unskew()
    {
        StageLightBeam[] beams = Object.FindObjectsByType<StageLightBeam>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Transform container = null;
        int fixedCount = 0;

        foreach (StageLightBeam beam in beams)
        {
            Transform t = beam.transform;
            if (IsUniform(t.lossyScale))
            {
                continue;
            }

            SerializedObject so = new SerializedObject(beam);
            SerializedProperty lengthProp = so.FindProperty("length");
            SerializedProperty radiusProp = so.FindProperty("sourceRadius");
            SerializedProperty angleProp = so.FindProperty("angle");

            float length = lengthProp.floatValue;
            float sourceRadius = radiusProp.floatValue;
            float endRadius = sourceRadius + length * Mathf.Tan(angleProp.floatValue * 0.5f * Mathf.Deg2Rad);

            // Quello che si vede, in coordinate mondo: TransformVector tiene conto della scala
            // (anche della deformazione), TransformDirection no.
            Vector3 origin = t.position;
            Vector3 axisVector = t.TransformVector(Vector3.forward * length);
            float worldLength = axisVector.magnitude;
            Vector3 axis = axisVector / worldLength;
            float widthPerUnit = Mathf.Max(
                Vector3.ProjectOnPlane(t.TransformVector(Vector3.right), axis).magnitude,
                Vector3.ProjectOnPlane(t.TransformVector(Vector3.up), axis).magnitude);
            float worldSourceRadius = sourceRadius * widthPerUnit;
            float worldEndRadius = endRadius * widthPerUnit;
            float worldAngle = 2f * Mathf.Atan((worldEndRadius - worldSourceRadius) / worldLength) * Mathf.Rad2Deg;

            // L'alto del cono: quello di prima, raddrizzato perpendicolare all'asse.
            Vector3 up = Vector3.ProjectOnPlane(t.TransformDirection(Vector3.up), axis);
            if (up.sqrMagnitude < 1e-6f)
            {
                up = Vector3.ProjectOnPlane(Vector3.up, axis);
            }

            if (container == null)
            {
                container = GetOrCreateContainer(beam.gameObject.scene);
            }

            Undo.SetTransformParent(t, container, "Raddrizza i fari a cono");
            Undo.RecordObject(t, "Raddrizza i fari a cono");
            t.localScale = Vector3.one;
            t.SetPositionAndRotation(origin, Quaternion.LookRotation(axis, up));

            lengthProp.floatValue = worldLength;
            radiusProp.floatValue = worldSourceRadius;
            angleProp.floatValue = Mathf.Clamp(worldAngle, 2f, 90f);
            so.ApplyModifiedProperties();

            Debug.Log($"[Luci] {beam.name}: lunghezza {length:0.##} -> {worldLength:0.##} m, " +
                      $"raggio all'attacco {sourceRadius:0.###} -> {worldSourceRadius:0.###} m, " +
                      $"apertura {angleProp.floatValue:0.#}°.", beam);
            fixedCount++;
        }

        if (fixedCount == 0)
        {
            Debug.Log("[Luci] Nessun faro a cono da raddrizzare: sono già tutti a scala uniforme.");
            return;
        }

        EditorSceneManager.MarkSceneDirty(container.gameObject.scene);
        Debug.Log($"[Luci] Raddrizzati {fixedCount} fari a cono, ora sotto \"{container.name}\". " +
                  "Salva la scena se ti va bene come sono venuti.", container);
    }

    private static bool IsUniform(Vector3 scale)
    {
        return scale.x > 0f && scale.y > 0f && scale.z > 0f &&
               Mathf.Abs(scale.x - scale.y) < UniformTolerance * scale.x &&
               Mathf.Abs(scale.x - scale.z) < UniformTolerance * scale.x;
    }

    private static Transform GetOrCreateContainer(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "Fari a cono")
            {
                return root.transform;
            }
        }

        GameObject container = new GameObject("Fari a cono");
        Undo.RegisterCreatedObjectUndo(container, "Raddrizza i fari a cono");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(container, scene);
        return container.transform;
    }
}
