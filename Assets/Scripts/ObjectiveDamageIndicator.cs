using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Avviso a schermo quando Console o SoundSystem vengono colpiti: una freccia rossa col nome
// dell'obiettivo, che pulsa e resta finché lo picchiano (più showSeconds dall'ultimo colpo).
//  - obiettivo fuori schermo: la freccia sta sul bordo, dalla parte dell'obiettivo, e punta
//    verso di lui. Così dal SoundSystem si vede subito che stanno picchiando la Console;
//  - obiettivo in vista: la freccia sta sopra di lui e punta in basso.
//
// Non serve che gli obiettivi chiamino niente: si guarda ogni fotogramma RemainingHits, e un
// calo vuol dire un colpo. Si aggiunge da solo all'avvio (come ObjectiveThreatLabel): niente da
// collegare in scena.
public class ObjectiveDamageIndicator : MonoBehaviour
{
    [Tooltip("Secondi in cui l'avviso resta acceso dopo l'ultimo colpo.")]
    [SerializeField] private float showSeconds = 2.5f;
    [Tooltip("Distanza della freccia dal bordo dello schermo, in pixel.")]
    [SerializeField] private float edgeMargin = 50f;
    [SerializeField] private float arrowSize = 40f;
    [SerializeField] private int fontSize = 16;
    [SerializeField] private Color color = new Color(1f, 0.12f, 0.1f, 1f);
    [Tooltip("Pulsazioni al secondo.")]
    [SerializeField] private float pulseSpeed = 4f;
    [Tooltip("Di quanto sopra la cima dell'obiettivo sta la freccia quando è in vista (unità di mondo).")]
    [SerializeField] private float heightAboveTop = 1.2f;

    private class Tracked
    {
        public string Name;
        public Transform Objective;
        public System.Func<int> RemainingHits;
        public Vector3 Anchor; // punto sopra il centro dell'obiettivo (non si muovono)
        public int LastHits;
        public float ShowUntil;
        public RectTransform Marker;
        public RectTransform Arrow;
        public Image ArrowImage;
        public Text Label;
    }

