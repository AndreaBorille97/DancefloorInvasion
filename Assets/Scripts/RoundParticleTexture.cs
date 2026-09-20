using UnityEngine;

// Un piccolo disco pieno con bordo anti-aliasato, disegnato pixel per pixel una sola volta e
// condiviso da tutti gli effetti a particelle rotonde (EnemyHealth, SbirroCloud): evita di
// dover importare un'immagine solo per questo (nessun asset esterno da collegare a mano).
public static class RoundParticleTexture
{
    private static Texture2D texture;

    public static Texture2D Get()
    {
        if (texture != null)
        {
            return texture;
        }

        const int size = 32;
        const float radius = size * 0.5f;
        texture = new Texture2D(size, size, TextureFormat.RGBA32, false);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distanceFromCenter = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                float alpha = Mathf.Clamp01((radius - distanceFromCenter) / 2f); // 2px di sfumatura sul bordo
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        return texture;
    }
}
