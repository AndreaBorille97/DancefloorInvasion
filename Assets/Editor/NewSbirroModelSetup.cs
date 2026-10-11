using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Sostituisce, dentro il prefab di uno Sbirro, il modello vecchio con quello nuovo fatto con
// Meshy e animato in Blender: Tools/Sbirro1/Monta il nuovo modello animato e
// Tools/Sbirro2/Monta il nuovo modello animato. Tutto il resto del prefab (EnemyChase,
// EnemyHealth, EnemyAttack, capsula di collisione, scala della radice) resta com'è, quindi lo
// Sbirro si comporta come prima: cambia solo quello che si vede.
//
// Come funziona:
//  - taglia e appoggio a terra si prendono MISURANDO il modello che c'è nel prefab prima di
//    toglierlo, così il nuovo viene alto uguale e con i piedi allo stesso punto, qualunque sia la
//    sua scala nativa;
//  - le clip del vecchio modello non vanno bene per il nuovo scheletro (rig Generic: il modello
//    si accartoccerebbe). Il nuovo FBX ha le sue "camminata" e "manganellata", e si usano quelle
//    in una copia del controller vecchio (stessi stati, stessa transizione, stesso parametro
//    "Attack" che fa scattare EnemyAttack);
//  - l'armatura resta chiamata come nell'FBX: le curve delle clip la cercano così.
//
// Il vecchio controller e il vecchio FBX non vengono toccati: per tornare indietro basta
// ripristinare il prefab.
//
// È ripetibile: rilanciarlo rifà il montaggio da capo senza accumulare figli.
public static class NewSbirroModelSetup
{
    // Quello che cambia da uno Sbirro all'altro.
    private class Setup
    {
        public string Tag;                    // per i messaggi in Console, es. "Sbirro1"
        public string PrefabPath;
        public string ModelPath;
        public string TemplateControllerPath; // il controller del modello vecchio, da copiare
        public string ControllerPath;
        public string TemplateMaterialPath;   // il materiale del corpo vecchio, da copiare
        public string MaterialPath;
        public string TexturePath;
        public string NormalMapPath;
        public string BatonMaterialPath;
        // Cartella con i materiali dell'equipaggiamento già sistemati per il modello vecchio
        // (scudo, acciaio, nylon...): un materiale del file con lo stesso nome prende quello.
        // Null = nessuno, tutto quello che non è manganello diventa corpo.
        public string EquipmentFolder;
        // Se non è null, l'altezza non si copia dal modello vecchio di questo prefab ma dal
        // modello di quest'altro prefab, moltiplicata per SizeFactor. Serve quando il modello vecchio
        // misurava male (vedi Sbirro2) e copiarne l'altezza darebbe uno Sbirro gigante.
        public string MatchHeightOfPrefab;
        public float SizeFactor = 1f;
        // Le clip del modello nuovo da usare per camminata e attacco: l'ultimo pezzo del nome
        // della clip (dopo l'ultima "|", es. "Armature|sparo" -> "sparo"). Gli stati del
        // controller copiato si riconoscono invece sempre da "camminata"/"manganellata".
        public string WalkClip = WalkMove;
        public string AttackClip = AttackMove;
        // Arma con texture sua (es. il lanciagranate dello Sbirro3): i materiali usati dalle mesh
        // sotto un nodo con WeaponNodeKeyword nel nome prendono una copia del materiale del corpo
        // con WeaponTexturePath. Null = niente arma separata.
        public string WeaponNodeKeyword;
        public string WeaponTexturePath;
        public string WeaponMaterialPath;
        // Schiarita dell'arma, stesso metodo del vecchio Sbirro2 (vedi Sbirro2ModelSetup):
        // WeaponBrightness moltiplica la texture (1 = com'è importata, 1.5 = 50% più chiara),
        // WeaponGlow aggiunge la texture stessa come emissione (0 = niente), un fondo di luce
        // propria che la stacca dal buio senza sbiancarla.
        public float WeaponBrightness = 1f;
        public float WeaponGlow = 0f;
        // Uno Sbirro che si ferma (lo Sbirro3 quando spara) non deve continuare a camminare sul
        // posto. Il file non ha una clip "fermo": se attivo, il controller riceve uno stato Idle
        // che tiene ferma la clip d'attacco al punto IdlePoseTime (0..1, 0 = primo fotogramma),
        // e un bool "Moving" che EnemyRangedAttack accende solo quando si muove davvero.
        public bool AddIdleFromAttackPose;
        public float IdlePoseTime = 0f;
    }

