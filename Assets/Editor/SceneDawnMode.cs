using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Porta la scena aperta all'alba, e sa tornare indietro: Tools/Scena/Alba e
// Tools/Scena/Ripristina illuminazione.
//
// Tocca solo l'illuminazione d'ambiente — il sole e il cielo — e NON le luci piazzate in giro
// per la festa (lanterne, LED, faretti, fuochi), che all'alba devono ancora essere accese: è
// quello il bello dell'ora, la festa che non è finita mentre il sole si alza.
//
// Prima di cambiare qualcosa, i valori di partenza vengono messi da parte (in EditorPrefs, non
// in un file dentro il progetto: è roba di prova, non merita un asset). Rilanciare "Alba" a
// scena già cambiata non li sovrascrive, quindi il ripristino resta sempre quello buono.
//
// ── Perché l'alba si fa col cielo e non con la luce d'ambiente ────────────────────────────────
// La scena prende la luce diffusa dal cielo (Ambient Mode: Skybox). Non è un dettaglio da
// aggirare, è la cosa più comoda che ci sia: basta mettere un cielo d'alba e TUTTA la scena si
// tinge da sé dei colori giusti, caldi dalla parte del sole e ancora freddi dall'altra. Quindi
// qui non si tocca affatto la luce d'ambiente — si cambia il cielo e la si lascia seguire.
//
// Il cielo d'alba è lo stesso cielo procedurale di Unity con l'atmosfera resa più spessa: più
// spessa è, più la luce si sparpaglia attraversandola, e più il rosso resta mentre il blu si
// perde. È esattamente il motivo per cui le albe sono rosse, e qui funziona per lo stesso motivo.
//
// Il sole poi va messo basso sull'orizzonte (pochi gradi) e virato al caldo. L'inclinazione
// conta doppio: il cielo procedurale disegna il chiarore attorno a dove sta il sole, quindi è
// l'inclinazione della luce a decidere dove si accende l'orizzonte. L'orientamento sul piano —
// da che parte arriva la luce — resta quello scelto in scena.
public static class SceneDawnMode
{
    // Stessa chiave del vecchio comando "Notte fonda": se la scena è rimasta a notte, i valori
    // di giorno sono ancora quelli buoni e vanno ripristinati da lì.
    private const string StateKey = "DancefloorInvasion.IlluminazioneDiGiorno";
    private const string DawnSkyboxPath = "Assets/Material/CieloAlba.mat";

    // Quanto è alto il sole sull'orizzonte, in gradi. Pochi: sopra i 15 non è più alba.
    private const float SunElevation = 6f;
    private const float SunIntensity = 0.7f;
    private static readonly Color SunColor = new Color(1f, 0.74f, 0.5f);

    [System.Serializable]
    private class SavedLighting
    {
        public List<string> sunPaths = new List<string>();
        public List<float> sunIntensities = new List<float>();
        public List<Color> sunColors = new List<Color>();
        public List<Vector3> sunAngles = new List<Vector3>();

        public int ambientMode;
        public Color ambientLight;
        public Color ambientSky;
        public Color ambientEquator;
        public Color ambientGround;
        public float ambientIntensity;
        public string skyboxPath;
        public float reflectionIntensity;
    }

    [MenuItem("Tools/Scena/Alba")]
    private static void Dawn()
    {
        Light[] suns = FindDirectionalLights();
        SavedLighting saved = Load();

        if (saved == null)
        {
            saved = Capture(suns);
            Store(saved);
        }
        else if (saved.sunAngles.Count != saved.sunPaths.Count)
        {
            // Salvataggio fatto dal vecchio comando "Notte fonda", che non si teneva
            // l'inclinazione perché non la cambiava. Qui invece serve, e si può ancora prendere:
            // il sole non è mai stato ruotato, quindi quella di adesso è ancora quella buona.
            saved.sunAngles.Clear();
            foreach (string path in saved.sunPaths)
            {
                Light sun = FindByPath(suns, path);
                saved.sunAngles.Add(sun != null ? sun.transform.rotation.eulerAngles : Vector3.zero);
            }
            Store(saved);
        }

        foreach (Light sun in suns)
        {
            Undo.RecordObject(sun.transform, "Alba");
            Undo.RecordObject(sun, "Alba");

            sun.intensity = SunIntensity;
            sun.color = SunColor;

            // Solo l'inclinazione: da che parte arriva la luce resta una scelta della scena.
            Vector3 angles = sun.transform.rotation.eulerAngles;
            sun.transform.rotation = Quaternion.Euler(SunElevation, angles.y, angles.z);
        }

        // La luce d'ambiente torna a seguire il cielo, così si tinge d'alba da sé. Se la scena
        // arrivava dal comando "Notte fonda" era stata forzata a un colore fisso: va rimessa.
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.reflectionIntensity = 1f;
        RenderSettings.skybox = LoadOrCreateDawnSkybox();

        Apply();
        Debug.Log("[Scena] Alba. Per tornare com'era: Tools/Scena/Ripristina illuminazione.");
    }

