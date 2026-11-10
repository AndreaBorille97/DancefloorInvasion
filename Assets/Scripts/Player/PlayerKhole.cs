using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Potere khole: premendo L2 (grilletto sinistro del gamepad), per activeSeconds secondi il Player
// passa attraverso i nemici, non subisce danno (PlayerHealth.TakeHit) e si lascia dietro una scia
// olografica di sé stesso, con delle stelline blu che restano a brillare lungo la scia. Le copie
// non sparano: le onde partono solo dal Player.
//
// Funziona a cariche come l'Hoffman (PlayerHoffman): ogni ammo khole raccolta
// (KholeAmmoPickup) ne mette una in tasca, ogni L2 ne spende una. A tasca vuota, o mentre il
// potere è già attivo, L2 non fa niente.
//
// Passare attraverso = niente collisioni fra i collider del Player e quelli dei nemici
// (Physics.IgnoreCollision, coppia per coppia). I nemici nati durante il potere vengono presi
// strada facendo (vedi RefreshIgnoredEnemies). Siccome l'attacco corpo a corpo degli Sbirro parte
// dal contatto fisico (EnemyAttack.OnCollisionEnter/Stay), intanto quelli non colpiscono: i
// proiettili e l'attacco ad area del Robosbirro invece sì, perché non passano dalle collisioni.
//
// La scia: ogni trailInterval secondi si fotografano le mesh del Player (le skinned vengono
// "cotte" nella posa del momento) e si ridisegnano lì dove sono state prese, con lo shader
// Ologramma, sbiadendo in trailLifetime secondi. Niente GameObject: le copie si disegnano a mano
// ogni frame con Graphics.RenderMesh, così non c'è niente da distruggere o da far pulire alla
// scena.
//
// Si aggiunge da solo al Player all'avvio, come PlayerHoffman.
public class PlayerKhole : MonoBehaviour
{
    [Header("Potere")]
    [Tooltip("Cariche in tasca a inizio partita. Le altre si guadagnano raccogliendo le ammo khole.")]
    [SerializeField] private int startingCharges = 0;
    [Tooltip("Quanti secondi dura il potere.")]
    [SerializeField] private float activeSeconds = 10f;

    [Header("Scia olografica")]
    [Tooltip("Ogni quanti secondi lascia una copia di sé.")]
    [SerializeField] private float trailInterval = 0.07f;
    [Tooltip("Quanti secondi impiega una copia a sparire.")]
    [SerializeField] private float trailLifetime = 2f;
    [Tooltip("Quanto è più luminosa la scia rispetto al materiale KholeOlogramma (che usa anche la " +
             "sfera dell'ammo, che così resta com'è). Sopra 1 il Bloom la fa brillare di più.")]
    [SerializeField] private float trailBrightness = 0.35f;
    [Tooltip("Quanto si vede il centro delle copie, non solo i bordi (0 = solo bordi, 1 = piene). " +
             "Più alto = scia più piena e luminosa.")]
    [Range(0f, 1f)] [SerializeField] private float trailCore = 0.2f;
    [Tooltip("Negli ultimi secondi del potere la scia lampeggia, per avvisare che sta per finire.")]
    [SerializeField] private float warningSeconds = 2f;
    [Tooltip("Lampeggi al secondo durante l'avviso.")]
    [SerializeField] private float warningBlinkRate = 6f;
    [Tooltip("Quanto resta accesa la scia nella fase spenta del lampeggio (0 = sparisce del tutto).")]
    [Range(0f, 1f)] [SerializeField] private float warningDimLevel = 0.1f;
    [Tooltip("Ogni quanto ricontrolla i nemici in scena, per far passare attraverso anche quelli appena nati.")]
    [SerializeField] private float enemyRefreshInterval = 0.25f;

    [Header("Stelline della scia")]
    [Tooltip("Colore delle stelline che restano lungo la scia.")]
    [SerializeField] private Color starColor = new Color(0.35f, 0.65f, 1f, 1f);
    [Tooltip("Quante stelline al secondo lascia dietro di sé col potere attivo.")]
    [SerializeField] private float starRate = 18f;
    [Tooltip("Raggio attorno al corpo del Player in cui nascono le stelline.")]
    [SerializeField] private float starRadius = 0.6f;
    [Tooltip("A che altezza da terra stanno le stelline, più o meno a metà corpo.")]
    [SerializeField] private float starHeight = 1f;