    private static readonly Setup Sbirro1 = new Setup
    {
        Tag = "Sbirro1",
        PrefabPath = "Assets/Prefabs/Sbirro1.prefab",
        ModelPath = "Assets/Prefabs/models/sbirro1/SBIRRO1_NEW_ANIMATO.fbx",
        TemplateControllerPath = "Assets/Prefabs/models/Animations/Sbirro1_Controller.controller",
        ControllerPath = "Assets/Prefabs/models/Animations/Sbirro1New_Controller.controller",
        TemplateMaterialPath = "Assets/Texture/Materials/sbirro_1_texture.mat",
        MaterialPath = "Assets/Texture/Materials/sbirro_1_new_texture.mat",
        TexturePath = "Assets/Prefabs/models/sbirro1/Meshy_AI_Pig_Cop_Mascot_0927134510_texture.png",
        NormalMapPath = "Assets/Prefabs/models/sbirro1/Meshy_AI_Pig_Cop_Mascot_0927134510_texture_normal.png",
        BatonMaterialPath = "Assets/Texture/Materials/sbirro_1_manganello.mat",
        EquipmentFolder = null,
    };

    // Il materiale del corpo parte da quello del vecchio Sbirro2, che è già schiarito e scaldato
    // per non spegnersi al crepuscolo (vedi Sbirro2ModelSetup): cambia solo la texture.
    // Scudo, fasce e rivetti riprendono i materiali estratti per il vecchio modello, che hanno gli
    // stessi nomi (Acciaio, Nylon, Plastica_Nera, Policarbonato).
    private static readonly Setup Sbirro2 = new Setup
    {
        Tag = "Sbirro2",
        PrefabPath = "Assets/Prefabs/Sbirro2.prefab",
        ModelPath = "Assets/Prefabs/models/sbirro2/SBIRRO2_NEW_ANIMATO.fbx",
        TemplateControllerPath = "Assets/Prefabs/models/Animations/Sbirro2_Controller.controller",
        ControllerPath = "Assets/Prefabs/models/Animations/Sbirro2New_Controller.controller",
        TemplateMaterialPath = "Assets/Texture/Materials/sbirro_2_texture.mat",
        MaterialPath = "Assets/Texture/Materials/sbirro_2_new_texture.mat",
        TexturePath = "Assets/Prefabs/models/sbirro2/Meshy_AI_Polizia_Pig_0927160433_texture.png",
        NormalMapPath = "Assets/Prefabs/models/sbirro2/Meshy_AI_Polizia_Pig_0927160433_texture_normal.png",
        BatonMaterialPath = "Assets/Texture/Materials/sbirro_2_manganello.mat",
        EquipmentFolder = "Assets/Material/Sbirro2",
        // Il vecchio Sbirro2 misurava 4 metri contro i 3 dello Sbirro1 (scudo e manganello ne
        // gonfiavano l'ingombro) e il nuovo, copiandone l'altezza, veniva enorme: si prende
        // invece quella dello Sbirro1. SizeFactor 1.1 = un 10% più alto dello Sbirro1.
        MatchHeightOfPrefab = "Assets/Prefabs/Sbirro1.prefab",
        SizeFactor = 1f,
    };

    // Lo Sbirro a distanza: prima del modello nuovo era una capsula con una sfera, senza Animator
    // (vedi RemovePlaceholder). Cammina col fucile e spara invece di manganellare, ma la logica
    // è la stessa: la copia del controller del nuovo Sbirro1 (camminata in loop, trigger
    // "Attack" per il colpo, tag "Attack" sullo stato), con le sue clip al posto di quelle.
    // Il trigger lo fa scattare EnemyRangedAttack a ogni fumogeno lanciato.
    // Nessuna normal map: Meshy ha dato solo le texture di colore di corpo e lanciagranate.
    private static readonly Setup Sbirro3 = new Setup
    {
        Tag = "Sbirro3",
        PrefabPath = "Assets/Prefabs/Sbirro3.prefab",
        ModelPath = "Assets/Prefabs/models/Sbirro3/SBIRRO3_NEW_ANIMATO.fbx",
        TemplateControllerPath = "Assets/Prefabs/models/Animations/Sbirro1New_Controller.controller",
        ControllerPath = "Assets/Prefabs/models/Animations/Sbirro3New_Controller.controller",
        TemplateMaterialPath = "Assets/Texture/Materials/sbirro_1_texture.mat",
        MaterialPath = "Assets/Texture/Materials/sbirro_3_new_texture.mat",
        TexturePath = "Assets/Prefabs/models/Sbirro3/Meshy_AI_Tactical_Hog_Mascot_0928163725_texture.png",
        NormalMapPath = null,
        BatonMaterialPath = "Assets/Texture/Materials/sbirro_1_manganello.mat",
        EquipmentFolder = null,
        MatchHeightOfPrefab = "Assets/Prefabs/Sbirro1.prefab",
        SizeFactor = 1f,
        WalkClip = "camminata_fucile",
        AttackClip = "sparo",
        WeaponNodeKeyword = "gun",
        WeaponTexturePath = "Assets/Prefabs/models/Sbirro3/Meshy_AI_Tactical_Grenade_Laun_0914203053_texture.png",
        WeaponMaterialPath = "Assets/Texture/Materials/sbirro_3_lanciagranate.mat",
        WeaponBrightness = 1.6f,
        WeaponGlow = 0.25f,
        AddIdleFromAttackPose = true,
        IdlePoseTime = 0f,
    };

