using System.Collections;
using UnityEngine;

// La sequenza di sconfitta, la stessa per tutte e tre le cause (morte del Player, Console/DJ
// distrutta, SoundSystem distrutto): salta in aria con l'esplosione dei camion (TruckExplosion),
// si zittisce tutto (musica, layer dei bassi, effetti), parte il disco che salta e, attaccati
// alla sua fine, i grugniti; dopo gameOverDelay scatta GameOverState (pannello, senza pausa).
//
// La camera prima va su ciò che salta in aria e lo mette al centro (CameraFollow.Focus), e la
// sequenza parte quando è arrivata: fino a quel momento l'oggetto resta in scena, così si vede
// esplodere. Se salta un obiettivo il Player sparisce subito (HidePlayer).
//
// Esplosione e suoni si regolano sul Player (PlayerHealth, sezione "Morte"), che li registra
// qui in Awake: finché la partita è in corso il Player c'è, e i valori restano anche dopo che è
// stato distrutto. Statico come GameOverState: chi lo fa partire viene distrutto subito dopo.
public static class DefeatSequence
{
    public class Settings
    {
        public float explosionScale = 1f;
        public Shader explosionShader;
        public float explosionShake = 0.8f;
        public float gameOverDelay = 1.5f;
        public AudioClip recordScratchClip;
        public float recordScratchVolume = 1f;
        public AudioClip deathClip;
        public float deathVolume = 1f;
    }

    // Tetto al viaggio della camera, se per qualche motivo non arrivasse mai.
    private const float MaxPanSeconds = 2.5f;
    // Un attimo fermi sull'obiettivo prima che salti, per capire cosa sta succedendo.
    private const float HoldBeforeExplosion = 0.25f;

    public static Settings Current = new Settings();
    public static bool IsRunning { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        IsRunning = false;
        Current = new Settings();
    }

    // victim = l'oggetto che salta in aria: viene distrutto qui, al momento dell'esplosione
    // (il Player solo spento, vedi Remove).
    // Se una sequenza è già partita (es. il Player muore mentre la camera va sulla Console)
    // l'oggetto esplode e basta: suoni e game over sono già in arrivo.
    // alsoDestroy = qualcosa che sparisce insieme alla vittima ma non conta per il centro
    // dell'esplosione e dell'inquadratura (il DJ accanto alla Console).
    // alsoExplode = oggetti a parte che saltano in aria con la vittima, ognuno con la sua
    // esplosione (deco e tentacolo del SoundSystem).
    // focusShift = metri di cui la camera si ferma più avanti dell'oggetto, verso il centro della
    // pista: il SoundSystem sta sul bordo, col mare dietro.
    public static void Play(GameObject victim, GameObject alsoDestroy = null, GameObject[] alsoExplode = null, float focusShift = 0f)
    {
        // Partita persa per un obiettivo: il Player sparisce subito.
        if (IsObjective(victim))
        {
            HidePlayer();
        }

        if (IsRunning)
        {
            StartRoutIfObjective(victim);
            Explode(GroundCenter(victim));
            Remove(victim);
            if (alsoDestroy != null)
            {
                Object.Destroy(alsoDestroy);
            }
            ExplodeExtras(alsoExplode);
            return;
        }

        IsRunning = true;
        GameObject runner = new GameObject("DefeatSequence");
        runner.AddComponent<Runner>().StartCoroutine(Run(victim, alsoDestroy, alsoExplode, focusShift));
    }

    private static bool IsObjective(GameObject victim)
    {
        return victim != null && (victim.GetComponent<DJConsoleHealth>() != null || victim.GetComponent<SoundSystem>() != null);
    }

    // Spento e non distrutto: chi tiene un riferimento al Player (Raver, minimappa, camera) non
    // si trova un oggetto distrutto in mano, e la ricerca per tag non lo trova più, come se non ci fosse.
    private static void HidePlayer()
    {
        PlayerHealth player = Object.FindAnyObjectByType<PlayerHealth>();
        if (player != null)
        {
            player.gameObject.SetActive(false);
        }
    }

    // Il Player si spegne invece di essere distrutto, per lo stesso motivo di HidePlayer: sotto il
    // game over la partita continua, e chi lo segue non deve trovarsi un oggetto distrutto.
    private static void Remove(GameObject victim)
    {
        if (victim == null)
        {
            return;
        }

        if (victim.GetComponent<PlayerHealth>() != null)
        {
            victim.SetActive(false);
        }
        else
        {
            Object.Destroy(victim);
        }
    }

    private static IEnumerator Run(GameObject victim, GameObject alsoDestroy, GameObject[] alsoExplode, float focusShift)
    {
        Vector3 center = GroundCenter(victim);

        // La camera va sempre su ciò che salta in aria, e lo mette al centro (anche se era già
        // inquadrato, ma di lato).
        CameraFollow follow = CameraFollow.Instance;
        if (follow != null)
        {
            follow.Focus(TowardFloorCenter(center, focusShift));
            float waited = 0f;
            while (!follow.IsAtFocus && waited < MaxPanSeconds)
            {
                yield return null;
                waited += Time.unscaledDeltaTime;
            }
            yield return new WaitForSecondsRealtime(HoldBeforeExplosion);

            if (victim != null)
            {
                center = GroundCenter(victim);
            }
        }

        StartRoutIfObjective(victim);
        Explode(center);
        Remove(victim);
        if (alsoDestroy != null)
        {
            Object.Destroy(alsoDestroy);
        }
        ExplodeExtras(alsoExplode);
        PlayAudio();

        yield return new WaitForSecondsRealtime(Current.gameOverDelay);
        GameOverState.Trigger();
    }

