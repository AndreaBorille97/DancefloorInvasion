using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Utility: rifà il prefab di una munizione mettendoci dentro un modello vero al posto del solido
// segnaposto (la sfera dello Speed, il cubo schiacciato del Camper), adattandolo da solo —
// taglia, verso, centratura, collider — e lasciandogli addosso CollectibleShine, cioè il contorno
// da collectible (sollevata da terra, gira, ondeggia, scintilla).
//
// Le munizioni da sistemare stanno nella tabella Jobs qui sotto: aggiungerne una vuol dire
// aggiungere una riga e una voce di menu, non riscrivere la procedura.
//
// Il prefab viene modificato sul posto, non ricreato: così gli spawner in scena continuano a
// puntare allo stesso file e non c'è niente da ricollegare. Si passa dall'editor perché è Unity a
// dover risolvere i fileID interni dei modelli (mesh e materiali): scriverli a mano nello YAML del
// prefab vuol dire indovinarli, e sbagliarli significa prefab rotto.
//
// È ripetibile: rilanciarla rifà il modello da capo senza accumulare figli, quindi è anche il modo
// di riagganciare un modello che è stato sostituito con uno nuovo.
public static class AmmoModelBuilder
{
    private const string ModelChildName = "Modello";

    // Come si capisce da che parte sta in piedi un modello. Sono due regole diverse perché due
    // sono le forme: una placca (il teschio) e un oggetto allungato (la tanica).
    private enum Upright
    {
        // Lo spessore in orizzontale: di una placca il lato più corto è lo spessore, e una placca
        // in piedi ce l'ha rivolto verso chi guarda. Non dice niente su quale degli altri due lati
        // vada in alto, il che va benissimo per una placca più o meno quadrata.
        ThinSideHorizontal,

        // Il lato più lungo in verticale: una tanica, una bottiglia, un bidone sono più alti che
        // larghi, quindi il lato lungo è l'altezza e basta portarlo in piedi.
        LongestSideUp,
    }

    // Una munizione da vestire: il prefab, dove sta il suo modello, quanto grande lo si vuole, come
    // si capisce il suo verso, e — se ne ha più d'uno — le cartelle delle varianti di colore fra
    // cui sorteggiare in partita.
    private class Job
    {
        public string PrefabPath;
        // Cartella (si prende il modello che c'è dentro, qualunque sia il nome del file: utile
        // quando il modello viene risostituito) oppure percorso diretto di un modello o prefab.
        public string ModelSource;
        public float TargetSize;
        public Upright StandUp = Upright.ThinSideHorizontal;
        // Vuoto = la munizione ha un aspetto solo e si tengono i materiali del modello.
        public string[] VariantFolders = new string[0];
    }

    private static readonly Job SpeedAmmo = new Job
    {
        PrefabPath = "Assets/Prefabs/SpeedUpPowerUpAmmo.prefab",
        ModelSource = "Assets/Prefabs/models/blue_punisher",
        // Un filo più grande della sfera di prima (che era 1 di diametro): si nota di più da
        // lontano senza diventare un ostacolo.
        TargetSize = 1.3f,
        VariantFolders = new[]
        {
            "Assets/Prefabs/models/blue_punisher",
            "Assets/Prefabs/models/green_punisher",
            "Assets/Prefabs/models/pink_punisher",
        },
    };

    private static readonly Job CamperAmmo = new Job
    {
        PrefabPath = "Assets/Prefabs/CamperAmmo.prefab",
        ModelSource = "Assets/Prefabs/models/Tanica.prefab",
        TargetSize = 1.3f,
        StandUp = Upright.LongestSideUp,
    };

    [MenuItem("Tools/Rebuild Ammo Model/Speed (teschio, 3 varianti)")]
    private static void BuildSpeedAmmo() => Build(SpeedAmmo);

    [MenuItem("Tools/Rebuild Ammo Model/Camper (tanica)")]
    private static void BuildCamperAmmo() => Build(CamperAmmo);

