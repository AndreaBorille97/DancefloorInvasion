using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Da mettere su un RectTransform vuoto, figlio del Canvas principale, posizionato e
// dimensionato in basso a destra: il suo stesso RectTransform (Rect Width/Height) è
// l'area su cui vengono proiettate le coordinate X/Z del mondo, secondo i bounds di
// FloorBounds.Instance (vedi PlaceIcon). Aggiunge da sé un'Image di sfondo e un punto
// colorato per ciascuno di Player, Console/DJ, SoundSystem, ogni Raver ed ogni Sbirro,
// letti a intervalli regolari (refreshInterval), non ogni frame. Console/DJ, SoundSystem
// e i Raver mostrano anche quanta vita resta: i primi due scalando la dimensione del
// punto in base a HealthFraction, i Raver sfumando dal verde (vita piena) al giallo al
// rosso (vedi GetHealthColor) sia in piedi che a terra: a terra il colore riflette quanto
// manca alla rianimazione (vedi RaverHealth.HealthFraction), non un grigio fisso. Sopra il
// riquadro, un pannello con due contatori testuali ("Console X/Y", "SoundSystem X/Y")
// mostra i colpi rimasti prima della distruzione (vedi RemainingHits/MaxHits) e, sulla
// stessa riga a destra, la probabilità che uno Sbirro punti dritto a quell'obiettivo
// (EnemyChase.GetDirectAttackChance): verde a 0%, rosso a 100%.
public class MinimapUI : MonoBehaviour
{
    [Header("Aggiornamento")]
    [Tooltip("Ogni quanti secondi rilegge posizione/vita di tutti gli elementi: non serve farlo ogni frame.")]
    [SerializeField] private float refreshInterval = 0.2f;

    [Header("Sfondo")]
    [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.75f);
    [Tooltip("Bordo attorno al riquadro della minimappa, per staccarlo dal gioco dietro.")]
    [SerializeField] private Color borderColor = new Color(1f, 1f, 1f, 0.6f);
    [SerializeField] private float borderThickness = 2f;

    [Header("Colori")]
    [SerializeField] private Color playerColor = Color.white;
    [SerializeField] private Color consoleColor = new Color(1f, 0.85f, 0.2f);
    [SerializeField] private Color soundSystemColor = new Color(1f, 0.55f, 0.1f);
    [SerializeField] private Color sbirroColor = new Color(0.9f, 0.2f, 0.2f);

    [Header("Dimensioni punti")]
    [SerializeField] private float playerDotSize = 14f;
    [Tooltip("Dimensione massima del punto di Console/SoundSystem: scala in giù verso il 40% man mano che la vita scende.")]
    [SerializeField] private float objectiveDotSize = 20f;
    [SerializeField] private float raverDotSize = 9f;
    [SerializeField] private float sbirroDotSize = 9f;

    [Header("Contatori vita Console/SoundSystem")]
    [Tooltip("Pannello con i due contatori testuali (\"Console X/Y\", \"SoundSystem X/Y\"), posizionato appena sopra il riquadro della minimappa.")]
    [SerializeField] private float counterPanelHeight = 68f;
    [SerializeField] private float counterPanelSpacing = 4f; // spazio tra il riquadro della minimappa e il pannello contatori sopra (e tra quel pannello e il countdown, vedi CreateCountdownLabel)
    [SerializeField] private int counterFontSize = 24;
    [Tooltip("Colore usato solo quando Console/SoundSystem non esistono ancora (testo \"--\"): a bersaglio vivo il colore è invece dinamico in base alla vita rimasta, vedi GetHealthColor.")]
    [SerializeField] private Color counterTextColor = Color.white;
    [Tooltip("Parte della riga occupata dalla vita, a sinistra: il resto, a destra, è per la probabilità di attacco.")]
    [Range(0.4f, 0.8f)]
    [SerializeField] private float healthColumnFraction = 0.64f;
    [Tooltip("Riga in cima al pannello con la scritta \"attacco nemico\", sopra la colonna delle percentuali.")]
    [SerializeField] private float threatHeaderHeight = 22f;
    [SerializeField] private int threatHeaderFontSize = 16;
    [SerializeField] private Color threatSafeColor = new Color(0.3f, 1f, 0.4f, 1f);
    [SerializeField] private Color threatDangerColor = new Color(1f, 0.2f, 0.15f, 1f);

