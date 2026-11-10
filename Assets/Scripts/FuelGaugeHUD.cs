using UnityEngine;
using UnityEngine.UI;

// Mad Max Fuel HUD (variante 4A) — collega questo script al GameObject root del HUD.
// Sprite in Assets/Sprites/FuelHud/: dial, needle, hub, fuel_icon, heart, bolt, life_segment.
// Import settings consigliati: Texture Type = Sprite (2D and UI), Pixels Per Unit = 400
// (gli sprite sono renderizzati a 4x), Filter = Bilinear, Compression = None per l'UI.
// PIVOT: needle.png -> pivot Bottom Center (0.5, 0). dial.png -> Custom pivot (0.5, 0.0328)
// cioè il perno sta 4 px sopra il bordo inferiore dell'immagine (a 1x).
[DisallowMultipleComponent]
public class FuelGaugeHUD : MonoBehaviour
{
    [Header("Refs")]
    public RectTransform needle;           // Image con needle.png, pivot bottom-center
    public Image[] lifeSegments;           // 8 Image con life_segment.png, da sinistra a destra
    [Tooltip("Il vecchio contatore scatti (fulmine): non serve più, gli scatti si contano nel rettangolo accanto agli altri poteri. Viene nascosto all'avvio insieme all'icona del fulmine.")]
    public Text dashCountLabel;
    public Text speedLabel;                // opzionale: km/h

    [Header("Range lancetta")]
    public float emptyAngle = -90f;        // E
    public float fullAngle  =  90f;        // F
    [Tooltip("Velocità di inseguimento della lancetta (unità/s). 0 = istantanea.")]
    public float needleLerp = 6f;
    [Tooltip("Vibrazione in gradi, simula l'ago meccanico.")]
    public float needleJitter = 1f;

    [Header("Barra vita")]
    [Tooltip("Fondo della barra vita, che si vede dove la vita è già stata persa.")]
    public Color lifeBarBackColor = new Color(0f, 0f, 0f, 0.55f);
    [Tooltip("Altezza della barra vita sopra la minimappa, nelle unità della minimappa. Il cuoricino accanto è un po' più grande.")]
    public float minimapLifeBarHeight = 16f;
    [Tooltip("Spazio in più fra la cima della minimappa (il countdown) e la barra vita.")]
    public float minimapLifeBarLift = 8f;
    [Tooltip("Grandezza del cuoricino accanto alla barra, rispetto all'altezza della barra.")]
    public float minimapHeartScale = 1.3f;

    [Header("Buchi neri, khole, scatti e serotonina")]
    [Tooltip("Colore del rettangolo dei buchi neri quando ce n'è almeno uno da lanciare.")]
    public Color blackHoleColor = new Color(0.55f, 0.2f, 0.85f, 1f);
    [Tooltip("Colore del rettangolo dei khole quando ce n'è almeno uno da usare.")]
    public Color kholeColor = new Color(0.2f, 0.75f, 0.95f, 1f);
    [Tooltip("Colore del rettangolo degli scatti quando ce n'è almeno uno da usare.")]
    public Color dashColor = new Color(1f, 0.82f, 0.2f, 1f);
    [Tooltip("Colore del rettangolo della serotonina quando ce n'è almeno una da usare.")]
    public Color serotoninColor = new Color(1f, 0.5f, 0.08f, 1f);
    [Tooltip("Colore dei rettangoli a tasca vuota.")]
    public Color blackHoleEmptyColor = new Color(0f, 0f, 0f, 0.55f);
    [Tooltip("Misure di ciascun rettangolo, nelle stesse unità dei segmenti vita.")]
    public Vector2 blackHoleBoxSize = new Vector2(60f, 24f);
    [Tooltip("Spazio fra i segmenti vita e i rettangoli, e fra un rettangolo e l'altro.")]
    public float blackHoleBoxGap = 6f;