    [MenuItem("Tools/Scena/Ripristina illuminazione")]
    private static void Restore()
    {
        SavedLighting saved = Load();
        if (saved == null)
        {
            EditorUtility.DisplayDialog(
                "Illuminazione",
                "Non ho niente da ripristinare: l'illuminazione non è mai stata cambiata da questi comandi.",
                "OK");
            return;
        }

        Light[] suns = FindDirectionalLights();
        for (int i = 0; i < saved.sunPaths.Count; i++)
        {
            Light sun = FindByPath(suns, saved.sunPaths[i]);
            if (sun == null)
            {
                continue;
            }

            Undo.RecordObject(sun.transform, "Ripristina illuminazione");
            Undo.RecordObject(sun, "Ripristina illuminazione");

            sun.intensity = saved.sunIntensities[i];
            sun.color = saved.sunColors[i];
            if (i < saved.sunAngles.Count)
            {
                sun.transform.rotation = Quaternion.Euler(saved.sunAngles[i]);
            }
        }

        RenderSettings.ambientMode = (AmbientMode)saved.ambientMode;
        RenderSettings.ambientLight = saved.ambientLight;
        RenderSettings.ambientSkyColor = saved.ambientSky;
        RenderSettings.ambientEquatorColor = saved.ambientEquator;
        RenderSettings.ambientGroundColor = saved.ambientGround;
        RenderSettings.ambientIntensity = saved.ambientIntensity;
        RenderSettings.reflectionIntensity = saved.reflectionIntensity;
        RenderSettings.skybox = string.IsNullOrEmpty(saved.skyboxPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<Material>(saved.skyboxPath);

        EditorPrefs.DeleteKey(StateKey);
        Apply();
        Debug.Log("[Scena] Illuminazione di partenza ripristinata.");
    }

    // Il cielo d'alba: atmosfera spessa (è lei a mangiarsi il blu e lasciare il rosso), tinta
    // appena rosata e sole in vista, basso sull'orizzonte. Nasce come materiale a sé la prima
    // volta, così il cielo di partenza resta dov'è e il ripristino non deve ricostruire niente.
    private static Material LoadOrCreateDawnSkybox()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(DawnSkyboxPath);
        if (existing != null)
        {
            return existing;
        }

        Shader shader = Shader.Find("Skybox/Procedural");
        if (shader == null)
        {
            Debug.LogWarning("[Scena] Shader Skybox/Procedural non trovato: lascio il cielo com'è.");
            return RenderSettings.skybox;
        }

        Material sky = new Material(shader);
        sky.SetFloat("_SunDisk", 2f);             // il sole si vede, ed è il soggetto
        sky.SetFloat("_SunSize", 0.045f);
        sky.SetFloat("_AtmosphereThickness", 1.7f);
        sky.SetColor("_SkyTint", new Color(0.62f, 0.52f, 0.6f));
        sky.SetColor("_GroundColor", new Color(0.32f, 0.27f, 0.24f));
        sky.SetFloat("_Exposure", 0.95f);

        AssetDatabase.CreateAsset(sky, DawnSkyboxPath);
        AssetDatabase.SaveAssets();
        return sky;
    }

    private static SavedLighting Capture(Light[] suns)
    {
        SavedLighting saved = new SavedLighting
        {
            ambientMode = (int)RenderSettings.ambientMode,
            ambientLight = RenderSettings.ambientLight,
            ambientSky = RenderSettings.ambientSkyColor,
            ambientEquator = RenderSettings.ambientEquatorColor,
            ambientGround = RenderSettings.ambientGroundColor,
            ambientIntensity = RenderSettings.ambientIntensity,
            reflectionIntensity = RenderSettings.reflectionIntensity,
            skyboxPath = RenderSettings.skybox != null ? AssetDatabase.GetAssetPath(RenderSettings.skybox) : string.Empty,
        };

        foreach (Light sun in suns)
        {
            saved.sunPaths.Add(PathOf(sun.transform));
            saved.sunIntensities.Add(sun.intensity);
            saved.sunColors.Add(sun.color);
            saved.sunAngles.Add(sun.transform.rotation.eulerAngles);
        }

        return saved;
    }

    private static SavedLighting Load()
    {
        return EditorPrefs.HasKey(StateKey)
            ? JsonUtility.FromJson<SavedLighting>(EditorPrefs.GetString(StateKey))
            : null;
    }

    private static void Store(SavedLighting saved)
    {
        EditorPrefs.SetString(StateKey, JsonUtility.ToJson(saved));
    }

    private static Light FindByPath(Light[] lights, string path)
    {
        foreach (Light light in lights)
        {
            if (PathOf(light.transform) == path)
            {
                return light;
            }
        }

        return null;
    }

    private static Light[] FindDirectionalLights()
    {
        List<Light> suns = new List<Light>();
        foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            // Solo il "sole": le luci piazzate nella festa devono restare come sono.
            if (light.type == LightType.Directional)
            {
                suns.Add(light);
            }
        }

        return suns.ToArray();
    }

    private static string PathOf(Transform target)
    {
        string path = target.name;
        for (Transform parent = target.parent; parent != null; parent = parent.parent)
        {
            path = parent.name + "/" + path;
        }

        return path;
    }

    private static void Apply()
    {
        DynamicGI.UpdateEnvironment();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        SceneView.RepaintAll();
    }
}