    [Header("Countdown vittoria")]
    [Tooltip("Pannello col conto alla rovescia dei secondi rimasti per vincere (vedi SurvivalTimer.RemainingSeconds), posizionato sopra il pannello dei contatori vita.")]
    [SerializeField] private float countdownPanelHeight = 36f;
    [SerializeField] private int countdownFontSize = 24;
    [SerializeField] private Color countdownTextColor = Color.white;

    private RectTransform rectTransform;

    // Cima dell'ultimo pannello impilato sopra il riquadro (il countdown), nelle coordinate locali
    // della minimappa: sopra ci si mette la barra vita del Player (vedi FuelGaugeHUD.BuildLifeBar).
    public float TopY { get; private set; }
    public float PanelSpacing => counterPanelSpacing;
    private float timer;

    private Image playerIcon;
    private Image consoleIcon;
    private Image soundSystemIcon;
    private readonly List<Image> raverIcons = new List<Image>();
    private readonly List<Image> sbirroIcons = new List<Image>();
    private Text consoleCounter;
    private Text soundSystemCounter;
    private Text consoleThreat;
    private Text soundSystemThreat;
    private readonly Dictionary<Text, string> threatTextKeys = new Dictionary<Text, string>(); // ultimo testo scritto, vedi FitThreatText
    // Ultimi valori scritti nei contatori: il testo (una stringa nuova ogni volta) si
    // ricostruisce solo quando cambiano, non ad ogni refresh.
    private int lastConsoleHits = int.MinValue;
    private int lastSoundHits = int.MinValue;
    private int lastCountdownSeconds = int.MinValue;
    private Text countdownLabel;
    private float healthPanelTopY; // bordo superiore del pannello contatori vita, usato per impilarci sopra il countdown

    private SurvivalTimer survivalTimer;

    void Awake()
    {
        rectTransform = (RectTransform)transform;

        CreateBackground();
        playerIcon = CreateIcon("Player", playerColor);
        consoleIcon = CreateIcon("Console", consoleColor);
        soundSystemIcon = CreateIcon("SoundSystem", soundSystemColor);
        CreateHealthCounters();
        CreateCountdownLabel();

        survivalTimer = FindAnyObjectByType<SurvivalTimer>();
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer < refreshInterval)
        {
            return;
        }