    [Header("Controller")]
    [Tooltip("Immagine del controller (Texture/Controller_da_gioco...): se c'è, prende il posto dei rettangoli e i contatori si scrivono accanto ai tasti colorati.")]
    public Texture2D controllerTexture;
    [Tooltip("Larghezza del controller, nelle stesse unità dei segmenti vita. L'altezza segue le proporzioni dell'immagine.")]
    public float controllerWidth = 184f;
    public int controllerFontSize = 18;
    [Tooltip("Spostamento del controller rispetto a dove stavano i segmenti vita (in alto a sinistra della riga).")]
    public Vector2 controllerOffset = new Vector2(0f, 50f);
    [Tooltip("Dove vanno i segmenti vita sotto il controller, in frazione della sua larghezza (0 = bordo sinistro, 1 = destro).")]
    public float controllerLifeX = 0.5f;
    [Tooltip("Spazio fra il fondo del controller e i segmenti vita. Negativo apposta: al centro il fondo del pad rientra (fra le due impugnature), e i segmenti salgono in quella rientranza senza toccare il pad.")]
    public float controllerLifeGap = -15f;
    [Tooltip("Colori delle scritte accanto ai tasti, come i tasti dell'immagine ma più chiari: sul fondo scuro del gioco il viola pieno di L1 non si leggeva.")]
    public Color hoffmanLabelColor = new Color(0.82f, 0.6f, 1f, 1f);
    public Color kholeLabelColor = new Color(0.45f, 0.85f, 1f, 1f);
    public Color dashLabelColor = Color.white;
    public Color serotoninLabelColor = new Color(1f, 0.62f, 0.25f, 1f);

    // ---- stato letto dal gioco (0..1 per fuel e life) ----
    [Range(0f, 1f)] public float fuel01 = 0.38f;
    [Range(0f, 1f)] public float life01 = 0.75f;
    RectTransform lifeFill; // parte piena della barra vita (vedi BuildLifeBar)
    public float speed = 0f;
    public int blackHoles = 0;
    public int kholes = 0;
    public int dashes = 0;
    public int serotonins = 0;

    float shownFuel;
    RectTransform powerRow;   // genitore dei rettangoli dei poteri: lo stesso dei segmenti vita
    Image blackHoleBox;
    Text blackHoleLabel;
    Image kholeBox;
    Text kholeLabel;
    Image dashBox;
    Text dashLabel;
    Image serotoninBox;
    Text serotoninLabel;
    bool lifeOnController; // segmenti vita spostati sul controller (vedi BuildController)
    Vector2 lifeTarget;    // punto del controller su cui centrarli
    RectTransform heart;   // il cuoricino, che sul controller sta sotto i segmenti vita

    // Nomi degli oggetti sotto l'HUD da cui vengono ritrovati i riferimenti mancanti.
    const string NeedleName = "needle";
    const string SegmentName = "life_segment";
    const string BoltName = "bolt";
    const string HeartName = "heart";
    const string ControllerName = "controller";

    void Awake()
    {
        ResolveMissingRefs();
    }

    // Il gauge è stato recuperato da backup più di una volta, e ogni volta gli oggetti sono
    // tornati al loro posto ma i riferimenti del componente no (è il guasto che descrive
    // FuelGaugeHudRepair; in GameplayV2 "needle" era di nuovo {fileID: 0}). Con needle vuoto la
    // lancetta non si muove MAI: Update calcola l'angolo giusto e poi lo scrive su un riferimento
    // nullo, senza un solo errore in Console. In gioco sembra una lancetta ferma in mezzo al
    // quadrante, perché la rotazione dell'oggetto resta a zero, che con emptyAngle e fullAngle
    // simmetrici è esattamente metà scala.
    //
    // Quindi non dipendiamo più dal comando in Tools/HUD, che va ricordato a mano ogni volta: qui
    // i riferimenti vuoti vengono ritrovati per nome fra i figli, come CamperVehicle e
    // PlayerHealth già fanno per riagganciare l'HUD stesso. Il comando resta utile perché
    // riempie i campi anche fuori dal Play e riscrive i colori salvati in scena.
    void ResolveMissingRefs()
    {
        if (needle == null)
        {
            RectTransform[] rects = GetComponentsInChildren<RectTransform>(true);
            for (int i = 0; i < rects.Length; i++)
            {
                if (rects[i] == transform) continue;
                if (!rects[i].name.ToLowerInvariant().Contains(NeedleName)) continue;
                needle = rects[i];
                break;
            }
        }

        if (!SegmentsAreComplete())
        {
            RebuildSegments();
        }
    }

