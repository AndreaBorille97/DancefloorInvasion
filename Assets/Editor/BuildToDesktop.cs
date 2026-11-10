using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Tools > Build su Desktop: build Windows 64 bit di GameplayV2 in Desktop\DancefloorInvasion.
// Usa la scena indicata qui e non le Build Settings, che contengono ancora SampleScene.
public static class BuildToDesktop
{
    private const string ScenePath = "Assets/Scenes/GameplayV2.unity";

    [MenuItem("Tools/Build su Desktop")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        RemoveBrokenReferences();

        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DancefloorInvasion");
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, true);
        }

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = Path.Combine(folder, "DancefloorInvasion.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });

        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"Build su Desktop completata: {folder}");
            EditorUtility.RevealInFinder(Path.Combine(folder, "DancefloorInvasion.exe"));
        }
        else
        {
            Debug.LogError($"Build su Desktop fallita: {report.summary.result}, {report.summary.totalErrors} errori. Vedi la Console.");
        }
    }

    // La build si ferma sui riferimenti rotti della scena: istanze di prefab il cui asset non c'è
    // più (i due "van", modello mai entrato nel progetto) e componenti con lo script cancellato
    // (es. su RaverPowerUpSpawner). In gioco non mostrano né fanno niente: si tolgono e si salva
    // la scena, scrivendo in Console cosa è stato tolto.
    private static void RemoveBrokenReferences()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int removedPrefabs = 0;
        int removedScripts = 0;

        List<GameObject> missingPrefabs = new List<GameObject>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                GameObject go = t.gameObject;
                bool parentMissing = t.parent != null && PrefabUtility.IsPrefabAssetMissing(t.parent.gameObject);
                if (PrefabUtility.IsPrefabAssetMissing(go) && !parentMissing)
                {
                    missingPrefabs.Add(go);
                }
            }
        }

        foreach (GameObject go in missingPrefabs)
        {
            Debug.Log($"[BuildToDesktop] Tolto '{go.name}': il suo prefab non esiste più.");
            UnityEngine.Object.DestroyImmediate(go);
            removedPrefabs++;
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                int count = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                if (count > 0)
                {
                    Debug.Log($"[BuildToDesktop] Tolti {count} script mancanti da '{t.name}'.");
                    removedScripts += count;
                }
            }
        }

        if (removedPrefabs + removedScripts > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