    private static void Build(Job job)
    {
        GameObject modelAsset = LoadModel(job.ModelSource);
        if (modelAsset == null)
        {
            EditorUtility.DisplayDialog(
                "Rebuild Ammo Model",
                $"Nessun modello trovato in {job.ModelSource}. Se l'hai appena copiato, lascia prima che Unity lo importi.",
                "OK");
            return;
        }

        List<Material> variants = LoadVariantMaterials(job, out string missing);

        GameObject prefab = PrefabUtility.LoadPrefabContents(job.PrefabPath);
        if (prefab == null)
        {
            EditorUtility.DisplayDialog("Rebuild Ammo Model", $"Prefab non trovato: {job.PrefabPath}", "OK");
            return;
        }

        try
        {
            // La radice torna dritta e a scala 1. Il CamperAmmo aveva sia la scala schiacciata
            // (1.2, 0.16, 1.1) sia una rotazione sua, residui di quando il segnaposto era un cubo
            // ridotto a piastrella inclinata: un modello appeso lì sotto ne esce stirato e storto,
            // e storte sarebbero anche tutte le misure che facciamo qui (ingombro, verso,
            // collider), che ragionano in coordinate mondo. Non si perde niente: gli spawner
            // istanziano le munizioni con rotazione neutra (vedi CamperSpawner), quindi quella
            // rotazione in partita non c'era comunque.
            prefab.transform.localScale = Vector3.one;
            prefab.transform.rotation = Quaternion.identity;

            // Via il solido segnaposto dalla radice: il modello nuovo va appeso come figlio, così
            // la radice resta pulita con collider e script (com'è fatto anche il Camper).
            RemoveComponent<MeshFilter>(prefab);
            RemoveComponent<MeshRenderer>(prefab);

            // Un eventuale modello di un lancio precedente: si butta e si rifà, altrimenti
            // rilanciando l'utility i modelli si accumulerebbero uno dentro l'altro.
            Transform previous = prefab.transform.Find(ModelChildName);
            if (previous != null)
            {
                Object.DestroyImmediate(previous.gameObject);
            }

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, prefab.transform);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = ModelChildName;
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;

            FitModel(model, prefab.transform, job.TargetSize, job.StandUp);

            // Col colore di partenza si vede l'anteprima del prefab già giusta; in partita viene
            // comunque risorteggiato da RandomMaterialVariant. Se la munizione ha un aspetto solo,
            // i materiali del modello si lasciano come sono.
            if (variants.Count > 0)
            {
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterial = variants[0];
                }
            }

            FitCollider(prefab, model);

            // Il contorno da collectible: sollevata da terra, gira, ondeggia, scintilla.
            if (prefab.GetComponent<CollectibleShine>() == null)
            {
                prefab.AddComponent<CollectibleShine>();
            }

            if (variants.Count > 0)
            {
                RandomMaterialVariant picker = prefab.GetComponent<RandomMaterialVariant>();
                if (picker == null)
                {
                    picker = prefab.AddComponent<RandomMaterialVariant>();
                }
                AssignVariants(picker, variants);
            }