    // Anche una sola casella vuota va rifatta: ApplyLife accende le prime "lit" caselle
    // dell'array e quelle nulle vengono saltate, quindi con dei buchi in mezzo la barra mostra
    // un numero di segmenti che non c'entra con la vita residua.
    bool SegmentsAreComplete()
    {
        if (lifeSegments == null || lifeSegments.Length == 0) return false;
        for (int i = 0; i < lifeSegments.Length; i++)
        {
            if (lifeSegments[i] == null) return false;
        }
        return true;
    }

    void RebuildSegments()
    {
        Image[] images = GetComponentsInChildren<Image>(true);
        int count = 0;
        for (int i = 0; i < images.Length; i++)
        {
            if (IsSegment(images[i])) count++;
        }
        if (count == 0) return; // niente da ricollegare: meglio lasciare com'era che svuotare tutto

        Image[] found = new Image[count];
        int k = 0;
        for (int i = 0; i < images.Length; i++)
        {
            if (IsSegment(images[i])) found[k++] = images[i];
        }

        // Da sinistra a destra per posizione sullo schermo, non per ordine in Hierarchy: la barra
        // si svuota da destra, ed è l'ordine che ApplyLife si aspetta.
        for (int i = 1; i < found.Length; i++)
        {
            Image item = found[i];
            float x = item.rectTransform.position.x;
            int j = i - 1;
            while (j >= 0 && found[j].rectTransform.position.x > x)
            {
                found[j + 1] = found[j];
                j--;
            }
            found[j + 1] = item;
        }

        lifeSegments = found;
    }

    static bool IsSegment(Image image)
    {
        return image.name.ToLowerInvariant().Contains(SegmentName);
    }

    void Start()
    {
        shownFuel = fuel01;
        HideDashCounter();
        HideFuelDial();
        BuildPowerBoxes();
        BuildLifeBar();
        ApplyPowers();
        ApplyCounters();
    }

    // Il vecchio contatore scatti (fulmine) non serve più: gli scatti si contano nel rettangolo
    // accanto agli altri poteri (BuildPowerBoxes).
    void HideDashCounter()
    {
        if (dashCountLabel != null) dashCountLabel.gameObject.SetActive(false);

        foreach (Image image in GetComponentsInChildren<Image>(true))
        {
            if (image.name.ToLowerInvariant().Contains(BoltName)) image.gameObject.SetActive(false);
        }
    }

    // Il quadrante della benzina serviva al Camper guidato dai Raver (L2), meccanica spenta: il
    // mezzo ora è solo un modello di scena. Si spengono le Image di quadrante, lancetta e perno,
    // non i GameObject, perché i segmenti vita e il resto dell'HUD potrebbero esserne figli.
    // Per riaverlo basta togliere la chiamata in Start.
    void HideFuelDial()
    {
        foreach (Image image in GetComponentsInChildren<Image>(true))
        {
            string n = image.name.ToLowerInvariant();
            if (n == "dial" || n == "hub" || n == "fuel_icon" || n.Contains(NeedleName)) image.enabled = false;
        }

        if (speedLabel != null) speedLabel.gameObject.SetActive(false);
    }

    // Segnaposto per i poteri in tasca: quattro rettangoli in fila sotto i segmenti vita, allineati a
    // sinistra, con scritto quanti ce ne sono. A sinistra gli Hoffman (PlayerHoffman,
    // viola), poi i khole (PlayerKhole, azzurro), gli scatti (PlayerDash, giallo) e la serotonina
    // (PlayerSerotonin, arancione); scuri a tasca
    // vuota. Vengono costruiti qui e non stanno in scena: così si mettono da soli sotto i segmenti
    // vita, dovunque sia stato spostato il gauge, e non c'è niente da ricollegare a mano se l'HUD
    // viene di nuovo recuperato da un backup (vedi ResolveMissingRefs).
    void BuildPowerBoxes()
    {
        if (!SegmentsAreComplete()) return;
        if (controllerTexture != null)
        {
            BuildController();
            return;
        }

        powerRow = lifeSegments[0].rectTransform.parent as RectTransform;
        if (powerRow == null) return;

        // Ingombro di tutti i segmenti nello spazio del loro genitore.
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        Vector3[] corners = new Vector3[4];
        foreach (Image segment in lifeSegments)
        {
            segment.rectTransform.GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
            {
                Vector2 local = powerRow.InverseTransformPoint(corner);
                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }
        }

        Vector3 origin = new Vector3(min.x, min.y - blackHoleBoxGap, 0f);
        Vector3 step = new Vector3(blackHoleBoxSize.x + blackHoleBoxGap, 0f, 0f);
        BuildCounterBox("black_hole", origin, out blackHoleBox, out blackHoleLabel);
        BuildCounterBox("khole", origin + step, out kholeBox, out kholeLabel);
        BuildCounterBox("dash", origin + step * 2f, out dashBox, out dashLabel);
        BuildCounterBox("serotonin", origin + step * 3f, out serotoninBox, out serotoninLabel);
    }

