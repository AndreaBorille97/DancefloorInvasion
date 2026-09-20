using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Crea (o riposiziona) il mare cartoon nella scena aperta: Tools/Mare/Crea o aggiorna il mare.
//
// Il mare è un solo Quad sdraiato col materiale MareCartoon (vedi Assets/Shaders/MareCartoon.
// shader): niente mesh da generare, niente onde sui vertici. A questa distanza di camera uno
// spostamento verticale di qualche centimetro non si vedrebbe comunque, mentre quello che si
// vede — colore a fasce, schiuma sul bagnasciuga, ondine che arrivano, luccichii — lo fa tutto
// il fragment shader. Di conseguenza il mare non costa praticamente niente.
//
// Dove va piazzato non è scritto a mano: viene preso dalla scena. Il bordo a monte del piano
// (quello verso la spiaggia) viene allineato al muro sud dell'arena, cioè al bordo del cubo che
// ospita FloorBounds, e la quota è quella della sabbia visibile (l'oggetto Floor), più due dita
// per non sfarfallare contro il pavimento. Così se l'arena viene ridimensionata basta rilanciare
// il comando e il mare torna al posto giusto.
//
// Il comando è ripetibile: se in scena c'è già un oggetto "Mare" lo riposiziona invece di
// crearne un altro.
public static class MareCartoonBuilder
{
    private const string SeaObjectName = "Mare";
    private const string ShaderName = "DancefloorInvasion/Mare Cartoon";
    private const string MaterialPath = "Assets/Material/MareCartoon.mat";

    // Quanto è grande il piano d'acqua, in metri. Deve solo coprire quello che si vede: la
    // camera guarda in giù a 45 gradi da 13 metri, quindi non arriva mai oltre una cinquantina
    // di metri. Più di così è solo un rettangolo enorme che intralcia in Scene view.
    private const float SeaWidth = 300f;
    private const float SeaDepth = 150f;

    // Il piano non si ferma alla battigia: sale ancora un po' sulla spiaggia asciutta, perché
    // l'alone di sabbia bagnata e la risacca che avanza stanno a monte della linea dell'acqua, e
    // se la mesh finisse lì si vedrebbero tagliati di netto da un bordo dritto.
    private const float BeachMargin = 5f;

    // Dove sta in media la linea dell'acqua, a sud di dove finisce l'arena: qualche metro, così
    // il Player che arriva al muro invisibile resta sempre all'asciutto.
    private const float WaterlineFromArena = 4f;

    // Di quanto il piano viene tenuto sopra la sabbia: sono due superfici complanari, e senza un
    // minimo di stacco si vedrebbero i triangoli battere uno sull'altro.
    private const float SandClearance = 0.05f;

