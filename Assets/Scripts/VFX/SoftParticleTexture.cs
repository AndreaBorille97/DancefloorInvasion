using UnityEngine;

// Sfumatura morbida dal centro al bordo (nessun contorno netto), l'opposto di
// RoundParticleTexture (disco pieno con bordo secco, adatto a detriti/polvere solida): qui
// serve per effetti "soffici" come il fumo (vedi SbirroCloud), dove un bordo netto farebbe
// sembrare ogni particella una bolla di sapone invece che un puffo di fumo.
public static class SoftParticleTexture
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
                float t = Mathf.Clamp01(distanceFromCenter / radius);
                float alpha = Mathf.SmoothStep(1f, 0f, t); // pieno vicino al centro, sfuma solo verso il bordo (non subito, altrimenti troppo evanescente)
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        return texture;
    }
}
