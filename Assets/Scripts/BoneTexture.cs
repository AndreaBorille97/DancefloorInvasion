using UnityEngine;

// L'ossicino da cartone animato — quello da dare al cane, con i due bitorzoli per parte —
// disegnato pixel per pixel una sola volta e condiviso da chi ne ha bisogno (EnemyHealth, per i
// pezzi che schizzano via alla morte dello Sbirro). Stesso approccio di RoundParticleTexture e
// EyeTexture: nessuna immagine da importare e da collegare a mano.
//
// La forma è l'unione di un bastoncino e di quattro palline, una per ogni angolo. Per avere il
// bordo pulito non si ragiona "dentro o fuori" ma per distanza con segno: di ogni pixel si
// calcola quanto dista dalla sagoma (negativo dentro, positivo fuori) e si prende la distanza
// minore fra le cinque parti. È quel numero, e non un sì/no, a dare la sfumatura di un pixel
// sul bordo, che altrimenti verrebbe seghettato.
public static class BoneTexture
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

        // Misure in frazione del mezzo lato: il bastoncino e le quattro palline alle estremità.
        Vector2 shaftHalf = new Vector2(0.62f, 0.17f);
        const float knobRadius = 0.3f;
        const float knobX = 0.6f;
        const float knobY = 0.26f;

        Color bone = new Color(0.95f, 0.93f, 0.86f);
        Color shade = new Color(0.72f, 0.68f, 0.58f);
        Color blood = new Color(0.55f, 0.11f, 0.11f);

        texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f - radius, y + 0.5f - radius) / radius;

                float distance = BoxDistance(p, shaftHalf);
                for (int cornerX = -1; cornerX <= 1; cornerX += 2)
                {
                    for (int cornerY = -1; cornerY <= 1; cornerY += 2)
                    {
                        float knob = (p - new Vector2(knobX * cornerX, knobY * cornerY)).magnitude - knobRadius;
                        distance = Mathf.Min(distance, knob);
                    }
                }

                // Dentro la sagoma la distanza è negativa: un paio di pixel di sfumatura sul bordo.
                float alpha = Mathf.Clamp01(-distance * radius / 2f);

                // Più si va verso il bordo più l'osso si scurisce: è quello che gli dà volume
                // invece di farlo sembrare una sagoma piatta ritagliata.
                float towardsEdge = Mathf.Clamp01(1f + distance * 4f);
                Color color = Color.Lerp(bone, shade, towardsEdge * towardsEdge);

                // Una punta di rosso sulle due teste: è stato strappato via, non raccolto da terra.
                float towardsTips = Mathf.InverseLerp(0.45f, 0.95f, Mathf.Abs(p.x));
                color = Color.Lerp(color, blood, towardsTips * 0.45f);

                color.a = alpha;
                texture.SetPixel(x, y, color);
            }
        }

        texture.Apply();
        return texture;
    }

    // Distanza con segno di un punto da un rettangolo centrato nell'origine: negativa dentro,
    // positiva fuori. È la formula classica, e serve perché gli angoli vengano giusti (fuori da
    // un angolo la distanza è quella dal vertice, non dal lato).
    private static float BoxDistance(Vector2 point, Vector2 half)
    {
        Vector2 d = new Vector2(Mathf.Abs(point.x) - half.x, Mathf.Abs(point.y) - half.y);
        float outside = new Vector2(Mathf.Max(d.x, 0f), Mathf.Max(d.y, 0f)).magnitude;
        float inside = Mathf.Min(Mathf.Max(d.x, d.y), 0f);
        return outside + inside;
    }
}