    [MenuItem("Tools/Mare/Crea o aggiorna il mare")]
    private static void Build()
    {
        FloorBounds arena = Object.FindAnyObjectByType<FloorBounds>();
        if (arena == null)
        {
            EditorUtility.DisplayDialog(
                "Mare cartoon",
                "Non trovo l'oggetto con FloorBounds (FloorBoundAnchors) nella scena aperta. " +
                "Apri GameplayV2 e riprova.",
                "OK");
            return;
        }

        if (!TryGetBounds(arena.gameObject, out Bounds arenaBounds))
        {
            EditorUtility.DisplayDialog(
                "Mare cartoon",
                $"L'oggetto {arena.name} non ha né Renderer né Collider: non riesco a capire dove " +
                "finisce l'arena.",
                "OK");
            return;
        }

        Material material = LoadOrCreateMaterial();
        if (material == null)
        {
            return;
        }

        GameObject sea = GameObject.Find(SeaObjectName);
        if (sea == null)
        {
            // CreatePrimitive per avere la mesh Quad di Unity senza doverla cercare tra gli
            // asset interni. Il MeshCollider che si porta dietro va tolto: il mare è
            // scenografia, non deve fermare né il Player né i nemici.
            sea = GameObject.CreatePrimitive(PrimitiveType.Quad);
            sea.name = SeaObjectName;
            Object.DestroyImmediate(sea.GetComponent<MeshCollider>());
            Undo.RegisterCreatedObjectUndo(sea, "Crea il mare");
        }
        else
        {
            Undo.RecordObject(sea.transform, "Aggiorna il mare");
        }

        // Il bordo a monte del piano entra di qualche metro nell'arena (è spiaggia asciutta:
        // trasparente, non si vede); la linea dell'acqua vera la disegna lo shader più a sud,
        // a _ShoreOffset metri da questo bordo.
        float planeEdgeZ = arenaBounds.min.z + BeachMargin;
        float waterY = FindSandTop(arenaBounds) + SandClearance;

        sea.transform.position = new Vector3(arenaBounds.center.x, waterY, planeEdgeZ - SeaDepth * 0.5f);
        sea.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // Quad sdraiato, lato +Y verso la spiaggia
        sea.transform.localScale = new Vector3(SeaWidth, SeaDepth, 1f);

        MeshRenderer renderer = sea.GetComponent<MeshRenderer>();
        Undo.RecordObject(renderer, "Aggiorna il mare");
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        EditorSceneManager.MarkSceneDirty(sea.scene);
        Selection.activeGameObject = sea;
        EditorGUIUtility.PingObject(sea);

        Debug.Log(
            $"[Mare] Piazzato a {sea.transform.position}, {SeaWidth}x{SeaDepth} metri. " +
            $"Linea dell'acqua attorno a z={planeEdgeZ - material.GetFloat("_ShoreOffset"):0.0} " +
            $"(l'arena finisce a z={arenaBounds.min.z:0.0}), pelo dell'acqua a y={waterY:0.00}. " +
            "I parametri di onde, schiuma e riflessi stanno sul materiale MareCartoon.",
            sea);
    }

    // Il materiale è un asset a sé (non una copia di quelli del pack ocean&lakeShaderPack):
    // così i parametri regolati per questa spiaggia restano qui e non toccano gli esempi.
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
                "Mare cartoon",
                $"Shader \"{ShaderName}\" non trovato. Controlla che Assets/Shaders/MareCartoon.shader " +
                "sia stato importato senza errori di compilazione.",
                "OK");
            return null;
        }

        Material material = new Material(shader);

        // L'unico parametro che dipende da come è piazzata la mesh, e quindi non può restare al
        // valore di default dello shader: la battigia va misurata dal bordo a monte del piano,
        // che sta BeachMargin metri dentro l'arena. Viene scritto solo alla creazione: se poi lo
        // sposti a mano, rilanciare il comando non te lo rimette a posto d'ufficio.
        material.SetFloat("_ShoreOffset", BeachMargin + WaterlineFromArena);

        AssetDatabase.CreateAsset(material, MaterialPath);
        AssetDatabase.SaveAssets();
        return material;
    }

    // La quota del pelo dell'acqua: la cima della sabbia che si vede, cioè il Renderer più largo
    // che copre l'arena (l'oggetto Floor). Se non lo trova ripiega sulla cima del cubo dell'arena,
    // che è alla stessa altezza a meno di qualche centimetro.
    private static float FindSandTop(Bounds arenaBounds)
    {
        float widest = 0f;
        float sandTop = arenaBounds.max.y;

        foreach (MeshRenderer renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            Bounds bounds = renderer.bounds;

            // Il pavimento è una lastra: larghissima e bassa. Ed è sotto i piedi di tutto il
            // resto, quindi deve contenere il centro dell'arena.
            bool flat = bounds.size.y < bounds.size.x * 0.25f && bounds.size.y < bounds.size.z * 0.25f;
            bool coversArena = bounds.Contains(new Vector3(arenaBounds.center.x, bounds.center.y, arenaBounds.center.z));
            float area = bounds.size.x * bounds.size.z;

            if (flat && coversArena && area > widest)
            {
                widest = area;
                sandTop = bounds.max.y;
            }
        }

        return sandTop;
    }

    private static bool TryGetBounds(GameObject target, out Bounds bounds)
    {
        Renderer renderer = target.GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            bounds = renderer.bounds;
            return true;
        }

        Collider collider = target.GetComponentInChildren<Collider>();
        if (collider != null)
        {
            bounds = collider.bounds;
            return true;
        }

        bounds = new Bounds(target.transform.position, Vector3.zero);
        return false;
    }
}
