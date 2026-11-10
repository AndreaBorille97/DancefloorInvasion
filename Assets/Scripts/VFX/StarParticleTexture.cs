using UnityEngine;

// Una stellina a quattro punte con un nucleo luminoso, disegnata pixel per pixel una sola volta
// e condivisa da tutti gli effetti che ne hanno bisogno (vedi CollectibleShine): stesso approccio
// di RoundParticleTexture, nessuna immagine da importare e collegare a mano.
public static class StarParticleTexture
{
    private static Texture2D texture;

    public static Texture2D Get()
    {
        if (texture != null)
        {
            return texture;
        }

        const int size = 64;
        const float half = size * 0.5f;
        texture = new Texture2D(size, size, TextureFormat.RGBA32, false);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Coordinate da -1 a 1 col centro dell'immagine nell'origine.
                float u = (x + 0.5f) / half - 1f;
                float v = (y + 0.5f) / half - 1f;

                // sqrt(|u|) + sqrt(|v|) = 1 è un'astroide: vale 1 sulle punte lungo gli assi e
                // cresce in fretta sulle diagonali, che è proprio il profilo concavo di una
                // stellina a quattro punte. Sotto 1 si è dentro la stella, sopra fuori.
                float star = Mathf.Sqrt(Mathf.Abs(u)) + Mathf.Sqrt(Mathf.Abs(v));
                float alpha = Mathf.Clamp01((1f - star) / 0.35f); // 0.35 = morbidezza del bordo

                // Nucleo: senza, il centro della stella è tenue quanto le punte e da lontano la
                // scintilla non si legge. Un dischetto pieno al centro le dà il lampo.
                float distanceFromCenter = Mathf.Sqrt(u * u + v * v);
                float core = Mathf.Clamp01((0.22f - distanceFromCenter) / 0.22f);

                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Max(alpha, core)));
            }
        }

        texture.Apply();
        return texture;
    }
}
