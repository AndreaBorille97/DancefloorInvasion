using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Cambia l'ora del giorno della scena aperta, e sa sempre tornare indietro:
//
//   Tools/Scena/Tramonto rosso
//   Tools/Scena/Crepuscolo (ora blu)
//   Tools/Scena/Ripristina illuminazione
//
// I comandi si possono dare in qualsiasi ordine e quante volte si vuole: i valori di partenza
// vengono messi da parte la prima volta e non si sovrascrivono più, quindi "Ripristina" riporta
// sempre alla scena di partenza, non all'ora provata prima.
//
// Tocca solo l'illuminazione d'ambiente: il sole, il cielo e la foschia. Le luci piazzate in giro
// per la festa (LED delle casse, lanterne, faretti, fuochi) non vengono sfiorate, ed è tutto il
// punto: è la festa che deve restare accesa mentre intorno cambia l'ora.
//
// ── Perché queste ore e non il giorno pieno ───────────────────────────────────────────────────
// Quello che decide se una luce "si vede" non è quanto è forte, ma quanto è forte RISPETTO a
// quello che ha intorno. Un LED a emissione 16 in pieno giorno, con l'ambiente a 1, è quindici
// volte il fondo: si vede, ma il fondo è comunque chiaro. Di notte fonda lo stesso LED è mille
// volte il fondo, e la scena diventa due lampadine sospese nel nero — le luci vincono, ma non si
// vede più niente di quello che illuminano. Tramonto e crepuscolo stanno nel mezzo, ed è l'unica
// fascia in cui si ottengono tutte e due le cose insieme.
//
// ── Come si ottiene ──────────────────────────────────────────────────────────────────────────
// La scena prende la luce diffusa dal cielo (Ambient Mode: Skybox). Non è un dettaglio da
// aggirare, è la cosa più comoda che ci sia: basta mettere il cielo giusto e TUTTA la scena si
// tinge da sé. Quindi qui non si sceglie a mano il colore dell'ambiente — si cambia il cielo e lo
// si lascia seguire.
//
// Il cielo è quello procedurale di Unity con l'atmosfera resa più spessa: più è spessa, più la
// luce si sparpaglia attraversandola, più il blu si perde per strada e il rosso resta. È
// esattamente il motivo per cui i tramonti sono rossi, e qui funziona per lo stesso motivo.
//
// L'inclinazione del sole conta doppio, perché il cielo procedurale disegna il chiarore attorno a
// dove sta il sole: è l'inclinazione a decidere dove si accende l'orizzonte. L'orientamento sul
// piano — da che parte arriva la luce — resta quello scelto in scena e non viene toccato.
//
// ── Aggiungere un'altra ora ──────────────────────────────────────────────────────────────────
// Si copia un Preset qui sotto, si cambiano i numeri e si aggiunge un [MenuItem] che chiama
// Applica(). Salvataggio, ripristino e materiale del cielo sono già a posto per tutti.
public static class SceneLightingModes
{
    // Stessa chiave dei comandi precedenti (Notte fonda, Alba, Crepuscolo): i valori di partenza
    // messi da parte allora sono ancora quelli buoni, e vanno ripristinati da lì.
    private const string StateKey = "DancefloorInvasion.IlluminazioneDiGiorno";

    // Il sole appena sopra l'orizzonte: la luce diretta arriva radente e rossa, e il disco solare
    // si vede posato sul mare. Sopra i 6-7 gradi non è più un tramonto, è un pomeriggio caldo.
    private static readonly Preset Tramonto = new Preset
    {
        nome = "Tramonto rosso",
        cieloPath = "Assets/Material/CieloTramonto.mat",

        elevazioneSole = 2.5f,
        intensitaSole = 0.85f,
        coloreSole = new Color(1f, 0.42f, 0.2f),

        // Quanto la scena prende dal cielo. È QUESTA la manopola del "si vede qualcosa": alzala se
        // il terreno è troppo scuro, abbassala se le luci della festa non risaltano abbastanza.
        intensitaAmbiente = 1f,
        intensitaRiflessi = 0.7f,

        // Atmosfera molto spessa: è lei a togliere il blu e lasciare il rosso. Sopra 4 il cielo
        // vira al marrone e si spegne.
        spessoreAtmosfera = 3.4f,
        tintaCielo = new Color(1f, 0.45f, 0.33f),
        coloreTerreno = new Color(0.12f, 0.05f, 0.04f),
        esposizioneCielo = 1.1f,
        discoSolare = DiscoSolare.Grande,

        // La foschia calda è metà del tramonto: è quella che allontana il fondo e lo tinge di
        // rosso mentre la festa in primo piano resta pulita. Se il mare sparisce troppo presto,
        // abbassa densitaFoschia.
        foschia = true,
        coloreFoschia = new Color(0.52f, 0.22f, 0.17f),
        densitaFoschia = 0.0035f,
    };