    // Il materiale sta in Resources apposta: il componente nasce a runtime, quindi non ha
    // riferimenti serializzati, e un materiale caricato da lì entra di sicuro nella build insieme
    // al suo shader.
    private const string HologramMaterialPath = "KholeOlogramma";

    private class Ghost
    {
        public Mesh Mesh;
        public bool OwnsMesh; // true se è una mesh cotta da una skinned: va distrutta a fine vita
        public Matrix4x4 Matrix;
        public int SubMeshCount;
        public float Age;
        public MaterialPropertyBlock Fade = new MaterialPropertyBlock(); // uno a testa: ogni copia sbiadisce per conto suo
    }

    private readonly List<Ghost> ghosts = new List<Ghost>();
    private readonly HashSet<Collider> ignoredEnemyColliders = new HashSet<Collider>();
    private Collider[] playerColliders;
    private Renderer[] bodyRenderers;
    // Per ogni mesh skinnata: se la sua copia cotta va messa in scena con la scala del Renderer
    // (true) o senza (false). Misurato la prima volta, vedi BakedMatrix.
    private readonly Dictionary<SkinnedMeshRenderer, bool> bakedNeedsScale = new Dictionary<SkinnedMeshRenderer, bool>();
    private Material hologramMaterial;
    private static readonly int FadeId = Shader.PropertyToID("_Fade");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int CoreId = Shader.PropertyToID("_Core");

    private int charges;
    private float activeTimer;
    private float trailTimer;
    private float refreshTimer;
    private FuelGaugeHUD hud; // rettangolo dei khole accanto a quello dei buchi neri
    private ParticleSystem stars;

    public int Charges => charges;
    public bool IsActive => activeTimer > 0f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToPlayer()
    {
        PlayerMovement player = FindAnyObjectByType<PlayerMovement>();
        if (player != null && player.GetComponent<PlayerKhole>() == null)
        {
            player.gameObject.AddComponent<PlayerKhole>();
        }
    }

    void Awake()
    {
        charges = startingCharges;
        hologramMaterial = Resources.Load<Material>(HologramMaterialPath);
        if (hologramMaterial == null)
        {
            Debug.LogWarning($"[PlayerKhole] Materiale Resources/{HologramMaterialPath} non trovato: niente scia olografica.");
        }

        hud = FindAnyObjectByType<FuelGaugeHUD>();
        UpdateUI();
        CreateStars();
    }

    void Update()
    {
        Gamepad pad = Gamepad.current;
        if (pad != null && pad.leftTrigger.wasPressedThisFrame)
        {
            TryActivate();
        }

        if (IsActive)
        {
            activeTimer -= Time.deltaTime;

            refreshTimer -= Time.deltaTime;
            if (refreshTimer <= 0f)
            {
                refreshTimer = enemyRefreshInterval;
                RefreshIgnoredEnemies();
            }

            trailTimer -= Time.deltaTime;
            if (trailTimer <= 0f)
            {
                trailTimer = trailInterval;
                SpawnGhost();
            }

            if (activeTimer <= 0f)
            {
                Deactivate();
            }
        }

        DrawGhosts();
    }

    // Spende una carica e accende il khole, se ce n'è una in tasca e non è già acceso. Lo
    // chiamano L2 e, in modalità facile, EasyAutoPowers.
    public bool TryActivate()
    {
        if (charges <= 0 || IsActive)
        {
            return false;
        }

        charges--;
        UpdateUI();
        Activate();
        PlayerMolecules.Show(PlayerMolecules.Molecola.Ketamina);
        return true;
    }

    // Chiamato dalle ammo khole raccolte.
    public void AddCharges(int amount)
    {
        charges += amount;
        UpdateUI();
    }

