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
    public Text dashCountLabel;            // contatore scatti (fulmine)
    public Text speedLabel;                // opzionale: km/h

    [Header("Range lancetta")]
    public float emptyAngle = -90f;        // E
    public float fullAngle  =  90f;        // F
    [Tooltip("Velocità di inseguimento della lancetta (unità/s). 0 = istantanea.")]
    public float needleLerp = 6f;
    [Tooltip("Vibrazione in gradi, simula l'ago meccanico.")]
    public float needleJitter = 1f;

    [Header("Colori barra vita")]
    public Color lifeHigh = new Color32(0x7F, 0x9A, 0x4E, 0xFF);
    public Color lifeMid  = new Color32(0xD9, 0xA5, 0x3C, 0xFF);
    public Color lifeLow  = new Color32(0xC8, 0x45, 0x2A, 0xFF);
    public Color lifeOff  = new Color32(0x3A, 0x35, 0x2C, 0xCC);

    // ---- stato letto dal gioco (0..1 per fuel e life) ----
    [Range(0f, 1f)] public float fuel01 = 0.38f;
    [Range(0f, 1f)] public float life01 = 0.75f;
    public int dashCount = 5;
    public float speed = 0f;

    float shownFuel;

    void Start()
    {
        shownFuel = fuel01;
        ApplyLife();
        ApplyCounters();
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

    public void SetDashCount(int n)                 { dashCount = Mathf.Max(0, n); ApplyCounters(); }
    public void SetSpeed(float kmh)                 { speed = kmh; ApplyCounters(); }

    // === rendering ===============================================
    void ApplyLife()
    {
        if (lifeSegments == null || lifeSegments.Length == 0) return;
        int n   = lifeSegments.Length;
        int lit = Mathf.CeilToInt(life01 * n);
        Color on = life01 > 0.62f ? lifeHigh : (life01 > 0.32f ? lifeMid : lifeLow);
        for (int i = 0; i < n; i++)
        {
            if (lifeSegments[i] == null) continue;
            lifeSegments[i].color = i < lit ? on : lifeOff;
        }
    }

    void ApplyCounters()
    {
        if (dashCountLabel != null) dashCountLabel.text = Mathf.Clamp(dashCount, 0, 99).ToString();
        if (speedLabel != null)     speedLabel.text = Mathf.RoundToInt(Mathf.Max(0f, speed)).ToString();
    }

    // comodo per test in editor
    void OnValidate()
    {
        if (!Application.isPlaying) return;
        ApplyLife();
        ApplyCounters();
    }
}