    // Al posto dei rettangoli, l'immagine del controller dove stavano i segmenti vita, che si
    // spostano sotto il pad, staccati, col cuoricino sotto. I contatori sono scritti fuori dal pad, accanto ai
    // tasti dello stesso colore: a sinistra di L1 (viola, Hoffman) e di L2 (azzurro, khole), a
    // destra di R1 (bianco, scatto) e di R2 (arancione, serotonina).
    //
    // Se il pad è già salvato in scena (Tools/HUD/Salva il pad in scena) si usa quello, con la
    // posizione e le scritte che gli sono state date a mano; altrimenti viene costruito qui.
    void BuildController()
    {
        powerRow = lifeSegments[0].rectTransform.parent as RectTransform;
        if (powerRow == null) return;

        foreach (Transform child in powerRow)
        {
            if (child.name.ToLowerInvariant() == HeartName)
            {
                heart = (RectTransform)child;
                break;
            }
        }

        RectTransform controller = powerRow.Find(ControllerName) as RectTransform;
        if (controller == null) controller = CreateController();

        // Segmenti vita sotto il pad, dovunque sia stato messo: li centra ApplyLife, che sa quanti se ne vedono.
        Rect pad = controller.rect;
        Vector2 topLeft = (Vector2)controller.localPosition + new Vector2(pad.xMin, pad.yMax);
        float segmentHalf = lifeSegments[0].rectTransform.rect.height * lifeSegments[0].rectTransform.localScale.y * 0.5f;
        lifeTarget = topLeft + new Vector2(controllerLifeX * pad.width, -pad.height - controllerLifeGap - segmentHalf);
        lifeOnController = true;

        // Punti sul bordo esterno dei tasti, in frazioni dell'immagine (0,0 = in basso a
        // sinistra). L1/L2 e R1/R2 sono uno sopra l'altro e vicinissimi: le scritte dei tasti di
        // sopra (L2, R2) si alzano un po', altrimenti si accavallano.
        const float raise = 10f;
        blackHoleLabel = FindOrCreateButtonLabel("hoffman_count", controller, new Vector2(0.150f, 0.824f), false, 0f, hoffmanLabelColor);
        kholeLabel = FindOrCreateButtonLabel("khole_count", controller, new Vector2(0.183f, 0.907f), false, raise, kholeLabelColor);
        dashLabel = FindOrCreateButtonLabel("dash_count", controller, new Vector2(0.850f, 0.828f), true, 0f, dashLabelColor);
        serotoninLabel = FindOrCreateButtonLabel("serotonin_count", controller, new Vector2(0.825f, 0.912f), true, raise, serotoninLabelColor);
    }

