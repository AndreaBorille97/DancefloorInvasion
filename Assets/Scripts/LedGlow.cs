using System;
using System.Collections.Generic;
using UnityEngine;

// Da mettere sul GameObject del modello del sound system (o su un suo genitore): illumina i
// dintorni di ogni LED (le mesh "LED_Cassa_01".."LED_Cassa_04", una per colonna).
//
// Perché serve: l'emissione di un materiale URP Lit (Assets/Material/LED_Blu.mat) accende il
// LED ma non illumina nient'altro - in realtime l'emissive non contribuisce all'illuminazione
// dei dintorni, quella arriva solo baked o da una vera Light.
//
// Perché sono SPOT e non point light: il LED è una lastra sottile incollata alla faccia
// frontale della cassa, che dietro ha tutto il corpo del mobile (la placca sta a z 45..48, il
// cassone va da -48 a +48). Una point light irradia in tutte le direzioni e, con le ombre
// spente, passa attraverso il pannello e accende l'interno degli scomparti: sembra una luce
// dentro il mobile invece che un LED sulla facciata. Uno spot largo puntato in fuori emette
// solo in avanti, che è poi quello che fa un pannello luminoso.
//
// Perché sono PIÙ luci per LED: deve essere luminosa tutta la mesh, e la lastra è grossa (~3 x 7
// unità di scena una volta scalato il prop). URP non ha area light realtime - le Rectangle sono
// solo baked - quindi l'unico modo di avere una sorgente con la forma e la dimensione del
// pannello è approssimarla, spargendo una griglia di luci deboli su tutta la sua superficie.
// Il pannello acceso in sé non lo fanno queste luci: lo fa il materiale Unlit HDR che ci metti
// sopra (Assets/Material/LED_Blu.mat), che rende ogni pixel della mesh emissivo a colore pieno.
public class LedGlow : MonoBehaviour
{
    [Tooltip("Prefisso del nome degli oggetti LED da illuminare (le mesh 'LED_Cassa_01', ...).")]
    public string ledNamePrefix = "LED_Cassa";

    [Tooltip("Quante luci sul lato lungo del pannello. Quelle sul lato corto le ricavo in " +
             "proporzione, così la griglia copre tutta la mesh invece di una riga sola. " +
             "È anche il primo valore da abbassare se le luci in scena diventano troppe.")]
    [Range(1, 12)]
    public int lightsAlongLed = 4;

    [Tooltip("Intensità di OGNI luce della griglia (non il totale del LED): alzando il numero " +
             "di luci qui sopra il pannello diventa anche complessivamente più luminoso.")]
    public float lightIntensity = 0.3f;

    [Tooltip("Raggio d'azione di ogni luce, in unità di scena: tienilo corto, deve schiarire " +
             "solo i dintorni immediati del pannello.")]
    public float lightRange = 2.5f;

    [Tooltip("Apertura del cono in gradi: largo (120-160) perché la luce scivoli anche di lato " +
             "sulla facciata, come il bagliore di un pannello, invece di fare un faro.")]
    [Range(20f, 179f)]
    public float spotAngle = 140f;

    [Tooltip("Di quanto staccare le luci dalla superficie del LED, in unità di scena.")]
    public float offsetFromSurface = 0.1f;

    [Tooltip("Da che parte guarda il LED: normalmente è il lato opposto al corpo della cassa e " +
             "viene ricavato da solo. Spunta questa casella se il verso risulta invertito.")]
    public bool invertiDirezione;

    [Tooltip("Ombre proiettate dai LED (costoso con più luci in scena, di solito meglio disattivato).")]
    public bool castShadows = false;

    private void Awake()
    {
        var leds = new List<Renderer>();
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.gameObject.name.StartsWith(ledNamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                leds.Add(renderer);
            }
        }

        if (leds.Count == 0)
        {
            Debug.LogWarning($"[{nameof(LedGlow)}] {name}: nessuna mesh che inizi per " +
                             $"'{ledNamePrefix}' fra i figli, nessuna luce creata.", this);
            return;
        }