    private const string BatonMaterialKeyword = "manganello";

    // Le mosse, riconosciute dal nome della clip.
    private const string WalkMove = "camminata";
    private const string AttackMove = "manganellata";

    [MenuItem("Tools/Sbirro1/Monta il nuovo modello animato")]
    private static void SetupSbirro1() => Run(Sbirro1);

    [MenuItem("Tools/Sbirro2/Monta il nuovo modello animato")]
    private static void SetupSbirro2() => Run(Sbirro2);

    [MenuItem("Tools/Sbirro3/Monta il nuovo modello animato")]
    private static void SetupSbirro3() => Run(Sbirro3);

    private static void Run(Setup setup)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(setup.ModelPath);
        if (model == null)
        {
            Fail(setup, $"Modello non trovato: {setup.ModelPath}");
            return;
        }

        Material bodyMaterial = BuildMaterial(setup, setup.TexturePath, setup.MaterialPath);
        if (bodyMaterial == null)
        {
            return;
        }

        Material weaponMaterial = null;
        if (setup.WeaponTexturePath != null)
        {
            weaponMaterial = BuildMaterial(setup, setup.WeaponTexturePath, setup.WeaponMaterialPath);
            if (weaponMaterial == null)
            {
                return;
            }
            BrightenWeapon(setup, weaponMaterial);
        }

        ConfigureImporter(setup, bodyMaterial, weaponMaterial);

        AnimatorController controller = BuildController(setup);
        if (controller == null)
        {
            return;
        }

