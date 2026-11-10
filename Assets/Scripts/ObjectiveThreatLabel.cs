using UnityEngine;
using UnityEngine.UI;

// Contatore sopra Console e SoundSystem: la probabilità che uno Sbirro punti dritto a
// quell'obiettivo invece di fermarsi alle guardie (EnemyChase.GetDirectAttackChance, cioè
// guardie a terra / guardie totali). Verde a 0%, rosso a 100%.
//
// È un'etichetta a schermo (Canvas overlay) che segue l'obiettivo, non una scritta nel mondo:
// resta sempre leggibile, della stessa misura e mai coperta da altro.
//
// Spenta: la percentuale ora sta nella minimappa, accanto alla vita (vedi MinimapUI). Per
// riaverla anche sopra gli obiettivi basta rimettere a true autoAttach, e si aggiunge da sola
// a ogni Console e SoundSystem all'avvio.
public class ObjectiveThreatLabel : MonoBehaviour
{
    private static readonly bool autoAttach = false; // non const: con const il resto del metodo darebbe l'avviso "codice irraggiungibile"

    [Tooltip("Quanto sopra la cima dell'obiettivo sta l'etichetta, in unità di mondo.")]
    [SerializeField] private float heightAboveTop = 1f;
    [Tooltip("Ogni quanti secondi ricalcola la probabilità.")]
    [SerializeField] private float refreshInterval = 0.25f;
    [Tooltip("Distanza minima dal bordo dello schermo, in pixel: l'etichetta non esce mai dallo schermo.")]
    [SerializeField] private float screenMargin = 10f;
    [SerializeField] private int fontSize = 24;
    [SerializeField] private Color safeColor = new Color(0.3f, 1f, 0.4f, 1f);
    [SerializeField] private Color dangerColor = new Color(1f, 0.2f, 0.15f, 1f);

    private static Canvas sharedCanvas;
    private Text label;
    private Vector3 anchor; // punto del mondo sopra l'obiettivo, preso una volta (gli obiettivi non si muovono)
    private float refreshTimer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToObjectives()
    {
        if (!autoAttach)
        {
            return;
        }

        foreach (DJConsoleHealth console in FindObjectsByType<DJConsoleHealth>(FindObjectsSortMode.None))
        {
            Attach(console.gameObject);
        }
        foreach (SoundSystem soundSystem in FindObjectsByType<SoundSystem>(FindObjectsSortMode.None))
        {
            Attach(soundSystem.gameObject);
        }
    }

    private static void Attach(GameObject objective)
    {
        if (objective.GetComponent<ObjectiveThreatLabel>() == null)
        {
            objective.AddComponent<ObjectiveThreatLabel>();
        }
    }

    void Start()
    {
        anchor = MeasureTop() + Vector3.up * heightAboveTop;
        label = CreateLabel();
        Refresh();
    }

    void OnDestroy()
    {
        if (label != null)
        {
            Destroy(label.gameObject);
        }
    }

    void LateUpdate()
    {
        if (label == null)
        {
            return;
        }

        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer <= 0f)
        {
            refreshTimer = refreshInterval;
            Refresh();
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        Vector3 screen = cam.WorldToScreenPoint(anchor);
        label.enabled = screen.z > 0f; // dietro la camera: non si disegna

        // Gli obiettivi stanno ai bordi alto e basso dell'arena: sopra quello in alto
        // l'etichetta finirebbe fuori dallo schermo. Si tiene dentro, a filo del bordo.
        Vector2 half = label.rectTransform.sizeDelta * 0.5f;
        screen.x = Mathf.Clamp(screen.x, half.x + screenMargin, Screen.width - half.x - screenMargin);
        screen.y = Mathf.Clamp(screen.y, half.y + screenMargin, Screen.height - half.y - screenMargin);
        label.rectTransform.position = screen;
    }

    // Nome dell'obiettivo, percentuale e da dove viene: guardie a terra / guardie totali.
    private void Refresh()
    {
        float chance = EnemyChase.GetDirectAttackChance(transform, out int guardCount, out int downGuards);
        label.text = $"{ObjectiveName}\n{Mathf.RoundToInt(chance * 100f)}%  ({downGuards}/{guardCount})";
        label.color = Color.Lerp(safeColor, dangerColor, chance);
    }

    private string ObjectiveName => GetComponent<SoundSystem>() != null ? "SOUND" : "CONSOLE";

    // Controllo: ogni Raver in scena all'avvio, quando va a terra, scrive in Console per quale
    // obiettivo è stato contato (quelli nati dopo dalle Raver Ammo non scrivono niente).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void LogGuardsGoingDown()
    {
        foreach (RaverHealth raver in FindObjectsByType<RaverHealth>(FindObjectsSortMode.None))
        {
            RaverHealth r = raver;
            r.OnDown += () =>
            {
                Transform objective = EnemyChase.GetGuardedObjective(r);
                string counted = objective == null ? "nessuno (non conta)"
                    : objective.GetComponent<SoundSystem>() != null ? "SOUND" : "CONSOLE";
                Debug.Log($"[Probabilità obiettivi] {r.name} è a terra: contato per {counted}.");
            };
        }
    }

    // Centro in pianta e cima dell'obiettivo, da tutti i suoi collider: il SoundSystem ha la
    // radice su una delle casse (muro4), quindi il suo transform non sta in mezzo.
    private Vector3 MeasureTop()
    {
        Collider[] colliders = GetComponentsInChildren<Collider>();
        if (colliders.Length == 0)
        {
            return transform.position;
        }

        Bounds bounds = colliders[0].bounds;
        foreach (Collider c in colliders)
        {
            bounds.Encapsulate(c.bounds);
        }

        return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
    }

    private Text CreateLabel()
    {
        if (sharedCanvas == null)
        {
            GameObject canvasObject = new GameObject("ObjectiveThreatLabels", typeof(Canvas));
            sharedCanvas = canvasObject.GetComponent<Canvas>();
            sharedCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            sharedCanvas.sortingOrder = -5; // sotto l'HUD
        }

        GameObject labelObject = new GameObject(name + "_threat", typeof(RectTransform), typeof(Text), typeof(Outline));
        labelObject.transform.SetParent(sharedCanvas.transform, false);
        RectTransform rect = (RectTransform)labelObject.transform;
        rect.sizeDelta = new Vector2(220f, 80f);

        Text text = labelObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;

        Outline outline = labelObject.GetComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(2f, -2f);
        return text;
    }
}