    RectTransform CreateController()
    {
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        Vector3[] corners = new Vector3[4];
        int firstSibling = int.MaxValue;
        foreach (Image segment in lifeSegments)
        {
            segment.rectTransform.GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
            {
                Vector2 local = powerRow.InverseTransformPoint(corner);
                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }
            firstSibling = Mathf.Min(firstSibling, segment.transform.GetSiblingIndex());
        }
        if (heart != null) firstSibling = Mathf.Min(firstSibling, heart.GetSiblingIndex());

        GameObject go = new GameObject(ControllerName, typeof(RectTransform), typeof(RawImage), typeof(LayoutElement));
        go.transform.SetParent(powerRow, false);
        go.GetComponent<LayoutElement>().ignoreLayout = true;
        RawImage image = go.GetComponent<RawImage>();
        image.texture = controllerTexture;
        image.raycastTarget = false;

        RectTransform controller = (RectTransform)go.transform;
        controller.anchorMin = controller.anchorMax = new Vector2(0.5f, 0.5f);
        controller.pivot = new Vector2(0f, 1f);
        float aspect = (float)controllerTexture.height / Mathf.Max(controllerTexture.width, 1);
        controller.sizeDelta = new Vector2(controllerWidth, controllerWidth * aspect);
        Vector2 topLeft = new Vector2(min.x, max.y) + controllerOffset;
        controller.localPosition = topLeft;
        // Dietro ai segmenti vita e al cuoricino, che altrimenti finirebbero coperti dall'immagine.
        controller.SetSiblingIndex(firstSibling);
        return controller;
    }

    Text FindOrCreateButtonLabel(string name, RectTransform controller, Vector2 buttonEdge, bool onRight, float yOffset, Color color)
    {
        Transform existing = controller.Find(name);
        Text label = existing != null ? existing.GetComponent<Text>() : null;
        return label != null ? label : CreateButtonLabel(name, controller, buttonEdge, onRight, yOffset, color);
    }

    // Per Tools/HUD/Salva il pad in scena: costruisce (o ritrova) il pad fuori dal Play e ci
    // centra sotto tutti i segmenti vita, così quello che si vede in editor è come sarà in gioco.
    // Non tocca i colori dei segmenti: li decide ApplyLife quando parte la partita.
    public RectTransform BakeController()
    {
        ResolveMissingRefs();
        if (controllerTexture == null || !SegmentsAreComplete()) return null;

        BuildController();
        if (!lifeOnController) return null;

        CenterLifeOnController(lifeSegments.Length);
        ApplyPowers();
        return powerRow.Find(ControllerName) as RectTransform;
    }