            PrefabUtility.SaveAsPrefabAsset(prefab, job.PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefab);
        }

        AssetDatabase.SaveAssets();

        string colors = variants.Count > 0 ? $", {variants.Count} varianti di colore collegate" : "";
        string warning = string.IsNullOrEmpty(missing) ? "" : $" Varianti non trovate: {missing}.";
        Debug.Log($"[AmmoModelBuilder] {job.PrefabPath} rifatto col modello \"{modelAsset.name}\"{colors}.{warning}");
    }

    // Il modello arriva dal generatore con scala, verso e centro suoi: lo si riporta a targetSize
    // sul lato più lungo, lo si rimette in piedi e lo si centra sul pivot del prefab, altrimenti
    // la munizione nascerebbe grande come una casa, coricata o spostata rispetto al suo collider.
    private static void FitModel(GameObject model, Transform root, float targetSize, Upright standUp)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return;
        }

        Bounds bounds = WorldBounds(renderers);

        float longestSide = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        if (longestSide > 0.0001f)
        {
            model.transform.localScale *= targetSize / longestSide;
        }

        StandUpright(model, renderers, standUp);

        // Ricalcolato dopo scalatura e rotazione: il centro si è spostato insieme alla mesh.
        bounds = WorldBounds(renderers);
        model.transform.position += root.position - bounds.center;
    }

    // Rimette in piedi il modello senza sapere niente di lui: si guarda solo com'è fatto il suo
    // ingombro, secondo il criterio scelto per quella munizione (vedi Upright).
    private static void StandUpright(GameObject model, Renderer[] renderers, Upright standUp)
    {
        Vector3 size = WorldBounds(renderers).size;

        if (standUp == Upright.LongestSideUp)
        {
            // Il lato più lungo va portato in verticale. Se è già lui il verticale non si tocca
            // niente; altrimenti lo si ribalta con una rotazione di 90° attorno all'asse che lo
            // porta in alto.
            if (size.x >= size.y && size.x >= size.z)
            {
                model.transform.Rotate(0f, 0f, 90f, Space.World); // il lato lungo era in X
            }
            else if (size.z >= size.x && size.z >= size.y)
            {
                model.transform.Rotate(-90f, 0f, 0f, Space.World); // il lato lungo era in Z
            }

            return;
        }

        // ThinSideHorizontal: se il lato corto è quello verticale il modello è sdraiato, e basta
        // ribaltarlo di 90° attorno a X per rimetterlo in piedi.
        if (size.y <= size.x && size.y <= size.z)
        {
            model.transform.Rotate(-90f, 0f, 0f, Space.World);
        }
    }

    // Il collider si riporta sull'ingombro vero del modello, qualunque forma abbia: senza, la
    // munizione si raccoglierebbe toccando il vuoto dove stava il segnaposto.
    private static void FitCollider(GameObject prefab, GameObject model)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return;
        }

        // In coordinate della radice, che è dove i collider esprimono centro e misure.
        Bounds world = WorldBounds(renderers);
        Vector3 center = prefab.transform.InverseTransformPoint(world.center);
        Vector3 size = prefab.transform.InverseTransformVector(world.size);
        size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));

        switch (prefab.GetComponent<Collider>())
        {
            case SphereCollider sphere:
                sphere.isTrigger = true;
                sphere.center = center;
                sphere.radius = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * 0.5f;
                break;

            case BoxCollider box:
                box.isTrigger = true;
                box.center = center;
                box.size = size;
                break;

            case CapsuleCollider capsule:
                capsule.isTrigger = true;
                capsule.center = center;
                capsule.radius = Mathf.Max(size.x, size.z) * 0.5f;
                capsule.height = size.y;
                break;
        }
    }

    private static Bounds WorldBounds(Renderer[] renderers)
    {
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds;
    }

    // source può essere una cartella (si prende il modello che c'è dentro, qualunque nome abbia)
    // o direttamente un FBX o un prefab.
    private static GameObject LoadModel(string source)
    {
        if (!AssetDatabase.IsValidFolder(source))
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(source);
        }

        foreach (string filter in new[] { "t:Model", "t:Prefab" })
        {
            foreach (string guid in AssetDatabase.FindAssets(filter, new[] { source }))
            {
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (model != null)
                {
                    return model;
                }
            }
        }

        return null;
    }

    // Il materiale di ogni variante si costruisce dalle texture della sua cartella, invece di
    // prendere quello del modello importato: nei modelli del generatore externalObjects è vuoto,
    // cioè Unity non ha rimappato i renderer sui materiali estratti, e quel materiale interno è
    // grigio spento. Le texture invece ci sono, e sono l'unica cosa che distingue le varianti.
    private static List<Material> LoadVariantMaterials(Job job, out string missing)
    {
        List<Material> variants = new List<Material>();
        List<string> notFound = new List<string>();

        foreach (string folder in job.VariantFolders)
        {
            Material material = BuildVariantMaterial(folder);
            if (material == null)
            {
                notFound.Add(Path.GetFileName(folder));
                continue;
            }

            variants.Add(material);
        }

        missing = string.Join(", ", notFound);
        return variants;
    }

    // Il materiale viene creato solo se non esiste già; se c'è, gli si riempiono le caselle.
    private static Material BuildVariantMaterial(string folder)
    {
        Texture2D albedo = FindTexture(folder, "_texture");
        if (albedo == null)
        {
            return null;
        }

        string materialsFolder = folder + "/Materials";
        if (!AssetDatabase.IsValidFolder(materialsFolder))
        {
            AssetDatabase.CreateFolder(folder, "Materials");
        }

        string materialPath = $"{materialsFolder}/{albedo.name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            // Lo shader del progetto è URP Lit, lo stesso dei materiali già estratti: se non si
            // trova è meglio fermarsi che creare un materiale rosa shocking da shader rotto.
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError($"[AmmoModelBuilder] Shader \"Universal Render Pipeline/Lit\" non trovato: " +
                               $"niente materiale per {folder}.");
                return null;
            }

            material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
        }

        material.SetTexture("_BaseMap", albedo);
        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", albedo); // alcuni shader leggono ancora il nome vecchio
        }

        Texture2D normal = FindTexture(folder, "_texture_normal");
        if (normal != null && MarkAsNormalMap(normal))
        {
            material.SetTexture("_BumpMap", normal);
            material.EnableKeyword("_NORMALMAP");
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    // Le texture si chiamano tutte "<modello>_texture", "<modello>_texture_normal" e così via:
    // si cerca per come finisce il nome, non per nome intero, che cambia da cartella a cartella.
    private static Texture2D FindTexture(string folder, string suffix)
    {
        if (!AssetDatabase.IsValidFolder(folder))
        {
            return null;
        }

        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path).EndsWith(suffix))
            {
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
        }

        return null;
    }

    // Una normal map va importata come tale, altrimenti Unity la tratta come un'immagine normale e
    // il risultato è un rilievo sbagliato (più il solito avviso in Console). Torna false se il
    // reimport non è andato a buon fine, così la casella resta vuota invece che sbagliata.
    private static bool MarkAsNormalMap(Texture2D texture)
    {
        string path = AssetDatabase.GetAssetPath(texture);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            return false;
        }

        if (importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }

        return true;
    }

    // Il campo delle varianti è privato (serializzato): da editor ci si arriva con SerializedObject.
    private static void AssignVariants(RandomMaterialVariant picker, List<Material> variants)
    {
        SerializedObject serialized = new SerializedObject(picker);
        SerializedProperty array = serialized.FindProperty("variants");
        array.arraySize = variants.Count;

        for (int i = 0; i < variants.Count; i++)
        {
            array.GetArrayElementAtIndex(i).objectReferenceValue = variants[i];
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void RemoveComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        if (component != null)
        {
            Object.DestroyImmediate(component, true);
        }
    }
}
