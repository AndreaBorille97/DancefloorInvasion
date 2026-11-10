using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

// Torna al menu principale ricaricando la scena di gioco: il menu della modalità (DifficultyMenu)
// riparte da solo con la scena.
//
// Ricaricare non basta: tutto lo stato statico (GameOverState, RoutState, DefeatSequence,
// GameDifficulty...) sopravvive al reload, e i componenti che si aggiungono da soli al Player e
// agli obiettivi lo fanno con [RuntimeInitializeOnLoadMethod(AfterSceneLoad)], che Unity chiama
// una volta sola all'avvio, non ad ogni scena caricata. Qui si richiamano a mano, cercandoli per
// attributo: i reset (SubsystemRegistration e BeforeSceneLoad) prima di caricare, gli agganci
// (AfterSceneLoad) subito dopo. Così ogni nuovo reset o aggancio aggiunto in futuro funziona da sé anche qui.
public static class SceneRestart
{
    private static bool reloading;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        reloading = false;
    }

    public static void ReloadToMenu()
    {
        if (reloading)
        {
            return;
        }

        reloading = true;
        Invoke(RuntimeInitializeLoadType.SubsystemRegistration);
        Invoke(RuntimeInitializeLoadType.BeforeSceneLoad);
        reloading = true; // il reset qui sopra comprende anche il nostro
        Time.timeScale = 1f;
        SceneManager.sceneLoaded += OnSceneLoaded;

        Scene scene = SceneManager.GetActiveScene();
#if UNITY_EDITOR
        // Nell'Editor la scena può non stare nelle Build Settings (GameplayV2 non c'è): la si
        // ricarica per percorso.
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
        SceneManager.LoadScene(scene.buildIndex);
#endif
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        reloading = false;
        Invoke(RuntimeInitializeLoadType.AfterSceneLoad);
    }

    private static void Invoke(RuntimeInitializeLoadType loadType)
    {
        foreach (Type type in typeof(SceneRestart).Assembly.GetTypes())
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                RuntimeInitializeOnLoadMethodAttribute attribute = method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
                if (attribute != null && attribute.loadType == loadType && method.GetParameters().Length == 0)
                {
                    method.Invoke(null, null);
                }
            }
        }
    }
}