        GameObject target = PrefabUtility.LoadPrefabContents(setup.PrefabPath);
        try
        {
            Transform root = target.transform;

            // Le misure si prendono dal modello che c'è adesso, prima di toglierlo: il vecchio al
            // primo lancio, quello nuovo se il comando viene rilanciato (che ha già la taglia giusta).
            Transform current = root.Cast<Transform>().FirstOrDefault(child => child.GetComponent<Animator>() != null);
            Bounds oldBounds = default;
            bool hasPlaceholder = current == null && setup.MatchHeightOfPrefab != null
                && TryMeasurePlaceholder(root, out oldBounds);
            if (!hasPlaceholder && (current == null || !TryMeasure(current.gameObject, out oldBounds)))
            {
                // Il caso tipico: l'FBX è stato cancellato e reimportato invece che sovrascritto,
                // quindi ha un GUID nuovo e il prefab punta ancora a quello vecchio (Missing Prefab).
                Fail(setup, $"In {setup.PrefabPath} non trovo il modello attuale (un figlio con Animator) da misurare. " +
                            "Se in Hierarchy il modello risulta \"Missing Prefab\", l'FBX è stato cancellato e " +
                            "reimportato: la prossima volta sovrascrivi il file senza cancellarlo.");
                return;
            }

            float targetHeight = oldBounds.size.y;
            if (setup.MatchHeightOfPrefab != null)
            {
                if (!TryReadModelHeight(setup.MatchHeightOfPrefab, out float referenceHeight))
                {
                    Fail(setup, $"Non riesco a misurare il modello dentro {setup.MatchHeightOfPrefab}.");
                    return;
                }
                targetHeight = referenceHeight * setup.SizeFactor;
            }
            float footOffset = oldBounds.min.y - root.position.y;
            if (hasPlaceholder)
            {
                RemovePlaceholder(setup, root);
            }
            else
            {
                Object.DestroyImmediate(current.gameObject);
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root);
            instance.name = model.name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            Vector3 baseScale = instance.transform.localScale;

            if (!TryMeasure(instance, out Bounds bounds))
            {
                Fail(setup, $"Il modello {model.name} non ha Renderer con geometria: non riesco a misurarlo.");
                return;
            }

            float scaleFactor = targetHeight / bounds.size.y;
            instance.transform.localScale = baseScale * scaleFactor;

            TryMeasure(instance, out bounds);
            float footNow = bounds.min.y - root.position.y;
            instance.transform.position += Vector3.up * (footOffset - footNow);

            Animator animator = instance.GetComponent<Animator>();
            if (animator == null)
            {
                animator = instance.AddComponent<Animator>();
            }
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false; // il movimento lo fa EnemyChase, non la clip

            PrefabUtility.SaveAsPrefabAsset(target, setup.PrefabPath);

            TryMeasure(instance, out Bounds finalBounds);
            Debug.Log(
                $"[{setup.Tag}] Nuovo modello montato. Alto {finalBounds.size.y:0.##} (voluto {targetHeight:0.##}, il vecchio era " +
                $"{oldBounds.size.y:0.##}), scala {scaleFactor:0.###}, controller {controller.name}.");

            ReportBindings(setup, instance, controller);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(target);
        }
    }

    // Prefab senza un modello vero (es. il vecchio Sbirro3, una capsula): i piedi vanno sul fondo
    // del collider di collisione della radice, che è quello che poggia a terra in gioco.
    // L'altezza invece viene da MatchHeightOfPrefab, qui conta solo il fondo.
    private static bool TryMeasurePlaceholder(Transform root, out Bounds bounds)
    {
        Collider body = root.GetComponent<Collider>();
        if (body == null || body.isTrigger)
        {
            bounds = default;
            return false;
        }

        bounds = body.bounds;
        return true;
    }

    // Toglie le forme segnaposto che facevano da modello: il MeshRenderer della radice (la
    // capsula; il suo collider resta, è la collisione dello Sbirro) e i figli con un Renderer
    // (es. la sfera), collider compreso: era parte della sagoma finta, non del corpo.
    private static void RemovePlaceholder(Setup setup, Transform root)
    {
        MeshRenderer rootRenderer = root.GetComponent<MeshRenderer>();
        MeshFilter rootFilter = root.GetComponent<MeshFilter>();
        if (rootRenderer != null)
        {
            Object.DestroyImmediate(rootRenderer, true);
        }
        if (rootFilter != null)
        {
            Object.DestroyImmediate(rootFilter, true);
        }

        foreach (Transform child in root.Cast<Transform>().ToArray())
        {
            if (child.GetComponentInChildren<Renderer>() != null && child.GetComponentInChildren<Animator>() == null)
            {
                Debug.Log($"[{setup.Tag}] Tolta la forma segnaposto \"{child.name}\".");
                Object.DestroyImmediate(child.gameObject);
            }
        }
    }

    // Altezza in unità di mondo del modello (il figlio con Animator) dentro un altro prefab,
    // compresa la scala della sua radice: è l'altezza con cui quello Sbirro compare in gioco.
    private static bool TryReadModelHeight(string prefabPath, out float height)
    {
        height = 0f;
        GameObject source = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Animator animator = source.GetComponentInChildren<Animator>();
            if (animator == null || !TryMeasure(animator.gameObject, out Bounds bounds))
            {
                return false;
            }

            height = bounds.size.y;
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(source);
        }
    }

    // Materiale del corpo: copia di quello vecchio (stesso shader e stesse impostazioni), con le
    // texture del modello nuovo. Se il vecchio usava la texture anche come emissione, la prende
    // anche lì. La normal map va segnata come tale nell'importer, altrimenti Unity la legge come
    // un colore e la luce viene storta.
    // Metallic e roughness di Meshy restano fuori: URP le vuole impacchettate in un'unica texture
    // (metallo in R, lucentezza in alfa), e il corpo di un maialino-poliziotto non ne ha bisogno.
    // Usato anche per l'arma separata (WeaponTexturePath/WeaponMaterialPath): stessa copia del
    // materiale del corpo, con la texture dell'arma. La normal map vale solo per il corpo.
    private static Material BuildMaterial(Setup setup, string texturePath, string materialPath)
    {
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture == null)
        {
            Fail(setup, $"Texture non trovata: {texturePath}");
            return null;
        }

        Texture2D normalMap = null;
        if (materialPath == setup.MaterialPath && setup.NormalMapPath != null)
        {
            if (AssetImporter.GetAtPath(setup.NormalMapPath) is TextureImporter normalImporter
                && normalImporter.textureType != TextureImporterType.NormalMap)
            {
                normalImporter.textureType = TextureImporterType.NormalMap;
                normalImporter.SaveAndReimport();
            }
            normalMap = AssetDatabase.LoadAssetAtPath<Texture2D>(setup.NormalMapPath);
        }

        if (AssetDatabase.LoadAssetAtPath<Material>(materialPath) == null
            && !AssetDatabase.CopyAsset(setup.TemplateMaterialPath, materialPath))
        {
            Fail(setup, $"Non riesco a copiare {setup.TemplateMaterialPath} in {materialPath}.");
            return null;
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture);
        }
        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", texture);
        }
        if (material.HasProperty("_EmissionMap") && material.GetTexture("_EmissionMap") != null)
        {
            material.SetTexture("_EmissionMap", texture);
        }
        if (material.HasProperty("_BumpMap"))
        {
            // Senza normal map propria si toglie anche quella eventualmente ereditata dal
            // materiale copiato: sarebbe la normal map di un altro modello, con altre UV.
            material.SetTexture("_BumpMap", normalMap);
            if (normalMap != null)
            {
                material.EnableKeyword("_NORMALMAP");
            }
            else
            {
                material.DisableKeyword("_NORMALMAP");
            }
        }

        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return material;
    }

    // Ogni materiale del file viene rimappato su un materiale del progetto: il manganello sul
    // nero pieno, l'equipaggiamento sul materiale omonimo della cartella dello Sbirro (se c'è),
    // tutto il resto sul corpo. La camminata va in loop. Si passa dal remap dell'importer, così
    // sopravvive a un reimport.
    private static void ConfigureImporter(Setup setup, Material bodyMaterial, Material weaponMaterial)
    {
        if (!(AssetImporter.GetAtPath(setup.ModelPath) is ModelImporter importer))
        {
            return;
        }

        // Nel file ci sono anche la telecamera (e a volte la luce) della scena di Blender,
        // esportate per sbaglio: dentro al prefab sarebbero una seconda camera e una luce che
        // girano con lo Sbirro. Si scartano all'import, così spariscono anche se il file viene
        // riesportato così.
        importer.importCameras = false;
        importer.importLights = false;

        // I nomi dei materiali del file: quelli ancora incorporati più quelli già rimappati da un
        // lancio precedente (una volta rimappato, un materiale non compare più fra i sotto-asset).
        string[] sourceMaterials = AssetDatabase.LoadAllAssetsAtPath(setup.ModelPath)
            .OfType<Material>()
            .Select(material => material.name)
            .Concat(importer.GetExternalObjectMap().Keys
                .Where(id => id.type == typeof(Material))
                .Select(id => id.name))
            .Distinct()
            .ToArray();

        string[] weaponMaterials = weaponMaterial != null
            ? FindWeaponMaterials(setup, importer)
            : new string[0];

        foreach (string name in sourceMaterials)
        {
            Material picked = weaponMaterials.Contains(name)
                ? weaponMaterial
                : PickMaterial(setup, name, bodyMaterial);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), picked);
            Debug.Log($"[{setup.Tag}] Materiale \"{name}\" del file -> {picked.name}.");
        }

        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        for (int i = 0; i < clips.Length; i++)
        {
            clips[i].loopTime = ClipMoveOf(setup, clips[i].name) == WalkMove;
        }
        importer.clipAnimations = clips;

        importer.SaveAndReimport();
    }

    // Sopra 1 il colore base non tinge, moltiplica: è una schiarita. L'emissione usa la texture
    // dell'arma, quindi aggiunge luce coi suoi stessi colori invece di velarla di grigio.
    // Valori assoluti, non incrementi: rilanciare il tool non la schiarisce di nuovo.
    private static void BrightenWeapon(Setup setup, Material material)
    {
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", Color.white * setup.WeaponBrightness);
        }

        if (material.HasProperty("_EmissionColor"))
        {
            if (setup.WeaponGlow > 0f)
            {
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                if (material.HasProperty("_EmissionMap"))
                {
                    material.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(setup.WeaponTexturePath));
                }
                material.SetColor("_EmissionColor", Color.white * setup.WeaponGlow);
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
            }
        }

        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
    }

    // I nomi (nel file) dei materiali usati dalle mesh dell'arma, cioè sotto un nodo con
    // WeaponNodeKeyword nel nome: il nome del materiale da solo non lo dice (Meshy li chiama
    // "Material.001", "Material_0.001"...). Dopo un primo lancio sui renderer c'è già il
    // materiale rimappato, quindi si risale al nome originale dalla tabella dei remap.
    private static string[] FindWeaponMaterials(Setup setup, ModelImporter importer)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(setup.ModelPath);
        var remaps = importer.GetExternalObjectMap();
        string keyword = setup.WeaponNodeKeyword.ToLowerInvariant();

        string[] names = model.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => IsUnderNode(renderer.transform, keyword))
            .SelectMany(renderer => renderer.sharedMaterials)
            .Where(material => material != null)
            .Select(material => remaps.FirstOrDefault(pair => pair.Value == material).Key.name ?? material.name)
            .Distinct()
            .ToArray();

        if (names.Length == 0)
        {
            Debug.LogWarning($"[{setup.Tag}] Nessuna mesh sotto un nodo \"{setup.WeaponNodeKeyword}\": l'arma prende il materiale del corpo.");
        }
        return names;
    }

    private static bool IsUnderNode(Transform node, string keyword)
    {
        for (Transform current = node; current != null; current = current.parent)
        {
            if (current.name.ToLowerInvariant().Contains(keyword))
            {
                return true;
            }
        }
        return false;
    }

    // Il manganello va nero pieno, ma solo se in Blender ha un materiale suo con "manganello" nel
    // nome: se è un pezzo della mesh del corpo, dipinto sulla stessa texture, da qui non c'è modo
    // di distinguerlo.
    private static Material PickMaterial(Setup setup, string sourceName, Material bodyMaterial)
    {
        if (sourceName.ToLowerInvariant().Contains(BatonMaterialKeyword))
        {
            return GetBatonMaterial(setup);
        }

        if (setup.EquipmentFolder != null)
        {
            Material equipment = AssetDatabase.LoadAssetAtPath<Material>($"{setup.EquipmentFolder}/{sourceName}.mat");
            if (equipment != null)
            {
                return equipment;
            }
        }

        return bodyMaterial;
    }

    // Nero pieno e opaco: niente texture e quasi nessun riflesso, altrimenti la luce ci
    // disegnerebbe sopra una striscia grigia e non sembrerebbe più nero.
    private static Material GetBatonMaterial(Setup setup)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(setup.BatonMaterialPath);
        if (material != null)
        {
            return material;
        }

        material = new Material(AssetDatabase.LoadAssetAtPath<Material>(setup.TemplateMaterialPath).shader);
        material.SetColor("_BaseColor", Color.black);
        material.SetFloat("_Smoothness", 0.1f);
        material.SetFloat("_Metallic", 0f);
        AssetDatabase.CreateAsset(material, setup.BatonMaterialPath);
        AssetDatabase.SaveAssets();
        return material;
    }

    // Copia del controller vecchio con le clip sostituite da quelle del modello nuovo. Se esiste
    // già non viene ricopiato, solo riagganciato alle clip: i ritocchi fatti a mano restano.
    private static AnimatorController BuildController(Setup setup)
    {
        AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(setup.ModelPath)
            .OfType<AnimationClip>()
            .Where(clip => !clip.name.StartsWith("__preview__"))
            .ToArray();
        if (clips.Length == 0)
        {
            Fail(setup, $"Nessuna clip dentro {setup.ModelPath}: controlla che nell'importer Animation sia acceso.");
            return null;
        }

        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(setup.ControllerPath) == null
            && !AssetDatabase.CopyAsset(setup.TemplateControllerPath, setup.ControllerPath))
        {
            Fail(setup, $"Non riesco a copiare {setup.TemplateControllerPath} in {setup.ControllerPath}.");
            return null;
        }

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(setup.ControllerPath);
        foreach (AnimatorControllerLayer layer in controller.layers)
        {
            foreach (ChildAnimatorState child in layer.stateMachine.states)
            {
                // Al primo lancio gli stati puntano alle clip del controller copiato ("camminata",
                // "manganellata"); rilanciando, già a quelle di questo modello (es. "sparo").
                string stateMotion = child.state.motion != null ? child.state.motion.name : child.state.name;
                string move = ClipMoveOf(setup, stateMotion) ?? MoveOf(stateMotion);
                if (move == null)
                {
                    continue;
                }

                AnimationClip replacement = clips.FirstOrDefault(clip => ClipMoveOf(setup, clip.name) == move);
                if (replacement != null)
                {
                    child.state.motion = replacement;
                }
                else
                {
                    string wanted = move == WalkMove ? setup.WalkClip : setup.AttackClip;
                    Debug.LogWarning($"[{setup.Tag}] Nessuna clip \"{wanted}\" nel modello nuovo: lo stato {child.state.name} resta com'era. " +
                                     $"Clip trovate: {string.Join(", ", clips.Select(clip => clip.name))}.");
                }
            }
        }

        if (setup.AddIdleFromAttackPose)
        {
            AddIdleState(setup, controller);
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    private const string IdleStateName = "Idle";
    private const string MovingParameter = "Moving";

    // Aggiunge lo stato fermo (vedi Setup.AddIdleFromAttackPose) e lo collega:
    //   camminata <-> fermo     secondo "Moving";
    //   fermo -> attacco        col trigger "Attack", come già fa la camminata;
    //   attacco -> fermo/camminata a fine clip, secondo "Moving".
    // Rilanciando si rifà tutto da capo: le transizioni verso/da lo stato fermo vengono tolte e
    // ricreate, quella attacco -> camminata perde la vecchia condizione su "Moving" e la riprende.
    private static void AddIdleState(Setup setup, AnimatorController controller)
    {
        if (!controller.parameters.Any(parameter => parameter.name == MovingParameter))
        {
            controller.AddParameter(MovingParameter, AnimatorControllerParameterType.Bool);
        }

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState walk = null;
        AnimatorState attack = null;
        AnimatorState idle = null;
        foreach (ChildAnimatorState child in machine.states)
        {
            if (child.state.name == IdleStateName)
            {
                idle = child.state;
                continue;
            }

            string motion = child.state.motion != null ? child.state.motion.name : "";
            string move = ClipMoveOf(setup, motion);
            if (move == WalkMove)
            {
                walk = child.state;
            }
            else if (move == AttackMove)
            {
                attack = child.state;
            }
        }

        if (walk == null || attack == null)
        {
            Debug.LogWarning($"[{setup.Tag}] Non trovo gli stati di camminata e attacco nel controller: niente stato fermo.");
            return;
        }

        if (idle == null)
        {
            idle = machine.AddState(IdleStateName, new Vector3(250f, 200f, 0f));
        }

        // Velocità 0: la clip resta ferma al fotogramma scelto con cycleOffset.
        idle.motion = attack.motion;
        idle.speed = 0f;
        idle.cycleOffset = setup.IdlePoseTime;
        idle.writeDefaultValues = attack.writeDefaultValues;

        foreach (AnimatorStateTransition transition in idle.transitions.ToArray())
        {
            idle.RemoveTransition(transition);
        }
        foreach (AnimatorState state in new[] { walk, attack })
        {
            foreach (AnimatorStateTransition transition in state.transitions.Where(t => t.destinationState == idle).ToArray())
            {
                state.RemoveTransition(transition);
            }
        }

        AnimatorStateTransition attackToWalk = attack.transitions.FirstOrDefault(t => t.destinationState == walk);
        if (attackToWalk == null)
        {
            attackToWalk = attack.AddTransition(walk);
            attackToWalk.hasExitTime = true;
            attackToWalk.exitTime = 0.87f;
            attackToWalk.duration = 0.25f;
        }
        attackToWalk.conditions = attackToWalk.conditions.Where(c => c.parameter != MovingParameter).ToArray();
        attackToWalk.AddCondition(AnimatorConditionMode.If, 0f, MovingParameter);

        AnimatorStateTransition attackToIdle = attack.AddTransition(idle);
        attackToIdle.hasExitTime = true;
        attackToIdle.exitTime = attackToWalk.exitTime;
        attackToIdle.duration = attackToWalk.duration;
        attackToIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, MovingParameter);

        AnimatorStateTransition walkToIdle = walk.AddTransition(idle);
        walkToIdle.hasExitTime = false;
        walkToIdle.duration = 0.15f;
        walkToIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, MovingParameter);

        AnimatorStateTransition idleToWalk = idle.AddTransition(walk);
        idleToWalk.hasExitTime = false;
        idleToWalk.duration = 0.15f;
        idleToWalk.AddCondition(AnimatorConditionMode.If, 0f, MovingParameter);

        AnimatorStateTransition idleToAttack = idle.AddTransition(attack);
        idleToAttack.hasExitTime = false;
        idleToAttack.duration = 0.1f;
        idleToAttack.AddCondition(AnimatorConditionMode.If, 0f, AttackTriggerName);

        Debug.Log($"[{setup.Tag}] Stato fermo \"{IdleStateName}\": clip \"{idle.motion.name}\" ferma al {setup.IdlePoseTime:0%}.");
    }

    private const string AttackTriggerName = "Attack";

    // Controlla che ogni osso animato dalle clip esista davvero sotto al modello: se ne mancano,
    // il modello resta fermo o si accartoccia senza un errore in Console.
    private static void ReportBindings(Setup setup, GameObject modelRoot, AnimatorController controller)
    {
        foreach (AnimatorControllerLayer layer in controller.layers)
        {
            foreach (ChildAnimatorState child in layer.stateMachine.states)
            {
                if (!(child.state.motion is AnimationClip clip))
                {
                    continue;
                }

                string[] paths = AnimationUtility.GetCurveBindings(clip)
                    .Select(binding => binding.path)
                    .Where(path => !string.IsNullOrEmpty(path))
                    .Distinct()
                    .ToArray();
                string[] missing = paths.Where(path => modelRoot.transform.Find(path) == null).ToArray();

                string detail = missing.Length == 0
                    ? "tutte agganciate"
                    : $"NON agganciate: {missing.Length} (es. {string.Join(" | ", missing.Take(3))})";
                Debug.Log($"[{setup.Tag}] Stato \"{child.state.name}\" -> clip \"{clip.name}\": {paths.Length} ossa animate, {detail}.");
            }
        }
    }

    // La mossa di una clip, dal nome: serve ad accoppiare gli stati del controller vecchio
    // (che puntano alle clip del vecchio modello) con le clip omonime del modello nuovo.
    private static string MoveOf(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        string lower = name.ToLowerInvariant();
        if (lower.Contains(WalkMove))
        {
            return WalkMove;
        }

        return lower.Contains(AttackMove) ? AttackMove : null;
    }

    // La mossa di una clip del modello nuovo: l'ultimo pezzo del nome (dopo l'ultima "|")
    // confrontato con WalkClip/AttackClip del setup. Confronto esatto e non "contiene": nel file
    // dello Sbirro3 ci sono sia "camminata" (senza fucile, avanzo da Blender) sia
    // "camminata_fucile", e va presa solo la seconda.
    private static string ClipMoveOf(Setup setup, string clipName)
    {
        if (string.IsNullOrEmpty(clipName))
        {
            return null;
        }

        string move = clipName.Substring(clipName.LastIndexOf('|') + 1).ToLowerInvariant();
        if (move == setup.WalkClip.ToLowerInvariant())
        {
            return WalkMove;
        }

        return move == setup.AttackClip.ToLowerInvariant() ? AttackMove : null;
    }

    // Ingombro del modello misurato sulla MESH (posa di legatura) e non su Renderer.bounds, che
    // per una mesh skinnata dipende dalla posa in cui l'Animator l'ha lasciata e cambia da un
    // lancio all'altro. Così la misura è ripetibile e confrontabile fra vecchio e nuovo modello.
    private static bool TryMeasure(GameObject target, out Bounds bounds)
    {
        bounds = new Bounds(target.transform.position, Vector3.zero);
        bool found = false;

        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>())
        {
            Mesh mesh = renderer is SkinnedMeshRenderer skinned
                ? skinned.sharedMesh
                : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null || mesh.bounds.size.sqrMagnitude <= 0.000001f)
            {
                continue;
            }

            Bounds local = mesh.bounds;
            Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 offset = new Vector3(
                    (corner & 1) == 0 ? -local.extents.x : local.extents.x,
                    (corner & 2) == 0 ? -local.extents.y : local.extents.y,
                    (corner & 4) == 0 ? -local.extents.z : local.extents.z);
                Vector3 world = toWorld.MultiplyPoint3x4(local.center + offset);

                if (!found)
                {
                    bounds = new Bounds(world, Vector3.zero);
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(world);
                }
            }
        }

        return found;
    }

    private static void Fail(Setup setup, string message)
    {
        EditorUtility.DisplayDialog(setup.Tag, message, "OK");
        Debug.LogError($"[{setup.Tag}] {message}");
    }
}
