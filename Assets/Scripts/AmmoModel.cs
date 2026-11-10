using UnityEngine;

// Aiuti comuni agli effetti delle ammo con un modello vero (AmmoHologram, AmmoPowderSwirl).
// Le ammo hanno un figlio "Modello" che contiene quello che si vede: gli effetti lavorano su
// quello, mentre CollectibleShine fa girare e ondeggiare la radice.
public static class AmmoModel
{
    // Il figlio "Modello", o il primo figlio se non c'è; la radice stessa se non ha figli.
    public static Transform FindHolder(Transform root)
    {
        Transform holder = root.Find("Modello");
        if (holder == null && root.childCount > 0)
        {
            holder = root.GetChild(0);
        }
        return holder != null ? holder : root;
    }

    // Sposta i figli di holder in modo che il centro di quello che si vede cada sul suo perno:
    // le bustine di models/BHUSTE hanno il perno sul fondo, e girando su sé stesse
    // ballerebbero. Rifarlo non fa danni: a modello già centrato lo spostamento è zero.
    public static void Center(Transform holder)
    {
        if (!TryGetBounds(holder, out Bounds bounds))
        {
            return;
        }

        Vector3 offset = holder.position - bounds.center;
        foreach (Transform child in holder)
        {
            child.position += offset;
        }
    }

    // Bounds in coordinate mondo di tutti i renderer sotto root.
    public static bool TryGetBounds(Transform root, out Bounds bounds)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        bounds = default;
        if (renderers.Length == 0)
        {
            return false;
        }

        bounds = renderers[0].bounds;
        foreach (Renderer r in renderers)
        {
            bounds.Encapsulate(r.bounds);
        }
        return true;
    }

    // Materiale additivo per luci e bagliori: lo stesso shader degli effetti dello scatto, con
    // gli stessi ripieghi se non c'è.
    public static Material CreateAdditiveMaterial(Color color)
    {
        Shader shader = Shader.Find("AirShot/Additive");
        if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Additive");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        return new Material(shader) { color = color };
    }
}
