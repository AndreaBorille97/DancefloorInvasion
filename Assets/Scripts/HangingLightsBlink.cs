using System.Collections.Generic;
using UnityEngine;

// Attaccalo sul GameObject del BAR1 (o su un suo genitore che contiene le lucine "Bulb0..BulbN").
// Alla partenza cerca tutti i Renderer figli il cui nome inizia per "Bulb", crea per ciascuno
// una vera Light (point light) colorata come il materiale di QUELLA lucina, e le divide in
// due gruppi alternati (pari/dispari lungo il filo) che si scambiano acceso/spento ogni
// blinkInterval secondi: (A on, B off) poi (A off, B on), come le classiche lucine di Natale.
public class HangingLightsBlink : MonoBehaviour
{
    [Tooltip("Prefisso del nome degli oggetti lucina da illuminare (es. le mesh 'Bulb0', 'Bulb1', ...)")]
    public string bulbNamePrefix = "Bulb";

    [Tooltip("Secondi tra un cambio di stato e l'altro. Più basso = lampeggio più veloce.")]
    [Range(0.05f, 5f)]
    public float blinkInterval = 0.5f;

    [Tooltip("Intensità della luce quando è accesa")]
    public float lightIntensity = 1.5f;

    [Tooltip("Raggio d'azione di ogni lucina")]
    public float lightRange = 2f;

    [Tooltip("Ombre proiettate dalle lucine (costoso con tante luci, di solito meglio disattivato)")]
    public bool castShadows = false;

    private struct Bulb
    {
        public Light light;
        public bool groupA;
    }

    private readonly List<Bulb> _bulbs = new List<Bulb>();
    private float _timer;
    private bool _groupAOn = true;

    private void Awake()
    {
        BuildBulbLights();
        ApplyState();
    }

    private void Update()
    {
        if (_bulbs.Count == 0) return;

        _timer += Time.deltaTime;
        if (_timer < blinkInterval) return;

        _timer -= blinkInterval;
        _groupAOn = !_groupAOn;
        ApplyState();
    }

    private void BuildBulbLights()
    {
        _bulbs.Clear();

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        int index = 0;
        foreach (Renderer renderer in renderers)
        {
            if (!renderer.gameObject.name.StartsWith(bulbNamePrefix, System.StringComparison.OrdinalIgnoreCase))
                continue;

            Color color = GetMaterialColor(renderer.sharedMaterial);
            Light light = CreateBulbLight(renderer.transform, color);
            _bulbs.Add(new Bulb { light = light, groupA = index % 2 == 0 });
            index++;
        }
    }

    private void ApplyState()
    {
        foreach (Bulb bulb in _bulbs)
        {
            bool on = bulb.groupA == _groupAOn;
            bulb.light.enabled = on;
        }
    }

    private static Color GetMaterialColor(Material mat)
    {
        if (mat == null) return Color.white;
        if (mat.HasProperty("_BaseColor")) return mat.GetColor("_BaseColor");
        if (mat.HasProperty("_Color")) return mat.GetColor("_Color");
        return Color.white;
    }

    private Light CreateBulbLight(Transform bulb, Color color)
    {
        var go = new GameObject("BulbLight");
        go.transform.SetParent(bulb, false);
        go.transform.localPosition = Vector3.zero;

        Light light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = lightIntensity;
        light.range = lightRange;
        light.shadows = castShadows ? LightShadows.Soft : LightShadows.None;

        return light;
    }
}
