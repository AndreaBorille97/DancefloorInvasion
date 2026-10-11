using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Piazza un fuocherello cartoon nella scena aperta: Tools/Fuoco/Crea un fuocherello.
//
// Il fuoco è fatto di tre pezzi, ognuno che fa una cosa sola:
//  - "Fiamma": un Quad con il materiale FuocoCartoon (vedi Assets/Shaders/FuocoCartoon.shader),
//    cioè il disegno vero e proprio;
//  - "Luce": una point light calda, che è quello che il fuoco fa alla scena intorno;
//  - il componente CartoonFire sulla radice, che fa tremolare la luce e sputa le scintille.
//
// Dove lo mette: sopra l'oggetto selezionato, se ne hai uno selezionato; altrimenti al centro di
// quello che stai guardando in Scene view. In tutti e due i casi lo appoggia a terra da sé,
// cercando il pavimento sotto quel punto — così non resta mai sospeso a mezz'aria.
//
// A differenza del mare, qui si possono fare più fuochi: ogni volta ne nasce uno nuovo, e ognuno
// tremola per conto suo (CartoonFire si dà uno sfasamento di partenza a caso, altrimenti due
// fuochi vicini pulserebbero all'unisono e si vedrebbe che è finto).
public static class CartoonFireBuilder
{
    private const string ShaderName = "DancefloorInvasion/Fuoco Cartoon";
    private const string MaterialPath = "Assets/Material/FuocoCartoon.mat";

    // Quanto è alto il fuocherello, in metri. Piccolo: è un fuoco da spiaggia, non un rogo.
    private const float FlameHeight = 1.1f;
    private const float FlameWidth = 1.0f;

    [MenuItem("Tools/Fuoco/Crea un fuocherello")]
    private static void Build()
    {
        Material material = LoadOrCreateMaterial();
        if (material == null)
        {
            return;
        }

        Vector3 position = ChoosePosition();

        GameObject fire = new GameObject("Fuocherello");
        fire.transform.position = position;
        Undo.RegisterCreatedObjectUndo(fire, "Crea un fuocherello");

        // La fiamma. Il Quad si porta dietro un MeshCollider che qui non serve: il fuoco è un
        // disegno, non deve fermare nessuno.
        GameObject flame = GameObject.CreatePrimitive(PrimitiveType.Quad);
        flame.name = "Fiamma";
        Object.DestroyImmediate(flame.GetComponent<MeshCollider>());
        flame.transform.SetParent(fire.transform, false);
        flame.transform.localScale = new Vector3(FlameWidth, FlameHeight, 1f);

        MeshRenderer renderer = flame.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        // La luce, un po' sopra la base: una fiamma illumina dal suo corpo, non dai piedi.
        GameObject lightObject = new GameObject("Luce");
        lightObject.transform.SetParent(fire.transform, false);
        lightObject.transform.localPosition = new Vector3(0f, FlameHeight * 0.45f, 0f);

        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.62f, 0.28f);
        light.intensity = 3.2f;
        light.range = 9f;
        light.shadows = LightShadows.None; // costa e non serve: la fiamma è piccola

        CreateSparkPoint(fire.transform);
        fire.AddComponent<CartoonFire>();

        Selection.activeGameObject = fire;
        EditorGUIUtility.PingObject(fire);
        EditorSceneManager.MarkSceneDirty(fire.scene);

        Debug.Log(
            $"[Fuoco] Fuocherello creato in {position}. Trascinalo dove vuoi: si porta dietro " +
            "fiamma, luce e scintille. I colori e la forma stanno sul materiale FuocoCartoon, " +
            "il tremolio e le scintille sul componente CartoonFire.", fire);
    }

    // Il punto da cui escono le scintille, come oggetto vero della scena e non creato a runtime:
    // così lo si può selezionare in Hierarchy e trascinare dove si vuole, e la posizione resta
    // anche uscendo dal Play. CartoonFire lo cerca per nome e ci monta sopra il sistema di
    // particelle, senza mai spostarlo.
    //
    // L'icona colorata serve a poterlo prendere anche in Scene view: un GameObject vuoto,
    // altrimenti, non ha niente da cliccare.
    private static GameObject CreateSparkPoint(Transform parent)
    {
        GameObject sparks = new GameObject(CartoonFire.SparkChildName);
        sparks.transform.SetParent(parent, false);
        sparks.transform.localPosition = new Vector3(0f, FlameHeight * 0.12f, 0f);

        Texture2D icon = EditorGUIUtility.IconContent("sv_label_5").image as Texture2D;
        if (icon != null)
        {
            EditorGUIUtility.SetIconForObject(sparks, icon);
        }

        return sparks;
    }

    // Per i fuochi creati prima che il punto scintille esistesse: seleziona il fuoco (o piu di
    // uno) e lancia questo, invece di rifarli da capo perdendo le regolazioni.
    [MenuItem("Tools/Fuoco/Aggiungi il punto scintille ai fuochi selezionati")]
    private static void AddSparkPoint()
    {
        int added = 0;
        foreach (GameObject selected in Selection.gameObjects)
        {
            CartoonFire fire = selected.GetComponent<CartoonFire>();
            if (fire == null || selected.transform.Find(CartoonFire.SparkChildName) != null)
            {
                continue;
            }

            GameObject sparks = CreateSparkPoint(selected.transform);
            Undo.RegisterCreatedObjectUndo(sparks, "Aggiungi il punto scintille");
            EditorSceneManager.MarkSceneDirty(selected.scene);
            added++;
        }

        EditorUtility.DisplayDialog(
            "Fuoco cartoon",
            added > 0
                ? $"Punto scintille aggiunto a {added} fuoco/fuochi. Ora lo trovi in Hierarchy sotto il fuoco, come figlio \"{CartoonFire.SparkChildName}\": trascinalo dove vuoi."
                : "Nessun fuoco da sistemare fra quelli selezionati: o non hanno CartoonFire, o il punto scintille ce l'hanno gia.",
            "OK");
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
                "Fuoco cartoon",
                $"Shader \"{ShaderName}\" non trovato. Controlla che Assets/Shaders/FuocoCartoon.shader " +
                "sia stato importato senza errori di compilazione.",
                "OK");
            return null;
        }

        Material material = new Material(shader);
        AssetDatabase.CreateAsset(material, MaterialPath);
        AssetDatabase.SaveAssets();
        return material;
    }

    // Sopra l'oggetto selezionato, o al centro della vista. Poi si cerca il pavimento sotto quel
    // punto e ci si appoggia sopra: un fuoco sospeso a mezz'aria si nota subito.
    private static Vector3 ChoosePosition()
    {
        Vector3 position = Selection.activeGameObject != null
            ? Selection.activeGameObject.transform.position
            : (SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero);

        // Si parte da bene in alto e si guarda in giù: così si trova il pavimento anche se il
        // punto di partenza era già sotto terra.
        if (Physics.Raycast(position + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 200f))
        {
            position.y = hit.point.y;
        }

        return position;
    }
}
