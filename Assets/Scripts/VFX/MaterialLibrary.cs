using UnityEngine;

// Punto unico per gli Shader cercati per nome a runtime: prima ogni effetto faceva il suo
// Shader.Find (38 punti diversi), quasi tutti per gli stessi tre shader. Qui la ricerca si fa
// una volta sola e resta cachata.
//
// ATTENZIONE build: Shader.Find trova solo gli shader inclusi nella build (referenziati da un
// materiale negli asset o elencati in Always Included Shaders) — è il problema già visto in
// build (vedi i materiali *Incluso in Resources). Centralizzare qui non lo risolve da solo,
// ma lascia un posto solo da controllare.
public static class MaterialLibrary
{
    private static Shader spriteDefault;
    private static Shader additive;
    private static Shader urpUnlit;

    // Lo shader più usato dagli effetti particellari del gioco.
    public static Shader SpriteDefault
    {
        get
        {
            if (spriteDefault == null)
            {
                spriteDefault = Shader.Find("Sprites/Default");
            }
            return spriteDefault;
        }
    }

    // Additivo per i lampi/scie: la stessa catena di ripieghi che usavano AirShotVFX e AmmoModel.
    public static Shader Additive
    {
        get
        {
            if (additive == null)
            {
                additive = Shader.Find("AirShot/Additive");
                if (additive == null) additive = Shader.Find("Legacy Shaders/Particles/Additive");
                if (additive == null) additive = Shader.Find("Particles/Standard Unlit");
                if (additive == null) additive = SpriteDefault;
            }
            return additive;
        }
    }

    public static Shader UrpUnlit
    {
        get
        {
            if (urpUnlit == null)
            {
                urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
            }
            return urpUnlit;
        }
    }

    // Con "Enter Play Mode" senza ricarica del dominio i campi statici sopravvivono tra una
    // partita e l'altra nell'Editor: gli Shader sono asset e restano validi, ma si azzera per
    // coerenza con gli altri statici del progetto.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        spriteDefault = null;
        additive = null;
        urpUnlit = null;
    }
}