    // L'ora blu, quella prima che il sole spunti. Il sole va appena SOTTO l'orizzonte: niente più
    // luce diretta (è quella che schiaccerebbe tutto), ma il cielo continua a disegnare il chiarore
    // attorno a dove sta per spuntare. Resta un filo di direzionale freddo per non perdere del
    // tutto il rilievo delle cose.
    private static readonly Preset Crepuscolo = new Preset
    {
        nome = "Crepuscolo (ora blu)",
        cieloPath = "Assets/Material/CieloCrepuscolo.mat",

        elevazioneSole = -1.5f,
        intensitaSole = 0.18f,
        coloreSole = new Color(0.62f, 0.7f, 1f),

        intensitaAmbiente = 1f,
        intensitaRiflessi = 0.35f,

        spessoreAtmosfera = 2.6f,
        tintaCielo = new Color(0.4f, 0.44f, 0.7f),
        coloreTerreno = new Color(0.05f, 0.05f, 0.09f),
        esposizioneCielo = 0.42f,
        discoSolare = DiscoSolare.Nessuno,   // il sole non è ancora spuntato

        foschia = false,
    };

    [MenuItem("Tools/Scena/Tramonto rosso", false, 0)]
    private static void ApplicaTramonto()
    {
        Applica(Tramonto);
    }

    [MenuItem("Tools/Scena/Crepuscolo (ora blu)", false, 1)]
    private static void ApplicaCrepuscolo()
    {
        Applica(Crepuscolo);
    }

    private enum DiscoSolare
    {
        Nessuno,
        Semplice,
        Grande,
    }

    private class Preset
    {
        public string nome;
        public string cieloPath;

        public float elevazioneSole;
        public float intensitaSole;
        public Color coloreSole;

        public float intensitaAmbiente;
        public float intensitaRiflessi;

        public float spessoreAtmosfera;
        public Color tintaCielo;
        public Color coloreTerreno;
        public float esposizioneCielo;
        public DiscoSolare discoSolare;

        public bool foschia;
        public Color coloreFoschia;
        public float densitaFoschia;
    }

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

