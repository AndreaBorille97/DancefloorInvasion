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
// riquadro, un pannello con due contatori testuali ("Console: X/Y", "SoundSystem: X/Y")
// mostra i colpi rimasti prima della distruzione (vedi RemainingHits/MaxHits).
public class MinimapUI : MonoBehaviour
{
    [Header("Aggiornamento")]
    [Tooltip("Ogni quanti secondi rilegge posizione/vita di tutti gli elementi: non serve farlo ogni frame.")]
    [SerializeField] private float refreshInterval = 0.2f;

    [Header("Sfondo")]
    [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.55f);

    [Header("Colori")]
    [SerializeField] private Color playerColor = Color.white;
    [SerializeField] private Color consoleColor = new Color(1f, 0.85f, 0.2f);
    [SerializeField] private Color soundSystemColor = new Color(1f, 0.55f, 0.1f);
    [SerializeField] private Color sbirroColor = new Color(0.9f, 0.2f, 0.2f);

    [Header("Dimensioni punti")]
    [SerializeField] private float playerDotSize = 10f;
    [Tooltip("Dimensione massima del punto di Console/SoundSystem: scala in giù verso il 40% man mano che la vita scende.")]
    [SerializeField] private float objectiveDotSize = 14f;
    [SerializeField] private float raverDotSize = 6f;
    [SerializeField] private float sbirroDotSize = 6f;

    [Header("Contatori vita Console/SoundSystem")]
    [Tooltip("Pannello con i due contatori testuali (\"Console: X/Y\", \"SoundSystem: X/Y\"), posizionato appena sopra il riquadro della minimappa.")]
    [SerializeField] private float counterPanelHeight = 56f;
    [SerializeField] private float counterPanelSpacing = 4f; // spazio tra il riquadro della minimappa e il pannello contatori sopra (e tra quel pannello e il countdown, vedi CreateCountdownLabel)
    [SerializeField] private int counterFontSize = 22;
    [Tooltip("Colore usato solo quando Console/SoundSystem non esistono ancora (testo \"--\"): a bersaglio vivo il colore è invece dinamico in base alla vita rimasta, vedi GetHealthColor.")]
    [SerializeField] private Color counterTextColor = Color.white;

    [Header("Countdown vittoria")]
    [Tooltip("Pannello col conto alla rovescia dei secondi rimasti per vincere (vedi SurvivalTimer.RemainingSeconds), posizionato sopra il pannello dei contatori vita.")]
    [SerializeField] private float countdownPanelHeight = 30f;
    [SerializeField] private int countdownFontSize = 16;
    [SerializeField] private Color countdownTextColor = Color.white;

    private RectTransform rectTransform;
    private float timer;

    private Image playerIcon;
    private Image consoleIcon;
    private Image soundSystemIcon;
    private readonly List<Image> raverIcons = new List<Image>();
    private readonly List<Image> sbirroIcons = new List<Image>();
    private Text consoleCounter;
    private Text soundSystemCounter;
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

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        UpdateSingleIcon(playerIcon, player != null ? player.transform : null, bounds, playerDotSize);

        DJConsoleHealth console = FindAnyObjectByType<DJConsoleHealth>();
        UpdateSingleIcon(consoleIcon, console != null ? console.transform : null, bounds,
            console != null ? Mathf.Lerp(objectiveDotSize * 0.4f, objectiveDotSize, console.HealthFraction) : objectiveDotSize);
        consoleCounter.text = console != null ? $"Console: {console.RemainingHits}/{console.MaxHits}" : "Console: --";
        consoleCounter.color = console != null ? GetHealthColor(console.HealthFraction) : counterTextColor;

        SoundSystem soundSystem = FindAnyObjectByType<SoundSystem>();
        UpdateSingleIcon(soundSystemIcon, soundSystem != null ? soundSystem.transform : null, bounds,
            soundSystem != null ? Mathf.Lerp(objectiveDotSize * 0.4f, objectiveDotSize, soundSystem.HealthFraction) : objectiveDotSize);
        soundSystemCounter.text = soundSystem != null ? $"SoundSystem: {soundSystem.RemainingHits}/{soundSystem.MaxHits}" : "SoundSystem: --";
        soundSystemCounter.color = soundSystem != null ? GetHealthColor(soundSystem.HealthFraction) : counterTextColor;

