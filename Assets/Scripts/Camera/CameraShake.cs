using UnityEngine;

// Tremolio dello schermo, a "trauma": chi vuole far tremare chiama CameraShake.Add con un valore
// fra 0 e 1, i colpi si sommano (fino a 1) e il trauma cala da solo nel tempo. Lo spostamento
// vale trauma al quadrato, così i colpi piccoli si sentono appena e quelli grossi molto di più.
//
// Lo applica CameraFollow, sommandolo alla posizione che insegue il Player: non tocca la
// rotazione e non entra nello SmoothDamp, quindi non sporca l'inseguimento.
public static class CameraShake
{
    private const float MaxOffset = 0.5f;  // spostamento a trauma pieno (unità)
    private const float Decay = 2.5f;      // trauma perso al secondo
    private const float Frequency = 28f;   // quanto è nervoso il tremolio

    private static float trauma;
    private static float seed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetState()
    {
        trauma = 0f;
        seed = Random.value * 100f;
    }

    public static void Add(float amount)
    {
        trauma = Mathf.Clamp01(trauma + amount);
    }

    // Chiamato una volta per frame da CameraFollow. Tempo non scalato: il tremolio continua
    // anche durante l'hit-stop delle uccisioni (vedi PlayerBerserk).
    public static Vector3 Tick(Transform camera, float unscaledDeltaTime)
    {
        if (trauma <= 0f)
        {
            return Vector3.zero;
        }

        float shake = trauma * trauma * MaxOffset;
        float t = Time.unscaledTime * Frequency;
        float x = (Mathf.PerlinNoise(seed, t) - 0.5f) * 2f;
        float y = (Mathf.PerlinNoise(seed + 50f, t) - 0.5f) * 2f;

        trauma = Mathf.Max(0f, trauma - Decay * unscaledDeltaTime);
        return (camera.right * x + camera.up * y) * shake;
    }
}
