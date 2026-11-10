#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ApeironVFX))]
public class ApeironVFXEditor : Editor
{
    Editor matEditor;

    public override void OnInspectorGUI()
    {
        var fx = (ApeironVFX)target;
        DrawDefaultInspector();

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button(Application.isPlaying ? "Cast (test)" : "Cast (test) — enter Play mode", GUILayout.Height(28)))
                fx.Cast(fx.transform.position);
        }

        if (fx.material != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Effect look (material)", EditorStyles.boldLabel);
            CreateCachedEditor(fx.material, typeof(MaterialEditor), ref matEditor);
            var me = (MaterialEditor)matEditor;
            me.DrawHeader();
            me.OnInspectorGUI();
        }
    }

    [MenuItem("GameObject/Effects/Apeiron Area VFX", false, 10)]
    static void Create(MenuCommand cmd)
    {
        var shader = Shader.Find("Apeiron/AreaVFX");
        if (shader == null) { Debug.LogError("Apeiron/AreaVFX shader not found."); return; }
        const string path = "Assets/Resources/ApeironVFX.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
        var go = new GameObject("Apeiron VFX");
        GameObjectUtility.SetParentAndAlign(go, cmd.context as GameObject);
        var fx = go.AddComponent<ApeironVFX>();
        fx.material = mat;
        fx.testKey = KeyCode.Space;
        if (Camera.main) fx.cameraToShake = Camera.main.transform;
        Undo.RegisterCreatedObjectUndo(go, "Create Apeiron VFX");
        Selection.activeObject = go;
    }
}
#endif