        // I salvataggi fatti prima che esistesse la foschia non hanno questi campi: JsonUtility li
        // rilegge a zero, e ripristinarli così spegnerebbe una foschia che magari c'era. Il flag
        // dice se i valori qui sotto valgono davvero.
        public bool fogSaved;
        public bool fog;
        public Color fogColor;
        public int fogMode;
        public float fogDensity;
        public float fogStart;
        public float fogEnd;
    }

    private static void Applica(Preset preset)
    {
        Light[] suns = FindDirectionalLights();
        SavedLighting saved = Load();

        if (saved == null)
        {
            saved = Capture(suns);
            Store(saved);
        }
        else
        {
            // Salvataggi fatti dai comandi più vecchi, che non si tenevano tutto perché non
            // cambiavano tutto. Quello che manca si può ancora prendere adesso: se nessuno l'ha
            // mai toccato, il valore di adesso è ancora quello di partenza.
            bool aggiornato = false;

            if (saved.sunAngles.Count != saved.sunPaths.Count)
            {
                saved.sunAngles.Clear();
                foreach (string path in saved.sunPaths)
                {
                    Light sun = FindByPath(suns, path);
                    saved.sunAngles.Add(sun != null ? sun.transform.rotation.eulerAngles : Vector3.zero);
                }

                aggiornato = true;
            }

            if (!saved.fogSaved)
            {
                CaptureFog(saved);
                aggiornato = true;
            }

            if (aggiornato)
            {
                Store(saved);
            }
        }

        foreach (Light sun in suns)
        {
            Undo.RecordObject(sun.transform, preset.nome);
            Undo.RecordObject(sun, preset.nome);

            sun.intensity = preset.intensitaSole;
            sun.color = preset.coloreSole;

            // Solo l'inclinazione: da che parte arriva la luce resta una scelta della scena.
            Vector3 angles = sun.transform.rotation.eulerAngles;
            sun.transform.rotation = Quaternion.Euler(preset.elevazioneSole, angles.y, angles.z);
        }

        // La luce diffusa torna a seguire il cielo, così si tinge dell'ora da sé. Se la scena
        // arrivava dal vecchio comando "Notte fonda" era stata forzata a un colore fisso.
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = preset.intensitaAmbiente;
        RenderSettings.reflectionIntensity = preset.intensitaRiflessi;
        RenderSettings.skybox = CostruisciCielo(preset);

        RenderSettings.fog = preset.foschia;
        if (preset.foschia)
        {
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = preset.coloreFoschia;
            RenderSettings.fogDensity = preset.densitaFoschia;
        }

        Aggiorna();
        Debug.Log(
            "[Scena] " + preset.nome + ". Se il terreno è troppo scuro alza intensitaAmbiente, " +
            "se le luci della festa non risaltano abbassalo (SceneLightingModes). " +
            "Per tornare com'era: Tools/Scena/Ripristina illuminazione.");
    }

    [MenuItem("Tools/Scena/Ripristina illuminazione", false, 20)]
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

        if (saved.fogSaved)
        {
            RenderSettings.fog = saved.fog;
            RenderSettings.fogColor = saved.fogColor;
            RenderSettings.fogMode = (FogMode)saved.fogMode;
            RenderSettings.fogDensity = saved.fogDensity;
            RenderSettings.fogStartDistance = saved.fogStart;
            RenderSettings.fogEndDistance = saved.fogEnd;
        }

        EditorPrefs.DeleteKey(StateKey);
        Aggiorna();
        Debug.Log("[Scena] Illuminazione di partenza ripristinata.");
    }

    // Il cielo dell'ora scelta, come materiale a sé: così il cielo di partenza resta dov'è e il
    // ripristino non deve ricostruire niente. I valori vengono riscritti a ogni comando, quindi
    // per ritoccare un'ora basta cambiare i numeri del Preset e ridare il comando.
    private static Material CostruisciCielo(Preset preset)
    {
        Material sky = AssetDatabase.LoadAssetAtPath<Material>(preset.cieloPath);
        if (sky == null)
        {
            Shader shader = Shader.Find("Skybox/Procedural");
            if (shader == null)
            {
                Debug.LogWarning("[Scena] Shader Skybox/Procedural non trovato: lascio il cielo com'è.");
                return RenderSettings.skybox;
            }

            sky = new Material(shader);
            AssetDatabase.CreateAsset(sky, preset.cieloPath);
        }

        sky.SetFloat("_AtmosphereThickness", preset.spessoreAtmosfera);
        sky.SetColor("_SkyTint", preset.tintaCielo);
        sky.SetColor("_GroundColor", preset.coloreTerreno);
        sky.SetFloat("_Exposure", preset.esposizioneCielo);
        ImpostaDiscoSolare(sky, preset.discoSolare);

        EditorUtility.SetDirty(sky);
        AssetDatabase.SaveAssets();
        return sky;
    }

    // _SunDisk è una KeywordEnum: cambiare solo il float non basta, il ramo dello shader lo
    // decidono le keyword. Dall'inspector le tiene allineate Unity, da codice no.
    private static void ImpostaDiscoSolare(Material sky, DiscoSolare disco)
    {
        sky.SetFloat("_SunDisk", (int)disco);
        sky.DisableKeyword("_SUNDISK_NONE");
        sky.DisableKeyword("_SUNDISK_SIMPLE");
        sky.DisableKeyword("_SUNDISK_HIGH_QUALITY");

        switch (disco)
        {
            case DiscoSolare.Nessuno:
                sky.EnableKeyword("_SUNDISK_NONE");
                break;
            case DiscoSolare.Semplice:
                sky.EnableKeyword("_SUNDISK_SIMPLE");
                break;
            default:
                sky.EnableKeyword("_SUNDISK_HIGH_QUALITY");
                // Un sole basso sembra più grande: è l'unico momento in cui lo si può guardare, ed
                // è quello che lo fa leggere come tramonto invece che come puntino.
                sky.SetFloat("_SunSize", 0.06f);
                sky.SetFloat("_SunSizeConvergence", 3f);
                break;
        }
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

        CaptureFog(saved);

        foreach (Light sun in suns)
        {
            saved.sunPaths.Add(PathOf(sun.transform));
            saved.sunIntensities.Add(sun.intensity);
            saved.sunColors.Add(sun.color);
            saved.sunAngles.Add(sun.transform.rotation.eulerAngles);
        }

        return saved;
    }

    private static void CaptureFog(SavedLighting saved)
    {
        saved.fog = RenderSettings.fog;
        saved.fogColor = RenderSettings.fogColor;
        saved.fogMode = (int)RenderSettings.fogMode;
        saved.fogDensity = RenderSettings.fogDensity;
        saved.fogStart = RenderSettings.fogStartDistance;
        saved.fogEnd = RenderSettings.fogEndDistance;
        saved.fogSaved = true;
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

    private static void Aggiorna()
    {
        DynamicGI.UpdateEnvironment();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        SceneView.RepaintAll();
    }
}
