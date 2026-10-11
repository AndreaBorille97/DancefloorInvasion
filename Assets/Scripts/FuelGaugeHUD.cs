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
    [Tooltip("Il vecchio contatore scatti (fulmine): non serve più, al suo posto c'è la barra stamina. Viene nascosto all'avvio insieme all'icona del fulmine.")]
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
    [Tooltip("Colore dei segmenti ancora pieni: uno per ogni colpo che il Player può ancora subire.")]
    public Color lifeOn  = Color.white;
    [Tooltip("Colore dei segmenti consumati. Alpha 0 = spariscono del tutto (è il comportamento voluto); alzando l'alpha resterebbero visibili come traccia spenta.")]
    public Color lifeOff = new Color(1f, 1f, 1f, 0f);

    [Header("Barra stamina")]
    [Tooltip("Colore della parte piena della barra stamina.")]
    public Color staminaColor = new Color(1f, 0.82f, 0.2f, 1f);
    [Tooltip("Colore del fondo della barra, cioè della stamina già consumata.")]
    public Color staminaBackColor = new Color(0f, 0f, 0f, 0.55f);
    [Tooltip("Altezza della barra, nelle stesse unità dei segmenti vita.")]
    public float staminaBarHeight = 10f;
    [Tooltip("Spazio fra il fondo dei segmenti vita e la barra.")]
    public float staminaBarGap = 6f;

    [Header("Buchi neri")]
    [Tooltip("Colore del rettangolo dei buchi neri quando ce n'è almeno uno da lanciare.")]
    public Color blackHoleColor = new Color(0.55f, 0.2f, 0.85f, 1f);
    [Tooltip("Colore del rettangolo a tasca vuota.")]
    public Color blackHoleEmptyColor = new Color(0f, 0f, 0f, 0.55f);
    [Tooltip("Misure del rettangolo, nelle stesse unità dei segmenti vita.")]
    public Vector2 blackHoleBoxSize = new Vector2(60f, 24f);
    [Tooltip("Spazio fra la barra stamina e il rettangolo.")]
    public float blackHoleBoxGap = 6f;

    // ---- stato letto dal gioco (0..1 per fuel, life e stamina) ----
    [Range(0f, 1f)] public float fuel01 = 0.38f;
    [Range(0f, 1f)] public float life01 = 0.75f;
    [Range(0f, 1f)] public float stamina01 = 1f;
    public float speed = 0f;
    public int blackHoles = 0;

    float shownFuel;
    RectTransform staminaBar;
    RectTransform staminaFill;
    Image blackHoleBox;
    Text blackHoleLabel;

    // Nomi degli oggetti sotto l'HUD da cui vengono ritrovati i riferimenti mancanti.
    const string NeedleName = "needle";
    const string SegmentName = "life_segment";
    const string BoltName = "bolt";

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
        BuildStaminaBar();
        BuildBlackHoleBox();
        ApplyLife();
        ApplyStamina();
        ApplyBlackHoles();
        ApplyCounters();
    }

    // Il contatore scatti non ha più senso: gli scatti non si contano più, si pagano in stamina.
    void HideDashCounter()
    {
        if (dashCountLabel != null) dashCountLabel.gameObject.SetActive(false);

        foreach (Image image in GetComponentsInChildren<Image>(true))
        {
            if (image.name.ToLowerInvariant().Contains(BoltName)) image.gameObject.SetActive(false);
        }
    }

    // La barra stamina viene costruita qui e non sta in scena: così si mette da sola sotto i
    // segmenti vita, larga quanto loro, dovunque sia stato spostato il gauge, e non c'è niente da
    // ricollegare a mano se l'HUD viene di nuovo recuperato da un backup (vedi ResolveMissingRefs).
    //
    // È un fondo scuro con dentro un rettangolo colorato che si accorcia da destra: la larghezza
    // del rettangolo pieno è la stamina rimasta.
    void BuildStaminaBar()
    {
        if (!SegmentsAreComplete()) return;

        RectTransform parent = lifeSegments[0].rectTransform.parent as RectTransform;
        if (parent == null) return;

        // Ingombro di tutti i segmenti nello spazio del loro genitore.
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
        }

        RectTransform back = CreateBarRect("stamina_bar", parent, staminaBackColor);
        back.anchorMin = back.anchorMax = new Vector2(0.5f, 0.5f);
        back.pivot = new Vector2(0f, 1f);
        back.sizeDelta = new Vector2(max.x - min.x, staminaBarHeight);
        back.localPosition = new Vector3(min.x, min.y - staminaBarGap, 0f);
        staminaBar = back;

        staminaFill = CreateBarRect("stamina_fill", back, staminaColor);
        staminaFill.anchorMin = Vector2.zero;
        staminaFill.anchorMax = Vector2.one;
        staminaFill.offsetMin = staminaFill.offsetMax = Vector2.zero;
    }

    // Segnaposto per i buchi neri in tasca (vedi PlayerBlackHoleThrow): un rettangolo sotto la
    // barra stamina, allineato a sinistra, con scritto quanti ce ne sono. Viola quando se ne può
    // lanciare almeno uno, scuro a tasca vuota. Costruito qui per lo stesso motivo della barra.
    void BuildBlackHoleBox()
    {
        if (staminaBar == null) return;

        RectTransform box = CreateBarRect("black_hole_box", staminaBar.parent, blackHoleEmptyColor);
        box.anchorMin = box.anchorMax = staminaBar.anchorMin;
        box.pivot = new Vector2(0f, 1f);
        box.sizeDelta = blackHoleBoxSize;
        box.localPosition = staminaBar.localPosition + new Vector3(0f, -(staminaBarHeight + blackHoleBoxGap), 0f);
        blackHoleBox = box.GetComponent<Image>();

        GameObject labelObject = new GameObject("black_hole_count", typeof(RectTransform), typeof(Text));
        labelObject.transform.SetParent(box, false);
        RectTransform labelRect = (RectTransform)labelObject.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;

        blackHoleLabel = labelObject.GetComponent<Text>();
        blackHoleLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        blackHoleLabel.alignment = TextAnchor.MiddleCenter;
        blackHoleLabel.fontStyle = FontStyle.Bold;
        blackHoleLabel.resizeTextForBestFit = true;
        blackHoleLabel.resizeTextMinSize = 6;
        blackHoleLabel.resizeTextMaxSize = 40;
        blackHoleLabel.color = Color.white;
        blackHoleLabel.raycastTarget = false;
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

    public void SetStamina(float current, float max) { stamina01 = max <= 0f ? 0f : Mathf.Clamp01(current / max); ApplyStamina(); }
    public void SetSpeed(float kmh)                 { speed = kmh; ApplyCounters(); }
    public void SetBlackHoles(int count)            { blackHoles = Mathf.Max(0, count); ApplyBlackHoles(); }

    // === rendering ===============================================
    // Un segmento per colpo: quelli ancora pieni restano com'è messo lifeOn (bianchi), quelli
    // consumati passano a lifeOff, che di suo è trasparente — quindi spariscono.
    //
    // Prima il colore dipendeva dalla vita residua (verde/giallo/rosso) e i segmenti consumati
    // restavano visibili in grigio. Tolto: con la barra da 8 e un colpo per segmento, è la
    // QUANTITÀ di segmenti a dire quanta vita resta, e cambiare anche il colore non aggiunge
    // niente — anzi, facendo cambiare tinta a tutta la barra ad ogni soglia distrae dall'unica
    // cosa che è cambiata davvero, cioè che un segmento se n'è andato.
    void ApplyLife()
    {
        if (lifeSegments == null || lifeSegments.Length == 0) return;
        int n   = lifeSegments.Length;
        int lit = Mathf.CeilToInt(life01 * n);
        for (int i = 0; i < n; i++)
        {
            if (lifeSegments[i] == null) continue;
            lifeSegments[i].color = i < lit ? lifeOn : lifeOff;
        }
    }

    void ApplyStamina()
    {
        if (staminaFill == null) return;
        staminaFill.anchorMax = new Vector2(stamina01, 1f);
    }

    void ApplyBlackHoles()
    {
        if (blackHoleBox == null) return;
        blackHoleBox.color = blackHoles > 0 ? blackHoleColor : blackHoleEmptyColor;
        blackHoleLabel.text = "x" + blackHoles;
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
        ApplyStamina();
        ApplyBlackHoles();
        ApplyCounters();
    }
}
