using UnityEngine;

// Un occhio strappato, disegnato pixel per pixel una sola volta e condiviso da chi ne ha
// bisogno (EnemyHealth, per i due occhi che schizzano via alla morte dello Sbirro): stesso
// approccio di RoundParticleTexture, nessuna immagine da importare e da collegare a mano.
//
// È un disegno di cerchi concentrici, cioè pura matematica: pupilla nera, iride, bianco
// dell'occhio, e sul bordo esterno il rosso di dove è stato strappato — è quel bordo a farlo
// leggere come un pezzo di nemico invece che come una pallina bianca. Sopra tutto, il puntino
// di luce in alto a sinistra: senza quello un occhio sembra finto.
public static class EyeTexture
{
    private static Texture2D texture;

    public static Texture2D Get()
    {
        if (texture != null)
        {
            return texture;
        }

        const int size = 64;
        const float radius = size * 0.5f;

        // Raggi delle varie zone, in frazione del raggio totale.
        const float pupilEnd = 0.26f;
        const float irisEnd = 0.46f;
        const float bloodStart = 0.72f;

        Color pupil = new Color(0.04f, 0.04f, 0.06f);
        Color iris = new Color(0.16f, 0.42f, 0.52f);
        Color white = new Color(0.97f, 0.96f, 0.93f);
        Color blood = new Color(0.55f, 0.09f, 0.09f);

        // Il puntino di luce: posizione e raggio in frazione del raggio totale, in alto a
        // sinistra rispetto al centro (nelle Texture2D la y cresce verso l'alto).
        Vector2 highlightCenter = new Vector2(-0.20f, 0.22f);
        const float highlightRadius = 0.14f;

        texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Posizione del pixel rispetto al centro, in frazione del raggio: (0,0) al
                // centro, lunghezza 1 sul bordo del disco.
                Vector2 offset = new Vector2(x + 0.5f - radius, y + 0.5f - radius) / radius;
                float distance = offset.magnitude;

                // Fuori dal disco non c'è niente; sul bordo un paio di pixel di sfumatura,
                // altrimenti l'occhio risulta seghettato.
                float alpha = Mathf.Clamp01((1f - distance) * radius / 2f);

                Color color;
                if (distance < pupilEnd)
                {
                    color = pupil;
                }
                else if (distance < irisEnd)
                {
                    // L'iride si scurisce verso il bordo esterno: è quello che le dà profondità
                    // invece di farla sembrare un bollo piatto.
                    float towardsEdge = Mathf.InverseLerp(pupilEnd, irisEnd, distance);
                    color = Color.Lerp(iris, iris * 0.45f, towardsEdge);
                }
                else
                {
                    // Bianco dell'occhio che vira al rosso solo sull'ultimo bordo.
                    float towardsBlood = Mathf.InverseLerp(bloodStart, 1f, distance);
                    color = Color.Lerp(white, blood, towardsBlood * towardsBlood);
                }

                // Il riflesso di luce sta sopra a tutto, pupilla compresa.
                float highlightDistance = (offset - highlightCenter).magnitude;
                if (highlightDistance < highlightRadius)
                {
                    float softness = Mathf.InverseLerp(highlightRadius, highlightRadius * 0.55f, highlightDistance);
                    color = Color.Lerp(color, Color.white, softness);
                }

                color.a = alpha;
                texture.SetPixel(x, y, color);
            }
        }

        texture.Apply();
        return texture;
    }
}
