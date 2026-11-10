using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Piazza fari da palco a cono nella scena aperta: Tools/Luci/Crea fari a cono.
//
// Ogni faro è un solo GameObject con tre pezzi: il cono visibile (MeshRenderer con il materiale
// FascioLuce, vedi Assets/Shaders/FascioLuce.shader), una spot light per la pozza di luce a
// terra, e il componente StageLightBeam che tiene allineati i due e li anima.
//
// Dove li mette: uno per ogni oggetto selezionato, come figlio (così segue il traliccio o la
// lampada su cui l'hai messo), con i colori sparsi in modo uniforme sull'arcobaleno. Seleziona
// quindi tutte le lampade gialle sul traliccio e lancia il comando una volta sola. Senza
// selezione ne crea uno al centro della Scene view, 6 metri sopra.
//
// Puntano in giù e un po' verso la camera, cioè verso il pubblico: da lì li giri a mano.
public static class StageLightBeamBuilder
{
    private const string ShaderName = "DancefloorInvasion/Fascio di Luce";
    private const string MaterialPath = "Assets/Art/Materials/FascioLuce.mat";

    // Luminosità HDR del colore del fascio: sopra 1, così il Bloom lo prende.
    private const float BeamBrightness = 3f;

    [MenuItem("Tools/Luci/Crea fari a cono")]
    private static void Build()
    {
        Material material = LoadOrCreateMaterial();
        if (material == null)
        {
            return;
        }

        GameObject[] selected = Selection.gameObjects;
        int count = Mathf.Max(1, selected.Length);
        GameObject[] created = new GameObject[count];
        // Parte da una tinta a caso, così due lanci del comando non danno gli stessi colori.
        float firstHue = Random.value;

        for (int i = 0; i < count; i++)
        {
            Transform anchor = selected.Length > 0 ? selected[i].transform : null;
            Color color = Color.HSVToRGB(Mathf.Repeat(firstHue + (float)i / count, 1f), 1f, 1f) * BeamBrightness;
            created[i] = CreateBeam(anchor, material, color);
        }

        Selection.objects = created;
        EditorSceneManager.MarkSceneDirty(created[0].scene);
        Debug.Log(
            $"[Luci] Creati {count} fari a cono. Ruotali per mirare (il fascio esce lungo la Z blu). " +
            "Colore, apertura, lunghezza, oscillazione e ciclo dei colori sono sul componente " +
            "StageLightBeam; morbidezza, pulviscolo e intensità del fascio sul materiale FascioLuce.", created[0]);
    }

    private static GameObject CreateBeam(Transform anchor, Material material, Color color)
    {
        GameObject beam = new GameObject("Faro a cono");
        Undo.RegisterCreatedObjectUndo(beam, "Crea fari a cono");

        // Mai figlio diretto del SoundSystem: ogni suo figlio diretto è per lui una cassa che
        // spara onde sonore (vedi SoundSystem.Awake), e un faro si metterebbe a sparare.
        if (anchor != null && anchor.GetComponent<SoundSystem>() == null)
        {
            beam.transform.SetParent(anchor, false);
            beam.transform.localPosition = Vector3.zero;
        }
        else
        {
            beam.transform.position = anchor != null
                ? anchor.position
                : (SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero) + Vector3.up * 6f;
        }

        beam.transform.rotation = Quaternion.LookRotation(GetDefaultAim(beam.transform.position), Vector3.up);

        MeshRenderer renderer = beam.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        Light light = beam.AddComponent<Light>();
        light.type = LightType.Spot;
        light.shadows = LightShadows.None; // costa, e con più fari insieme non si nota

        StageLightBeam stageLight = beam.AddComponent<StageLightBeam>();
        SerializedObject serialized = new SerializedObject(stageLight);
        serialized.FindProperty("color").colorValue = color;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return beam;
    }

    // In giù e un po' verso la camera di gioco: i fari di un palco illuminano il pubblico.
    private static Vector3 GetDefaultAim(Vector3 from)
    {
        Camera camera = Camera.main;
        Vector3 towardCamera = camera != null ? camera.transform.position - from : Vector3.back;
        towardCamera.y = 0f;
        towardCamera = towardCamera.sqrMagnitude > 0.0001f ? towardCamera.normalized : Vector3.back;
        return (Vector3.down + towardCamera * 0.6f).normalized;
    }

    private static Material LoadOrCreateMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (existing != null)
        {
            return existing;
        }

        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            EditorUtility.DisplayDialog(
                "Fari a cono",
                $"Shader \"{ShaderName}\" non trovato. Controlla che Assets/Shaders/FascioLuce.shader " +
                "sia stato importato senza errori di compilazione.",
                "OK");
            return null;
        }

        Material material = new Material(shader);
        AssetDatabase.CreateAsset(material, MaterialPath);
        AssetDatabase.SaveAssets();
        return material;
    }
}