    private readonly List<Tracked> tracked = new List<Tracked>();
    private Canvas canvas;
    private static Sprite arrowSprite;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (FindAnyObjectByType<ObjectiveDamageIndicator>() == null)
        {
            new GameObject("ObjectiveDamageIndicator").AddComponent<ObjectiveDamageIndicator>();
        }
    }

    void Start()
    {
        canvas = new GameObject("ObjectiveDamageIndicatorCanvas", typeof(Canvas)).GetComponent<Canvas>();
        canvas.transform.SetParent(transform, false);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50; // sopra l'HUD: deve saltare all'occhio

        foreach (DJConsoleHealth console in FindObjectsByType<DJConsoleHealth>(FindObjectsSortMode.None))
        {
            DJConsoleHealth c = console;
            Track("CONSOLE", c.transform, () => c.RemainingHits);
        }
        foreach (SoundSystem soundSystem in FindObjectsByType<SoundSystem>(FindObjectsSortMode.None))
        {
            SoundSystem s = soundSystem;
            Track("SOUND", s.transform, () => s.RemainingHits);
        }
    }

    private void Track(string name, Transform objective, System.Func<int> remainingHits)
    {
        Tracked t = new Tracked
        {
            Name = name,
            Objective = objective,
            RemainingHits = remainingHits,
            Anchor = MeasureTop(objective) + Vector3.up * heightAboveTop,
            LastHits = remainingHits(),
        };
        CreateMarker(t);
        tracked.Add(t);
    }

    void LateUpdate()
    {
        Camera cam = Camera.main;
        foreach (Tracked t in tracked)
        {
            if (t.Objective == null)
            {
                t.Marker.gameObject.SetActive(false);
                continue;
            }

            int hits = t.RemainingHits();
            if (hits < t.LastHits)
            {
                t.ShowUntil = Time.time + showSeconds;
            }
            t.LastHits = hits;

            bool show = Time.time < t.ShowUntil && cam != null;
            t.Marker.gameObject.SetActive(show);
            if (show)
            {
                Place(t, cam);
            }
        }
    }

    private void Place(Tracked t, Camera cam)
    {
        Vector3 screen = cam.WorldToScreenPoint(t.Anchor);
        Vector2 center = new Vector2(Screen.width, Screen.height) * 0.5f;
        bool behind = screen.z < 0f;
        Vector2 point = new Vector2(screen.x, screen.y);
        if (behind)
        {
            // Dietro la camera la proiezione esce ribaltata: la si rigira attorno al centro.
            point = center - (point - center);
        }

        bool onScreen = !behind
            && point.x > edgeMargin && point.x < Screen.width - edgeMargin
            && point.y > edgeMargin && point.y < Screen.height - edgeMargin;

        float angle; // verso in cui punta la freccia, in gradi (0 = destra)
        Vector2 position;
        if (onScreen)
        {
            position = point;
            angle = -90f; // in basso, verso l'obiettivo
        }
        else
        {
            // Sul bordo, lungo la retta dal centro dello schermo verso l'obiettivo.
            Vector2 dir = point - center;
            if (dir.sqrMagnitude < 0.01f)
            {
                dir = Vector2.up;
            }
            Vector2 half = center - Vector2.one * edgeMargin;
            float scale = Mathf.Min(
                Mathf.Abs(dir.x) > 0.001f ? half.x / Mathf.Abs(dir.x) : float.MaxValue,
                Mathf.Abs(dir.y) > 0.001f ? half.y / Mathf.Abs(dir.y) : float.MaxValue);
            position = center + dir * scale;
            angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        }

        t.Marker.position = position;
        t.Arrow.localRotation = Quaternion.Euler(0f, 0f, angle);
        // La scritta sta dalla parte opposta alla punta, così la freccia non la copre.
        Vector2 back = -new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
        t.Label.rectTransform.anchoredPosition = back * (arrowSize * 0.85f);

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * pulseSpeed * Mathf.PI * 2f);
        t.Arrow.localScale = Vector3.one * (0.9f + 0.25f * pulse);
        Color c = color;
        c.a = 0.65f + 0.35f * pulse;
        t.ArrowImage.color = c;
        t.Label.color = c;
    }

    private void CreateMarker(Tracked t)
    {
        GameObject markerObject = new GameObject(t.Name + "_DamageMarker", typeof(RectTransform));
        markerObject.transform.SetParent(canvas.transform, false);
        t.Marker = (RectTransform)markerObject.transform;
        t.Marker.sizeDelta = Vector2.zero;

        GameObject arrowObject = new GameObject("Arrow", typeof(RectTransform), typeof(Image), typeof(Outline));
        arrowObject.transform.SetParent(t.Marker, false);
        t.Arrow = (RectTransform)arrowObject.transform;
        t.Arrow.sizeDelta = Vector2.one * arrowSize;
        t.ArrowImage = arrowObject.GetComponent<Image>();
        t.ArrowImage.sprite = GetArrowSprite();
        t.ArrowImage.raycastTarget = false;
        Outline arrowOutline = arrowObject.GetComponent<Outline>();
        arrowOutline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        arrowOutline.effectDistance = new Vector2(2f, -2f);

        GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(Outline));
        labelObject.transform.SetParent(t.Marker, false);
        t.Label = labelObject.GetComponent<Text>();
        t.Label.text = t.Name + "!";
        t.Label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.Label.fontSize = fontSize;
        t.Label.fontStyle = FontStyle.Bold;
        t.Label.alignment = TextAnchor.MiddleCenter;
        t.Label.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.Label.raycastTarget = false;
        t.Label.rectTransform.sizeDelta = new Vector2(160f, 40f);
        Outline labelOutline = labelObject.GetComponent<Outline>();
        labelOutline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        labelOutline.effectDistance = new Vector2(2f, -2f);

        markerObject.SetActive(false);
    }

    // Centro in pianta e cima dell'obiettivo, da tutti i suoi collider (come ObjectiveThreatLabel:
    // il SoundSystem ha la radice su una delle casse, non in mezzo).
    private static Vector3 MeasureTop(Transform objective)
    {
        Collider[] colliders = objective.GetComponentsInChildren<Collider>();
        if (colliders.Length == 0)
        {
            return objective.position;
        }

        Bounds bounds = colliders[0].bounds;
        foreach (Collider c in colliders)
        {
            bounds.Encapsulate(c.bounds);
        }
        return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
    }

    // Freccia bianca piena che punta a destra (il colore lo dà l'Image), disegnata una volta sola.
    private static Sprite GetArrowSprite()
    {
        if (arrowSprite != null)
        {
            return arrowSprite;
        }

        const int N = 64;
        Texture2D texture = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "DamageArrow", wrapMode = TextureWrapMode.Clamp };
        Color[] pixels = new Color[N * N];
        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                // Triangolo con la punta a destra (x = 1) e la base a sinistra (x = 0.15).
                float u = (x + 0.5f) / N, v = (y + 0.5f) / N - 0.5f;
                float halfHeight = (1f - u) / 0.85f * 0.45f;
                float inside = Mathf.Min(u - 0.15f, halfHeight - Mathf.Abs(v));
                pixels[y * N + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(inside * N * 0.7f));
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();

        arrowSprite = Sprite.Create(texture, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
        return arrowSprite;
    }
}
