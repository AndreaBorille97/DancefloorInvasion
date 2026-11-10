#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// Seleziona il robot → Tools > Sonic Double Wave > Configura robot
/// Aggiunge DualSpeakerEmitter + BerserkMode, crea Speaker_L / Speaker_R e collega i due prefab già inclusi.
public static class SonicDoubleWaveSetup
{
    const string Prefabs = "Assets/SonicDoubleWave/Prefabs/";

    [MenuItem("Tools/Sonic Double Wave/Configura robot")]
    static void Setup()
    {
        var normal = AssetDatabase.LoadAssetAtPath<SonicRingShot>(Prefabs + "SonicRingShot.prefab");
        var berserk = AssetDatabase.LoadAssetAtPath<SonicRingShot>(Prefabs + "SonicRingShot_Berserk.prefab");
        if (!normal || !berserk) { Debug.LogError("Prefab non trovati in " + Prefabs); return; }
        var robot = Selection.activeGameObject;
        if (!robot || PrefabUtility.IsPartOfPrefabAsset(robot)) { Debug.LogWarning("Seleziona il robot nella scena."); return; }

        var em = robot.GetComponent<DualSpeakerEmitter>() ?? Undo.AddComponent<DualSpeakerEmitter>(robot);
        em.ringPrefab = normal;
        if (!em.speakerLeft) em.speakerLeft = MakeSpeaker(robot.transform, "Speaker_L", -0.55f);
        if (!em.speakerRight) em.speakerRight = MakeSpeaker(robot.transform, "Speaker_R", 0.55f);
        var bz = robot.GetComponent<BerserkMode>() ?? Undo.AddComponent<BerserkMode>(robot);
        bz.berserkRingPrefab = berserk;
        EditorUtility.SetDirty(em); EditorUtility.SetDirty(bz);
        Debug.Log("Robot configurato. Sposta Speaker_L / Speaker_R sulle casse (asse Z = direzione di sparo).");
    }

    static Transform MakeSpeaker(Transform parent, string name, float x)
    {
        var t = new GameObject(name).transform;
        Undo.RegisterCreatedObjectUndo(t.gameObject, "Speaker");
        t.SetParent(parent, false);
        t.localPosition = new Vector3(x, 1.95f, 0.1f);
        t.localRotation = Quaternion.identity;
        return t;
    }
}
#endif