    private void Activate()
    {
        activeTimer = activeSeconds;
        trailTimer = 0f;
        SetStarsEmitting(true);
        refreshTimer = 0f;

        // Presi adesso e non in Awake: il modello del Player potrebbe essere stato cambiato nel
        // frattempo. I trigger non contano, non bloccano nessuno.
        List<Collider> solid = new List<Collider>();
        foreach (Collider c in GetComponentsInChildren<Collider>())
        {
            if (!c.isTrigger)
            {
                solid.Add(c);
            }
        }
        playerColliders = solid.ToArray();

        List<Renderer> body = new List<Renderer>();
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            // Le onde sonore non sono il corpo del Player, nella scia non ci vanno. Oggi nascono
            // già staccate da lui (PlayerShooting), il filtro resta nel caso tornino sue figlie.
            // Neanche la patina dell'anfetamina (MetallicPatina): è un guscio sopra il corpo, non il corpo.
            if ((r is MeshRenderer || r is SkinnedMeshRenderer) && r.GetComponentInParent<SoundWaveProjectile>() == null
                && r.GetComponent<MetallicPatina.Shell>() == null)
            {
                body.Add(r);
            }
        }
        bodyRenderers = body.ToArray();
    }

    private void Deactivate()
    {
        activeTimer = 0f;
        SetStarsEmitting(false); // quelle già nate finiscono di brillare da sole, come le ultime copie
        foreach (Collider enemy in ignoredEnemyColliders)
        {
            if (enemy == null)
            {
                continue;
            }

            foreach (Collider own in playerColliders)
            {
                if (own != null)
                {
                    Physics.IgnoreCollision(own, enemy, false);
                }
            }
        }
        ignoredEnemyColliders.Clear();
    }

    // Nemici = chiunque abbia EnemyHealth (Sbirro e varianti).
    private void RefreshIgnoredEnemies()
    {
        foreach (EnemyHealth enemy in EnemyHealth.All)
        {
            IgnoreEnemy(enemy);
        }
    }

    private void IgnoreEnemy(Component enemy)
    {
        foreach (Collider c in enemy.GetComponentsInChildren<Collider>())
        {
            if (c.isTrigger || !ignoredEnemyColliders.Add(c))
            {
                continue;
            }

            foreach (Collider own in playerColliders)
            {
                if (own != null)
                {
                    Physics.IgnoreCollision(own, c, true);
                }
            }
        }
    }

    private void SpawnGhost()
    {
        if (hologramMaterial == null || bodyRenderers == null)
        {
            return;
        }

        foreach (Renderer r in bodyRenderers)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
            {
                continue;
            }

            Ghost ghost = new Ghost();
            if (r is SkinnedMeshRenderer skinned)
            {
                ghost.Mesh = new Mesh();
                skinned.BakeMesh(ghost.Mesh, true);
                ghost.OwnsMesh = true;
                ghost.Matrix = BakedMatrix(skinned, ghost.Mesh);
            }
            else
            {
                MeshFilter filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                {
                    continue;
                }
                ghost.Mesh = filter.sharedMesh;
                ghost.Matrix = r.transform.localToWorldMatrix;
            }

            ghost.SubMeshCount = ghost.Mesh.subMeshCount;
            ghosts.Add(ghost);
        }
    }

    // Dove mettere la copia cotta di una mesh skinnata. Se BakeMesh la restituisce già scalata
    // basta posizione e rotazione del Renderer, altrimenti serve anche la sua scala: quale delle
    // due non lo si dà per scontato (dipende dalla versione di Unity e da come è fatta la
    // gerarchia del modello, scalata dentro l'fbx e poi ancora in scena), lo si misura una volta
    // confrontando la copia con l'ingombro vero del modello. Prima la scia era fatta solo dalla
    // capsula, e con la scala sbagliata la copia del modello veniva minuscola o enorme.
    private Matrix4x4 BakedMatrix(SkinnedMeshRenderer skinned, Mesh baked)
    {
        Transform t = skinned.transform;
        Matrix4x4 unscaled = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
        if (!bakedNeedsScale.TryGetValue(skinned, out bool needsScale))
        {
            float real = skinned.bounds.size.magnitude;
            float asIs = WorldSize(unscaled, baked.bounds);
            float scaled = WorldSize(t.localToWorldMatrix, baked.bounds);
            needsScale = real > 0.0001f
                && Mathf.Abs(Mathf.Log(Mathf.Max(scaled, 0.0001f) / real)) < Mathf.Abs(Mathf.Log(Mathf.Max(asIs, 0.0001f) / real));
            bakedNeedsScale[skinned] = needsScale;
            Debug.Log($"[PlayerKhole] Scia di {skinned.name}: modello largo {real:0.##} m, copia " +
                      $"{asIs:0.##} m senza scala e {scaled:0.##} m con la scala -> {(needsScale ? "con" : "senza")} scala.");
        }
        return needsScale ? t.localToWorldMatrix : unscaled;
    }

    // Diagonale dell'ingombro in mondo di bounds portato da matrix.
    private static float WorldSize(Matrix4x4 matrix, Bounds bounds)
    {
        Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 offset = new Vector3(
                (corner & 1) == 0 ? -bounds.extents.x : bounds.extents.x,
                (corner & 2) == 0 ? -bounds.extents.y : bounds.extents.y,
                (corner & 4) == 0 ? -bounds.extents.z : bounds.extents.z);
            Vector3 world = matrix.MultiplyPoint3x4(bounds.center + offset);
            min = Vector3.Min(min, world);
            max = Vector3.Max(max, world);
        }
        return (max - min).magnitude;
    }

    private void DrawGhosts()
    {
        // Avviso di fine potere: tutte le copie si spengono e si riaccendono insieme. Finito il
        // potere il lampeggio si ferma e le ultime copie sbiadiscono normalmente.
        float blink = 1f;
        if (IsActive && activeTimer <= warningSeconds)
        {
            bool on = Mathf.Repeat(activeTimer * warningBlinkRate, 1f) >= 0.5f;
            blink = on ? 1f : warningDimLevel;
        }

        for (int i = ghosts.Count - 1; i >= 0; i--)
        {
            Ghost ghost = ghosts[i];
            ghost.Age += Time.deltaTime;
            if (ghost.Age >= trailLifetime)
            {
                if (ghost.OwnsMesh)
                {
                    Destroy(ghost.Mesh);
                }
                ghosts.RemoveAt(i);
                continue;
            }

            // Sbiadisce più lenta all'inizio e più svelta alla fine: la coda della scia resta
            // luminosa più a lungo invece di spegnersi subito.
            float life = 1f - ghost.Age / trailLifetime;
            ghost.Fade.SetFloat(FadeId, Mathf.Sqrt(life) * blink);
            ghost.Fade.SetColor(ColorId, hologramMaterial.GetColor(ColorId) * trailBrightness);
            ghost.Fade.SetFloat(CoreId, trailCore);
            RenderParams parameters = new RenderParams(hologramMaterial)
            {
                matProps = ghost.Fade,
                shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows = false,
            };

            for (int sub = 0; sub < ghost.SubMeshCount; sub++)
            {
                Graphics.RenderMesh(parameters, ghost.Mesh, sub, ghost.Matrix);
            }
        }
    }

    // Le stelline: un ParticleSystem figlio del Player ma simulato in coordinate mondo, così ogni
    // stellina resta dove è nata mentre il Player va avanti, e si trova lungo la scia accanto
    // alle copie olografiche. Vivono quanto una copia (trailLifetime) e sbiadiscono con lei.
    // Costruito a runtime come le scintille delle ammo (CollectibleShine), con la stessa stellina.
    private void CreateStars()
    {
        GameObject starObject = new GameObject("KholeStars");
        starObject.transform.SetParent(transform, false);
        starObject.transform.localPosition = Vector3.up * starHeight;

        stars = starObject.AddComponent<ParticleSystem>();
        stars.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = stars.main;
        main.playOnAwake = false;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(trailLifetime * 0.5f, trailLifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.22f);
        main.startColor = starColor;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        ParticleSystem.EmissionModule emission = stars.emission;
        emission.rateOverTime = starRate;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = stars.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = starRadius;

        // Nascono e muoiono sfumando, e nel mezzo pulsano: brillano invece di restare accese fisse.
        ParticleSystem.ColorOverLifetimeModule color = stars.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            alphaKeys = new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.15f),
                new GradientAlphaKey(0.4f, 0.4f),
                new GradientAlphaKey(1f, 0.65f),
                new GradientAlphaKey(0f, 1f),
            },
        });

        ParticleSystemRenderer starRenderer = starObject.GetComponent<ParticleSystemRenderer>();
        // Sprites/Default come gli altri effetti del progetto: c'è di sicuro anche in URP.
        starRenderer.material = new Material(MaterialLibrary.SpriteDefault);
        starRenderer.material.mainTexture = StarParticleTexture.Get();
        starRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        starRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        starRenderer.receiveShadows = false;

        stars.Play();
    }

    private void SetStarsEmitting(bool on)
    {
        if (stars != null)
        {
            ParticleSystem.EmissionModule emission = stars.emission;
            emission.enabled = on;
        }
    }

    void OnDestroy()
    {
        foreach (Ghost ghost in ghosts)
        {
            if (ghost.OwnsMesh && ghost.Mesh != null)
            {
                Destroy(ghost.Mesh);
            }
        }
        ghosts.Clear();
    }

    private void UpdateUI()
    {
        if (hud != null)
        {
            hud.SetKholes(charges);
        }
    }
}
