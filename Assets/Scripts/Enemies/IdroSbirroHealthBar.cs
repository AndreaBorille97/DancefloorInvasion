using UnityEngine;
using UnityEngine.UI;

// Barra della vita sopra l'IdroSbirro: la aggiunge IdroSbirro.Awake, legge EnemyHealth.
// Come ObjectiveThreatLabel è disegnata a schermo (Canvas overlay) e segue il camion, non sta
// nel mondo: resta sempre leggibile, della stessa misura e mai coperta dal modello.
public class IdroSbirroHealthBar : MonoBehaviour
{
    private static readonly Vector2 Size = new Vector2(120f, 14f);
    private const float Border = 2f;
    private const float HeightAboveTop = 1f; // unità di mondo sopra la cima del camion

    private static readonly Color BackColor = new Color(0f, 0f, 0f, 0.75f);
    private static readonly Color FullColor = new Color(0.3f, 1f, 0.4f, 1f);
    private static readonly Color EmptyColor = new Color(1f, 0.2f, 0.15f, 1f);

    private static Canvas sharedCanvas;

    private EnemyHealth health;
    private RectTransform bar;
    private RectTransform fill;
    private Image fillImage;
    private float topOffset; // dal pivot alla cima della sagoma, preso una volta (il camion non cambia forma)

    void Start()
    {
        health = GetComponent<EnemyHealth>();
        topOffset = MeasureTop() - transform.position.y + HeightAboveTop;
        CreateBar();
    }

    void OnDestroy()
    {
        if (bar != null)
        {
            Destroy(bar.gameObject);
        }
    }

    void LateUpdate()
    {
        if (bar == null || health == null)
        {
            return;
        }

        float health01 = health.MaxHealth > 0f ? Mathf.Clamp01(health.CurrentHealth / health.MaxHealth) : 0f;
        fill.anchorMax = new Vector2(health01, 1f);
        fillImage.color = Color.Lerp(EmptyColor, FullColor, health01);

        Camera cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        Vector3 screen = cam.WorldToScreenPoint(transform.position + Vector3.up * topOffset);
        bar.gameObject.SetActive(screen.z > 0f); // dietro la camera: non si disegna
        bar.position = screen;
    }

    private float MeasureTop()
    {
        float top = transform.position.y;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r is MeshRenderer || r is SkinnedMeshRenderer)
            {
                top = Mathf.Max(top, r.bounds.max.y);
            }
        }
        return top;
    }

    private void CreateBar()
    {
        if (sharedCanvas == null)
        {
            GameObject canvasObject = new GameObject("IdroSbirroHealthBars", typeof(Canvas));
            sharedCanvas = canvasObject.GetComponent<Canvas>();
            sharedCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            sharedCanvas.sortingOrder = -5; // sotto l'HUD
        }

        GameObject barObject = new GameObject(name + "_health", typeof(RectTransform), typeof(Image));
        barObject.transform.SetParent(sharedCanvas.transform, false);
        bar = (RectTransform)barObject.transform;
        bar.sizeDelta = Size;
        Image back = barObject.GetComponent<Image>();
        back.color = BackColor;
        back.raycastTarget = false;

        // Il riempimento va da sinistra fino a health01 della larghezza (anchorMax.x, vedi LateUpdate).
        GameObject fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillObject.transform.SetParent(bar, false);
        fill = (RectTransform)fillObject.transform;
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.offsetMin = new Vector2(Border, Border);
        fill.offsetMax = new Vector2(-Border, -Border);
        fillImage = fillObject.GetComponent<Image>();
        fillImage.raycastTarget = false;
    }
}