    Text CreateButtonLabel(string name, RectTransform controller, Vector2 buttonEdge, bool onRight, float yOffset, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline));
        go.transform.SetParent(controller, false);
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = buttonEdge;
        rect.pivot = new Vector2(onRight ? 0f : 1f, 0.5f);
        rect.sizeDelta = new Vector2(120f, controllerFontSize + 8f);
        rect.anchoredPosition = new Vector2(onRight ? 10f : -10f, yOffset); // staccate dal pad

        Text label = go.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = controllerFontSize;
        label.fontStyle = FontStyle.Bold;
        label.alignment = onRight ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.color = color;
        label.raycastTarget = false;

        Outline outline = go.GetComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        return label;
    }

    void BuildCounterBox(string name, Vector3 localPosition, out Image boxImage, out Text label)
    {
        RectTransform box = CreateBarRect(name + "_box", powerRow, blackHoleEmptyColor);
        box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
        box.pivot = new Vector2(0f, 1f);
        box.sizeDelta = blackHoleBoxSize;
        box.localPosition = localPosition;
        boxImage = box.GetComponent<Image>();

        GameObject labelObject = new GameObject(name + "_count", typeof(RectTransform), typeof(Text));
        labelObject.transform.SetParent(box, false);
        RectTransform labelRect = (RectTransform)labelObject.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;

        label = labelObject.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.alignment = TextAnchor.MiddleCenter;
        label.fontStyle = FontStyle.Bold;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 6;
        label.resizeTextMaxSize = 40;
        label.color = Color.white;
        label.raycastTarget = false;
    }

    static RectTransform CreateBarRect(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        // Se il genitore dei segmenti ha un layout automatico, la barra non deve entrarci.
        go.GetComponent<LayoutElement>().ignoreLayout = true;

        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return (RectTransform)go.transform;
    }

    void Update()
    {
        shownFuel = needleLerp <= 0f
            ? fuel01
            : Mathf.MoveTowards(shownFuel, fuel01, needleLerp * Time.deltaTime);

        if (needle != null)
        {
            float a = Mathf.Lerp(emptyAngle, fullAngle, shownFuel);
            float j = needleJitter <= 0f ? 0f :
                (Mathf.PerlinNoise(Time.time * 3.1f, 0f) - 0.5f) * 2f * needleJitter;
            // rotazione UI: angolo positivo = antiorario, quindi invertiamo
            needle.localRotation = Quaternion.Euler(0f, 0f, -(a + j));
        }
    }

    // === API da chiamare dal gioco ===============================
    public void SetFuel(float current, float max)   { fuel01 = max <= 0f ? 0f : Mathf.Clamp01(current / max); }
    public void SetFuel01(float v)                  { fuel01 = Mathf.Clamp01(v); }

    public void SetLife(float current, float max)   { SetLife01(max <= 0f ? 0f : current / max); }
    public void SetLife01(float v)                  { life01 = Mathf.Clamp01(v); ApplyLife(); }

    public void SetDashes(int count)                { dashes = Mathf.Max(0, count); ApplyPowers(); }
    public void SetSpeed(float kmh)                 { speed = kmh; ApplyCounters(); }
    public void SetBlackHoles(int count)            { blackHoles = Mathf.Max(0, count); ApplyPowers(); }
    public void SetKholes(int count)                { kholes = Mathf.Max(0, count); ApplyPowers(); }
    public void SetSerotonins(int count)            { serotonins = Mathf.Max(0, count); ApplyPowers(); }

    // === rendering ===============================================
    // Niente più un cubetto per colpo: con 5 colpi in normale e 10 in facile (vedi PlayerHealth) la
    // vita è una barra continua, lunga quanto tutta la fila dei segmenti e al loro posto, che si
    // accorcia da destra e sfuma dal verde (piena) al rosso (vuota) passando per il giallo. I
    // segmenti restano in scena, spenti, solo come riferimento per la posizione (vedi BuildLifeBar).
    void ApplyLife()
    {
        if (lifeFill == null) return; // prima di Start: la barra la costruisce BuildLifeBar
        lifeFill.anchorMax = new Vector2(life01, 1f);
        lifeFill.GetComponent<Image>().color = Color.HSVToRGB(Mathf.Lerp(0f, 1f / 3f, life01), 0.9f, 0.95f);
    }

    // In Start, dopo BuildPowerBoxes. Se in scena c'è la minimappa, barra e cuoricino vanno sopra
    // di lei (BuildLifeBarOnMinimap) e il pad resta da solo; altrimenti al posto dei segmenti.
    void BuildLifeBar()
    {
        if (!SegmentsAreComplete()) return;

        MinimapUI minimap = FindAnyObjectByType<MinimapUI>();
        if (minimap != null)
        {
            BuildLifeBarOnMinimap(minimap);
            return;
        }

        if (lifeOnController) CenterLifeOnController(lifeSegments.Length);

        RectTransform parent = lifeSegments[0].rectTransform.parent as RectTransform;
        if (parent == null) return;

        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        Vector3[] corners = new Vector3[4];
        foreach (Image segment in lifeSegments)
        {
            segment.rectTransform.GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
            {
                Vector2 local = parent.InverseTransformPoint(corner);
                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }
            segment.gameObject.SetActive(false);
        }

        RectTransform back = CreateBarRect("life_bar", parent, lifeBarBackColor);
        back.anchorMin = back.anchorMax = new Vector2(0.5f, 0.5f);
        back.pivot = Vector2.zero;
        back.sizeDelta = max - min;
        back.localPosition = min;
        back.SetSiblingIndex(lifeSegments[0].transform.GetSiblingIndex());

        lifeFill = CreateBarRect("life_fill", back, Color.green);
        lifeFill.anchorMin = Vector2.zero;
        lifeFill.anchorMax = Vector2.one;
        lifeFill.offsetMin = lifeFill.offsetMax = Vector2.zero;
        ApplyLife();
    }

    // Barra larga quanto la minimappa, appena sopra il suo ultimo pannello (il countdown), col
    // cuoricino a sinistra. Barra e cuore diventano figli della minimappa e la seguono se la si sposta.
    void BuildLifeBarOnMinimap(MinimapUI minimap)
    {
        foreach (Image segment in lifeSegments) segment.gameObject.SetActive(false);

        RectTransform map = (RectTransform)minimap.transform;
        float width = map.rect.width;
        float bottom = minimap.TopY + minimap.PanelSpacing + minimapLifeBarLift;

        if (heart == null) heart = FindHeart();
        float heartSize = 0f;
        if (heart != null)
        {
            heartSize = minimapLifeBarHeight * minimapHeartScale;
            heart.SetParent(map, false);
            heart.anchorMin = heart.anchorMax = new Vector2(0.5f, 0.5f);
            heart.pivot = new Vector2(0f, 0.5f);
            heart.localScale = Vector3.one;
            heart.sizeDelta = new Vector2(heartSize, heartSize);
            heart.anchoredPosition = new Vector2(-width * 0.5f, bottom + minimapLifeBarHeight * 0.5f);
            heartSize += minimap.PanelSpacing;
        }

        RectTransform back = CreateBarRect("life_bar", map, lifeBarBackColor);
        back.anchorMin = back.anchorMax = new Vector2(0.5f, 0.5f);
        back.pivot = Vector2.zero;
        back.sizeDelta = new Vector2(width - heartSize, minimapLifeBarHeight);
        back.anchoredPosition = new Vector2(-width * 0.5f + heartSize, bottom);

        lifeFill = CreateBarRect("life_fill", back, Color.green);
        lifeFill.anchorMin = Vector2.zero;
        lifeFill.anchorMax = Vector2.one;
        lifeFill.offsetMin = lifeFill.offsetMax = Vector2.zero;
        ApplyLife();
    }

    RectTransform FindHeart()
    {
        foreach (Image image in GetComponentsInChildren<Image>(true))
        {
            if (image.name.ToLowerInvariant() == HeartName) return image.rectTransform;
        }
        return null;
    }

    // Centra sotto il controller le prime n caselle (quelle in uso), spostando tutta la fila insieme:
    // con 5 colpi su 8 caselle, centrarle tutte lascerebbe la barra storta a sinistra.
    void CenterLifeOnController(int n)
    {
        Vector2 center = Vector2.zero;
        int count = 0;
        for (int i = 0; i < n; i++)
        {
            if (lifeSegments[i] == null) continue;
            center += (Vector2)lifeSegments[i].rectTransform.localPosition;
            count++;
        }
        if (count == 0) return;

        Vector3 shift = lifeTarget - center / count;
        foreach (Image segment in lifeSegments)
        {
            if (segment != null) segment.rectTransform.localPosition += shift;
        }

        // Il cuoricino sotto la fila, centrato, staccato di qualche unità.
        if (heart != null)
        {
            float segmentHalf = lifeSegments[0].rectTransform.rect.height * lifeSegments[0].rectTransform.localScale.y * 0.5f;
            float heartHalf = heart.rect.height * heart.localScale.y * 0.5f;
            heart.localPosition = lifeTarget - new Vector2(0f, segmentHalf + 3f + heartHalf);
        }
    }

    void ApplyPowers()
    {
        if (controllerTexture != null)
        {
            ApplyButtonLabel(blackHoleLabel, "LSD", blackHoles);
            ApplyButtonLabel(kholeLabel, "K", kholes);
            ApplyButtonLabel(dashLabel, "COKE", dashes);
            ApplyButtonLabel(serotoninLabel, "MDMA", serotonins);
            return;
        }

        ApplyCounter(blackHoleBox, blackHoleLabel, blackHoles, blackHoleColor);
        ApplyCounter(kholeBox, kholeLabel, kholes, kholeColor);
        ApplyCounter(dashBox, dashLabel, dashes, dashColor);
        ApplyCounter(serotoninBox, serotoninLabel, serotonins, serotoninColor);
    }

    void ApplyCounter(Image box, Text label, int count, Color fullColor)
    {
        if (box == null) return;
        box.color = count > 0 ? fullColor : blackHoleEmptyColor;
        label.text = "x" + count;
    }

    // Tutto maiuscolo; cambia solo il numero, la scritta resta sempre uguale (colore in CreateButtonLabel).
    static void ApplyButtonLabel(Text label, string drug, int count)
    {
        if (label == null) return;
        label.text = drug + " X" + count;
    }

    void ApplyCounters()
    {
        if (speedLabel != null) speedLabel.text = Mathf.RoundToInt(Mathf.Max(0f, speed)).ToString();
    }

    // comodo per test in editor
    void OnValidate()
    {
        if (!Application.isPlaying) return;
        ApplyLife();
        ApplyPowers();
        ApplyCounters();
    }
}