        timer = 0f;
        Refresh();
    }

    private void Refresh()
    {
        if (FloorBounds.Instance == null)
        {
            return;
        }

        Bounds bounds = FloorBounds.Instance.Bounds;

        PlayerMovement player = PlayerMovement.Instance;
        UpdateSingleIcon(playerIcon, player != null ? player.transform : null, bounds, playerDotSize);

        // Dal registry invece di FindAnyObjectByType ad ogni refresh: gli obiettivi sono due e
        // non vengono mai distrutti, il giro è una formalità.
        DJConsoleHealth console = null;
        SoundSystem soundSystemFound = null;
        foreach (IObjective objective in ObjectiveRegistry.All)
        {
            if (console == null) console = objective as DJConsoleHealth;
            if (soundSystemFound == null) soundSystemFound = objective as SoundSystem;
        }
        UpdateSingleIcon(consoleIcon, console != null ? console.transform : null, bounds,
            console != null ? Mathf.Lerp(objectiveDotSize * 0.4f, objectiveDotSize, console.HealthFraction) : objectiveDotSize);
        int consoleHits = console != null ? console.RemainingHits : int.MinValue;
        if (consoleHits != lastConsoleHits)
        {
            lastConsoleHits = consoleHits;
            consoleCounter.text = console != null ? $"Console {console.RemainingHits}/{console.MaxHits}" : "Console --";
        }
        consoleCounter.color = console != null ? GetHealthColor(console.HealthFraction) : counterTextColor;
        RefreshThreat(consoleThreat, console != null ? console.transform : null);

        SoundSystem soundSystem = soundSystemFound;
        UpdateSingleIcon(soundSystemIcon, soundSystem != null ? soundSystem.transform : null, bounds,
            soundSystem != null ? Mathf.Lerp(objectiveDotSize * 0.4f, objectiveDotSize, soundSystem.HealthFraction) : objectiveDotSize);
        int soundHits = soundSystem != null ? soundSystem.RemainingHits : int.MinValue;
        if (soundHits != lastSoundHits)
        {
            lastSoundHits = soundHits;
            soundSystemCounter.text = soundSystem != null ? $"SoundSystem {soundSystem.RemainingHits}/{soundSystem.MaxHits}" : "SoundSystem --";
        }
        soundSystemCounter.color = soundSystem != null ? GetHealthColor(soundSystem.HealthFraction) : counterTextColor;
        RefreshThreat(soundSystemThreat, soundSystem != null ? soundSystem.transform : null);
        MatchCounterFontSize();

        RefreshGroup(RaverHealth.All, raverIcons, "Raver", bounds,
            (raver, icon) =>
            {
                icon.color = GetHealthColor(raver.HealthFraction);
                SetSize(icon, raverDotSize);
            });

        RefreshGroup(EnemyHealth.All, sbirroIcons, "Sbirro", bounds,
            (enemy, icon) =>
            {
                icon.color = sbirroColor;
                SetSize(icon, sbirroDotSize);
            });

        // RemainingSeconds non va mai sotto zero (vedi SurvivalTimer), quindi -1 fa da
        // sentinella per "nessun timer in scena".
        int totalSeconds = survivalTimer != null ? Mathf.FloorToInt(survivalTimer.RemainingSeconds) : -1;
        if (totalSeconds != lastCountdownSeconds)
        {
            lastCountdownSeconds = totalSeconds;
            countdownLabel.text = totalSeconds >= 0 ? $"{totalSeconds / 60}:{totalSeconds % 60:00}" : "--:--";
        }
    }

    // Probabilità che uno Sbirro punti dritto all'obiettivo invece di fermarsi alle guardie:
    // guardie a terra / guardie totali, la stessa del tiro a sorte in EnemyChase. Accanto, più
    // piccoli, i due numeri da cui viene.
    private void RefreshThreat(Text threat, Transform objective)
    {
        if (objective == null)
        {
            threat.text = "--";
            threat.color = counterTextColor;
            threatTextKeys.Remove(threat);
            return;
        }

        float chance = EnemyChase.GetDirectAttackChance(objective, out int guardCount, out int downGuards);
        string percent = $"{Mathf.RoundToInt(chance * 100f)}%";
        string counts = $"{downGuards}/{guardCount}";
        FitThreatText(threat, percent, counts);
        threat.color = Color.Lerp(threatSafeColor, threatDangerColor, chance);
    }

    // "45%  4/14", coi numeri a ThreatCountsScale del corpo della percentuale: il corpo più
    // grande (fino a counterFontSize) a cui tutto sta nella colonna, che è stretta. Si ricalcola
    // solo quando il testo cambia.
    private const float ThreatCountsScale = 0.6f;

    private void FitThreatText(Text threat, string percent, string counts)
    {
        string key = percent + "|" + counts;
        if (threatTextKeys.TryGetValue(threat, out string last) && last == key)
        {
            return;
        }
        threatTextKeys[threat] = key;

        float available = threat.rectTransform.rect.width;
        int size = counterFontSize;
        for (; size > 10; size--)
        {
            threat.fontSize = size;
            threat.text = ThreatText(percent, counts, size);
            if (threat.preferredWidth <= available)
            {
                break;
            }
        }

        threat.fontSize = size;
        threat.text = ThreatText(percent, counts, size);
    }

    private static string ThreatText(string percent, string counts, int size)
    {
        return $"{percent}  <size={Mathf.Max(8, Mathf.RoundToInt(size * ThreatCountsScale))}>{counts}</size>";
    }

    private void RefreshGroup<T>(List<T> items, List<Image> pool, string label, Bounds bounds, Action<T, Image> configure) where T : Component
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (i >= pool.Count)
            {
                pool.Add(CreateIcon(label, Color.white));
            }

            Image icon = pool[i];
            icon.gameObject.SetActive(true);
            configure(items[i], icon);
            PlaceIcon(icon.rectTransform, items[i].transform.position, bounds);
        }

        // Meno elementi di prima (Sbirro morto, ecc.): nasconde i punti in eccesso invece di
        // distruggerli, pronti per essere riattivati se il numero torna a salire.
        for (int i = items.Count; i < pool.Count; i++)
        {
            pool[i].gameObject.SetActive(false);
        }
    }

    private void UpdateSingleIcon(Image icon, Transform target, Bounds bounds, float size)
    {
        bool visible = target != null;
        icon.gameObject.SetActive(visible);
        if (!visible)
        {
            return;
        }

        SetSize(icon, size);
        PlaceIcon(icon.rectTransform, target.position, bounds);
    }

    // Mappa la posizione world (X/Z) sull'area del riquadro della minimappa, secondo i
    // bounds del pavimento: (0,0) del pavimento finisce al centro del riquadro.
    private void PlaceIcon(RectTransform icon, Vector3 worldPosition, Bounds bounds)
    {
        float u = Mathf.InverseLerp(bounds.min.x, bounds.max.x, worldPosition.x);
        float v = Mathf.InverseLerp(bounds.min.z, bounds.max.z, worldPosition.z);

        float width = rectTransform.rect.width;
        float height = rectTransform.rect.height;

        icon.anchoredPosition = new Vector2((u - 0.5f) * width, (v - 0.5f) * height);
    }

    private static void SetSize(Image icon, float size)
    {
        icon.rectTransform.sizeDelta = new Vector2(size, size);
    }

    // Verde a vita piena, giallo a metà, rosso vicino alla distruzione: più immediato da
    // leggere al volo rispetto al solo numero bianco fisso di prima.
    private static Color GetHealthColor(float fraction)
    {
        return fraction >= 0.5f
            ? Color.Lerp(Color.yellow, Color.green, (fraction - 0.5f) * 2f)
            : Color.Lerp(Color.red, Color.yellow, fraction * 2f);
    }

    private void CreateBackground()
    {
        GameObject backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(Image));
        backgroundObject.transform.SetParent(rectTransform, false);

        Image background = backgroundObject.GetComponent<Image>();
        background.color = backgroundColor;
        background.raycastTarget = false;

        RectTransform backgroundRect = background.rectTransform;
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;

        CreateBorder();
    }

    // Quattro strisce sottili lungo i lati del riquadro, appena dentro: un'Outline sull'Image di
    // sfondo non basta, perché su un rettangolo semitrasparente si vede solo come un alone.
    private void CreateBorder()
    {
        CreateBorderSide("BorderTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -borderThickness), Vector2.zero);
        CreateBorderSide("BorderBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, borderThickness));
        CreateBorderSide("BorderLeft", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(borderThickness, 0f));
        CreateBorderSide("BorderRight", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-borderThickness, 0f), Vector2.zero);
    }

    private void CreateBorderSide(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject sideObject = new GameObject(name, typeof(RectTransform), typeof(Image));
        sideObject.transform.SetParent(rectTransform, false);

        Image side = sideObject.GetComponent<Image>();
        side.color = borderColor;
        side.raycastTarget = false;

        RectTransform sideRect = side.rectTransform;
        sideRect.anchorMin = anchorMin;
        sideRect.anchorMax = anchorMax;
        sideRect.offsetMin = offsetMin;
        sideRect.offsetMax = offsetMax;
    }

    private Image CreateIcon(string name, Color color)
    {
        GameObject iconObject = new GameObject(name, typeof(RectTransform), typeof(Image));
        iconObject.transform.SetParent(rectTransform, false);

        Image icon = iconObject.GetComponent<Image>();
        icon.color = color;
        icon.raycastTarget = false;

        RectTransform iconRect = icon.rectTransform;
        iconRect.anchorMin = new Vector2(0.5f, 0.5f);
        iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);

        return icon;
    }

    // Pannello con i due contatori, appena sopra il riquadro della minimappa (fuori dal suo
    // stesso sfondo, che copre solo l'area 0..rect.height): stesso sfondo scuro semitrasparente
    // della minimappa, per leggibilità sopra qualunque cosa ci sia dietro in game.
    private void CreateHealthCounters()
    {
        float boxTop = rectTransform.rect.height * 0.5f;
        float panelHeight = counterPanelHeight + threatHeaderHeight;
        float panelCenterY = boxTop + counterPanelSpacing + panelHeight * 0.5f;

        GameObject panelObject = new GameObject("HealthCountersBackground", typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(rectTransform, false);

        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = backgroundColor;
        panelImage.raycastTarget = false;

        RectTransform panelRect = panelImage.rectTransform;
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(rectTransform.rect.width, panelHeight);
        panelRect.anchoredPosition = new Vector2(0f, panelCenterY);

        // In cima, una volta sola, cosa sono le percentuali della colonna di destra. Parte da
        // metà riga così la scritta, più larga della colonna, ci sta senza andare a capo.
        float panelTop = panelCenterY + panelHeight * 0.5f;
        Text header = CreateCounterLabel("ThreatHeader", panelTop - threatHeaderHeight * 0.5f, threatHeaderHeight,
            0.3f, 1f, TextAnchor.MiddleRight, threatHeaderFontSize);
        header.text = "attacco nemico";

        // Ogni riga: vita a sinistra, probabilità di attacco a destra.
        float rowHeight = counterPanelHeight * 0.5f;
        float rowsCenterY = panelTop - threatHeaderHeight - counterPanelHeight * 0.5f;
        float consoleY = rowsCenterY + rowHeight * 0.5f;
        float soundSystemY = rowsCenterY - rowHeight * 0.5f;
        consoleCounter = CreateCounterLabel("ConsoleCounter", consoleY, rowHeight, 0f, healthColumnFraction, TextAnchor.MiddleLeft, counterFontSize);
        soundSystemCounter = CreateCounterLabel("SoundSystemCounter", soundSystemY, rowHeight, 0f, healthColumnFraction, TextAnchor.MiddleLeft, counterFontSize);
        consoleThreat = CreateCounterLabel("ConsoleThreat", consoleY, rowHeight, healthColumnFraction, 1f, TextAnchor.MiddleRight, counterFontSize);
        soundSystemThreat = CreateCounterLabel("SoundSystemThreat", soundSystemY, rowHeight, healthColumnFraction, 1f, TextAnchor.MiddleRight, counterFontSize);

        healthPanelTopY = panelTop;
    }

    // Stesso corpo per le due scritte della vita: il più grande (fino a counterFontSize) a cui
    // la più lunga delle due, di solito "SoundSystem X/Y", sta ancora nella sua colonna.
    private void MatchCounterFontSize()
    {
        float available = consoleCounter.rectTransform.rect.width;
        int size = counterFontSize;
        for (; size > 10; size--)
        {
            consoleCounter.fontSize = size;
            soundSystemCounter.fontSize = size;
            if (consoleCounter.preferredWidth <= available && soundSystemCounter.preferredWidth <= available)
            {
                break;
            }
        }

        consoleCounter.fontSize = size;
        soundSystemCounter.fontSize = size;
    }

    // Pannello del countdown, impilato subito sopra il pannello dei contatori vita (stesso
    // sfondo scuro semitrasparente, stesso schema di CreateHealthCounters).
    private void CreateCountdownLabel()
    {
        float panelCenterY = healthPanelTopY + counterPanelSpacing + countdownPanelHeight * 0.5f;
        TopY = panelCenterY + countdownPanelHeight * 0.5f;

        GameObject panelObject = new GameObject("CountdownBackground", typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(rectTransform, false);

        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = backgroundColor;
        panelImage.raycastTarget = false;

        RectTransform panelRect = panelImage.rectTransform;
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(rectTransform.rect.width, countdownPanelHeight);
        panelRect.anchoredPosition = new Vector2(0f, panelCenterY);

        GameObject labelObject = new GameObject("CountdownLabel", typeof(RectTransform), typeof(Text));
        labelObject.transform.SetParent(rectTransform, false);

        countdownLabel = labelObject.GetComponent<Text>();
        countdownLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        countdownLabel.fontSize = countdownFontSize;
        countdownLabel.fontStyle = FontStyle.Bold;
        countdownLabel.alignment = TextAnchor.MiddleCenter;
        countdownLabel.color = countdownTextColor;
        countdownLabel.raycastTarget = false;

        RectTransform labelRect = countdownLabel.rectTransform;
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.sizeDelta = new Vector2(rectTransform.rect.width - 8f, countdownPanelHeight);
        labelRect.anchoredPosition = new Vector2(0f, panelCenterY);
        AddTextOutline(labelObject);
    }

    // Contorno scuro sotto le scritte: restano leggibili anche quando il colore (giallo,
    // verde chiaro) è vicino a quello di ciò che c'è dietro.
    private static void AddTextOutline(GameObject labelObject)
    {
        Outline outline = labelObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
    }

    // Etichetta su una riga del pannello contatori, fra le frazioni from..to della sua larghezza.
    // Niente Best Fit: Console e SoundSystem lo farebbero a corpi diversi (la seconda scritta è
    // più lunga), le loro misure le allinea MatchCounterFontSize.
    private Text CreateCounterLabel(string name, float y, float rowHeight, float from, float to, TextAnchor alignment, int fontSize)
    {
        GameObject labelObject = new GameObject(name, typeof(RectTransform), typeof(Text));
        labelObject.transform.SetParent(rectTransform, false);

        Text label = labelObject.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontStyle = FontStyle.Bold;
        label.fontSize = fontSize;
        label.horizontalOverflow = HorizontalWrapMode.Overflow; // mai a capo: al massimo sborda di poco
        label.alignment = alignment;
        label.color = counterTextColor;
        label.raycastTarget = false;

        const float padding = 8f;
        float width = rectTransform.rect.width;
        float left = from * width + padding;
        float right = to * width - padding;

        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.sizeDelta = new Vector2(right - left, rowHeight);
        labelRect.anchoredPosition = new Vector2((left + right) * 0.5f - width * 0.5f, y);
        AddTextOutline(labelObject);

        return label;
    }
}