    // Se salta in aria la Console o il SoundSystem la partita non si ferma: i Raver scappano e gli
    // Sbirro li inseguono (DefeatRout), mentre compare il game over.
    private static void StartRoutIfObjective(GameObject victim)
    {
        if (IsObjective(victim))
        {
            DefeatRout.Activate();
        }
    }

    private static void Explode(Vector3 ground)
    {
        TruckExplosion.Spawn(ground, Current.explosionScale, Current.explosionShader);
        CameraShake.Add(Current.explosionShake);
    }

    // Solo le esplosioni, senza altro tremolio: quello della vittima basta e avanza.
    private static void ExplodeExtras(GameObject[] extras)
    {
        if (extras == null)
        {
            return;
        }

        foreach (GameObject extra in extras)
        {
            if (extra == null)
            {
                continue;
            }

            TruckExplosion.Spawn(GroundCenter(extra), Current.explosionScale, Current.explosionShader);
            Object.Destroy(extra);
        }
    }

    // Il punto spostato di shift metri verso il centro della pista (FloorBounds), in piano.
    private static Vector3 TowardFloorCenter(Vector3 point, float shift)
    {
        if (shift == 0f || FloorBounds.Instance == null)
        {
            return point;
        }

        Vector3 toCenter = FloorBounds.Instance.Bounds.center - point;
        toCenter.y = 0f;
        if (toCenter.sqrMagnitude < 0.0001f)
        {
            return point;
        }
        return point + toCenter.normalized * shift;
    }

    // Centro dell'oggetto (il SoundSystem è largo quattro casse: il pivot può stare su un lato),
    // portato a terra come l'esplosione del Player. Prima i collider solidi, cioè il corpo vero
    // (le casse muro1..muro4): fra le mesh ci sono anche i coni dei faretti (FarettoBeam), lunghi
    // metri, che spostavano il centro verso il mare. Le mesh solo se non c'è nessun collider.
    private static Vector3 GroundCenter(GameObject victim)
    {
        Vector3 center = victim.transform.position;
        bool any = false;
        Bounds bounds = default;
        foreach (Collider collider in victim.GetComponentsInChildren<Collider>())
        {
            if (collider.isTrigger || !collider.enabled)
            {
                continue;
            }

            if (any)
            {
                bounds.Encapsulate(collider.bounds);
            }
            else
            {
                bounds = collider.bounds;
                any = true;
            }
        }
        if (any)
        {
            center = bounds.center;
            if (FloorBounds.Instance != null)
            {
                center.y = FloorBounds.Instance.Bounds.max.y;
            }
            return center;
        }

        foreach (Renderer renderer in victim.GetComponentsInChildren<Renderer>())
        {
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
            {
                continue;
            }

            if (any)
            {
                bounds.Encapsulate(renderer.bounds);
            }
            else
            {
                bounds = renderer.bounds;
                any = true;
            }
        }
        if (any)
        {
            center = bounds.center;
        }

        if (FloorBounds.Instance != null)
        {
            center.y = FloorBounds.Instance.Bounds.max.y;
        }
        return center;
    }

    // Si zittisce tutto, poi il disco che salta e, attaccati alla sua fine, i grugniti. Su un
    // oggetto a parte: chi è saltato in aria è già stato distrutto. Gli AudioSource non
    // risentono della pausa del game over (timeScale 0).
    private static void PlayAudio()
    {
        foreach (AudioSource playing in Object.FindObjectsByType<AudioSource>())
        {
            playing.Stop();
        }

        GameObject audioObject = new GameObject("DefeatAudio");
        // PlayScheduled sul clock dell'audio: i grugniti partono esattamente a fine disco, senza
        // il buco o la sovrapposizione che darebbe un'attesa a frame.
        double start = AudioSettings.dspTime + 0.05;
        float total = 0f;

        AudioClip scratch = Current.recordScratchClip;
        if (scratch != null)
        {
            AddLayers(audioObject, scratch, Current.recordScratchVolume, start);
            start += (double)scratch.samples / scratch.frequency;
            total += scratch.length;
        }

        if (Current.deathClip != null)
        {
            AddLayers(audioObject, Current.deathClip, Current.deathVolume, start);
            total += Current.deathClip.length;
        }

        audioObject.AddComponent<DestroyAfterRealtime>().seconds = total + 0.2f;
    }

    // Volume sopra 1 (il massimo di un AudioSource): lo stesso clip su più AudioSource
    // sovrapposti, che si sommano, come la voce di CyberGiorgia. Troppo alto può distorcere.
    private static void AddLayers(GameObject owner, AudioClip clip, float volume, double start)
    {
        int layers = Mathf.Max(1, Mathf.CeilToInt(volume));
        for (int i = 0; i < layers; i++)
        {
            AudioSource source = owner.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = volume / layers;
            source.spatialBlend = 0f;
            source.playOnAwake = false;
            source.PlayScheduled(start);
        }
    }

    private class Runner : MonoBehaviour
    {
    }

    // Tempo reale: con timeScale 0 un Destroy ritardato non scatterebbe mai.
    private class DestroyAfterRealtime : MonoBehaviour
    {
        public float seconds;

        private IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(seconds);
            Destroy(gameObject);
        }
    }
}