        Vector3 propCenter = PropCenter();
        foreach (Renderer led in leds)
        {
            CreateLedLights(led, propCenter);
        }
    }

    // Centro dell'ingombro di tutto il prop, il riferimento per capire da che parte è il "fuori".
    private Vector3 PropCenter()
    {
        Bounds bounds = default;
        bool started = false;
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!started)
            {
                bounds = renderer.bounds;
                started = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
        return started ? bounds.center : transform.position;
    }

    private void CreateLedLights(Renderer led, Vector3 propCenter)
    {
        Color color = GetLedColor(led.sharedMaterial);
        Transform anchor = led.transform;

        // Il LED è una lastra: dei suoi bounds, l'asse più corto è lo spessore (la normale
        // della facciata) e gli altri due sono i lati della superficie da coprire.
        Bounds local = led.localBounds;
        Vector3 size = local.size;
        int thinAxis = 0, longAxis = 0;
        for (int axis = 1; axis < 3; axis++)
        {
            if (size[axis] < size[thinAxis]) thinAxis = axis;
            if (size[axis] >= size[longAxis]) longAxis = axis;
        }
        // Con una mesh cubica i due assi sarebbero ambigui e potrebbero cadere sullo stesso
        // indice: qui servono comunque tre assi distinti, altrimenti crossAxis va fuori range.
        if (longAxis == thinAxis) longAxis = (thinAxis + 1) % 3;
        int crossAxis = 3 - thinAxis - longAxis;   // il terzo asse: 0+1+2 = 3

        // TransformVector/TransformPoint e non i valori locali: così rotazione e scala del
        // modello in scena sono già dentro, e la griglia resta incollata al pannello comunque
        // sia orientato e ridimensionato.
        Vector3 alongLocal = Vector3.zero;
        alongLocal[longAxis] = size[longAxis];
        Vector3 along = anchor.TransformVector(alongLocal);

        Vector3 crossLocal = Vector3.zero;
        crossLocal[crossAxis] = size[crossAxis];
        Vector3 cross = anchor.TransformVector(crossLocal);

        Vector3 normalLocal = Vector3.zero;
        normalLocal[thinAxis] = 1f;
        Vector3 normal = anchor.TransformDirection(normalLocal).normalized;

        Vector3 center = anchor.TransformPoint(local.center);

        // Il pannello guarda dalla parte opposta al corpo del prop: il LED e' incollato alla
        // facciata, il grosso della cassa sta dietro di lui.
        Vector3 outward = Vector3.Dot(center - propCenter, normal) < 0f ? -normal : normal;
        if (invertiDirezione) outward = -outward;

        float halfThickness = anchor.TransformVector(normalLocal * size[thinAxis]).magnitude * 0.5f;
        Vector3 surface = center + outward * (halfThickness + offsetFromSurface);

        // Griglia proporzionata al pannello: le luci sul lato corto sono quelle sul lato lungo
        // ridotte nello stesso rapporto, così restano equidistanti in entrambe le direzioni
        // invece di addensarsi su una.
        int rows = Mathf.Max(1, lightsAlongLed);
        float ratio = size[longAxis] > 0f ? size[crossAxis] / size[longAxis] : 1f;
        int columns = Mathf.Max(1, Mathf.RoundToInt(rows * ratio));

        int index = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                // Ogni luce al centro di una cella: distribuite su tutta la lastra senza che
                // quelle di bordo finiscano a cavallo del perimetro.
                float offsetAlong = (r + 0.5f) / rows - 0.5f;
                float offsetCross = (c + 0.5f) / columns - 0.5f;
                Vector3 position = surface + along * offsetAlong + cross * offsetCross;
                CreateLight(anchor, position, outward, color, index++);
            }
        }
    }

    // Colore preso dall'emissione del materiale (o dal base color se non ce l'ha), normalizzato
    // al canale più alto: l'intensità HDR del materiale (serve al bagliore del LED stesso) non
    // deve sommarsi a 'lightIntensity', altrimenti regolarla in Inspector diventa imprevedibile.
    private static Color GetLedColor(Material mat)
    {
        Color color = Color.blue;
        if (mat != null && mat.HasProperty("_EmissionColor")) color = mat.GetColor("_EmissionColor");
        else if (mat != null && mat.HasProperty("_BaseColor")) color = mat.GetColor("_BaseColor");

        float max = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
        if (max <= 0f) return Color.blue;
        return new Color(color.r / max, color.g / max, color.b / max, 1f);
    }

    private void CreateLight(Transform anchor, Vector3 worldPosition, Vector3 direction, Color color, int index)
    {
        var go = new GameObject($"LedLight_{index}");
        go.transform.SetParent(anchor, false);
        go.transform.position = worldPosition;
        go.transform.rotation = Quaternion.LookRotation(direction);

        Light light = go.AddComponent<Light>();
        light.type = LightType.Spot;
        light.color = color;
        light.intensity = lightIntensity;
        light.range = lightRange;
        light.spotAngle = spotAngle;
        light.innerSpotAngle = spotAngle * 0.5f;
        light.shadows = castShadows ? LightShadows.Soft : LightShadows.None;
    }
}