        RefreshGroup(FindObjectsByType<RaverHealth>(FindObjectsSortMode.None), raverIcons, "Raver", bounds,
            (raver, icon) =>
            {
                icon.color = GetHealthColor(raver.HealthFraction);
                SetSize(icon, raverDotSize);
            });

        RefreshGroup(FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None), sbirroIcons, "Sbirro", bounds,
            (enemy, icon) =>
            {
                icon.color = sbirroColor;
                SetSize(icon, sbirroDotSize);
            });

        if (survivalTimer != null)
        {
            float remaining = survivalTimer.RemainingSeconds;
            int minutes = Mathf.FloorToInt(remaining / 60f);
            int seconds = Mathf.FloorToInt(remaining % 60f);
            countdownLabel.text = $"{minutes}:{seconds:00}";
        }
        else
        {
            countdownLabel.text = "--:--";
        }
    }

    private void RefreshGroup<T>(T[] items, List<Image> pool, string label, Bounds bounds, Action<T, Image> configure) where T : Component
    {
        for (int i = 0; i < items.Length; i++)
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
        for (int i = items.Length; i < pool.Count; i++)
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
        float panelCenterY = boxTop + counterPanelSpacing + counterPanelHeight * 0.5f;

        GameObject panelObject = new GameObject("HealthCountersBackground", typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(rectTransform, false);

        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = backgroundColor;
        panelImage.raycastTarget = false;

        RectTransform panelRect = panelImage.rectTransform;
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(rectTransform.rect.width, counterPanelHeight);
        panelRect.anchoredPosition = new Vector2(0f, panelCenterY);

        float rowHeight = counterPanelHeight * 0.5f;
        consoleCounter = CreateCounterLabel("ConsoleCounter", panelCenterY + rowHeight * 0.5f, rowHeight);
        soundSystemCounter = CreateCounterLabel("SoundSystemCounter", panelCenterY - rowHeight * 0.5f, rowHeight);

        healthPanelTopY = panelCenterY + counterPanelHeight * 0.5f;
    }

    // Pannello del countdown, impilato subito sopra il pannello dei contatori vita (stesso
    // sfondo scuro semitrasparente, stesso schema di CreateHealthCounters).
    private void CreateCountdownLabel()
    {
        float panelCenterY = healthPanelTopY + counterPanelSpacing + countdownPanelHeight * 0.5f;

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
        countdownLabel.alignment = TextAnchor.MiddleCenter;
        countdownLabel.color = countdownTextColor;
        countdownLabel.raycastTarget = false;

        RectTransform labelRect = countdownLabel.rectTransform;
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.sizeDelta = new Vector2(rectTransform.rect.width - 8f, countdownPanelHeight);
        labelRect.anchoredPosition = new Vector2(0f, panelCenterY);
    }

    private Text CreateCounterLabel(string name, float y, float rowHeight)
    {
        GameObject labelObject = new GameObject(name, typeof(RectTransform), typeof(Text));
        labelObject.transform.SetParent(rectTransform, false);

        Text label = labelObject.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontStyle = FontStyle.Bold;
        // Best Fit invece di un fontSize fisso: "SoundSystem: X/Y" è più lungo di
        // "Console: X/Y" e allo stesso counterFontSize non ci stava (andava a capo e si
        // tagliava fuori dalla riga) — così ognuna delle due etichette usa il più grande
        // corpo che entra nella sua riga, invece di uno unico tarato sulla più corta.
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 10;
        label.resizeTextMaxSize = counterFontSize;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = counterTextColor;
        label.raycastTarget = false;

        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.sizeDelta = new Vector2(rectTransform.rect.width - 8f, rowHeight);
        labelRect.anchoredPosition = new Vector2(0f, y);

        return label;
    }
}
