using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Premendo L1 (spalla sinistra del gamepad) il Player scatena l'Hoffman: un frattale psichedelico
// (ApeironVFX, cartella particles/hoffman) che si apre a terra con il Player al centro. Ogni Sbirro
// (chiunque abbia EnemyHealth) che si trova dentro l'area del frattale mentre è aperto muore
// all'istante esplodendo in colori psichedelici (EnemyHealth.HoffmanExplode, PsychedelicExplosion).
// I boss restano immuni (vedi Kill).
// Per tutta la durata della mossa il Player resta piantato sul posto (PlayerMovement.Root): può
// ancora mirare e sparare, ma non camminare né scattare.
//
// Prima qui c'era il buco nero lanciato davanti al Player (PlayerBlackHoleThrow): il file è stato
// rinominato tenendo lo stesso GUID.
//
// I lanci si guadagnano raccogliendo le ammo Hoffman (HoffmanAmmoPickup): ogni raccolta mette un
// Hoffman in tasca, ogni L1 ne spende uno. A tasca vuota, o con un Hoffman già in corso, L1 non fa niente.
//
// Si aggiunge da solo al Player all'avvio (vedi AttachToPlayer): non serve metterlo a mano in scena.
// L'aspetto del frattale (colori, tentacoli, gas...) si regola sul materiale
// Assets/Resources/ApeironVFX.mat.
[RequireComponent(typeof(PlayerMovement))]
public class PlayerHoffman : MonoBehaviour
{
    [Header("Lancio")]
    [Tooltip("Hoffman in tasca a inizio partita. Gli altri si guadagnano raccogliendo le ammo Hoffman.")]
    [SerializeField] private int startingCharges = 0;

    [Header("Area")]
    [Tooltip("Raggio dell'area del frattale, in metri: gli Sbirro entro questa distanza dal Player muoiono.")]
    [SerializeField] private float radius = 10.5f;
    [Tooltip("Durata totale della mossa in secondi (apertura, frattale aperto, chiusura e lampo finale). Il Player resta fermo per tutto questo tempo.")]
    [SerializeField] private float duration = 3f;
    [Tooltip("Off = il frattale è identico a ogni lancio. On = ogni lancio è diverso.")]
    [SerializeField] private bool vary = false;

    [Header("Tremolio")]
    [Tooltip("Trauma dato a CameraShake quando il frattale si apre (0-1).")]
    [Range(0f, 1f)] [SerializeField] private float shakeOnCast = 0.5f;

    private const string MaterialPath = "ApeironVFX"; // Assets/Resources/ApeironVFX.mat

    private int charges;
    private FuelGaugeHUD hud; // rettangolo degli Hoffman sotto la barra vita
    private PlayerMovement movement;
    private ApeironVFX fx;
    // Uno Sbirro con più collider verrebbe segnalato da ApeironVFX una volta per collider: senza
    // questo elenco morirebbe due volte (doppia polvere, doppio evento di uccisione).
    private readonly HashSet<EnemyHealth> killed = new HashSet<EnemyHealth>();

    public int Charges => charges;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToPlayer()
    {
        PlayerMovement player = FindAnyObjectByType<PlayerMovement>();
        if (player != null && player.GetComponent<PlayerHoffman>() == null)
        {
            player.gameObject.AddComponent<PlayerHoffman>();
        }
    }

    void Awake()
    {
        charges = startingCharges;
        movement = GetComponent<PlayerMovement>();
        // Il componente si aggiunge da solo a runtime, quindi niente riferimento serializzato:
        // l'HUD è l'unico FuelGaugeHUD in scena (stesso fallback di PlayerDash).
        hud = FindAnyObjectByType<FuelGaugeHUD>();
        fx = CreateEffect();
        UpdateUI();
    }

    void Update()
    {
        Gamepad pad = Gamepad.current;
        if (pad != null && pad.leftShoulder.wasPressedThisFrame)
        {
            TryCast();
        }
    }

    public bool IsPlaying => fx != null && fx.IsPlaying;

    // Spende un Hoffman e lo scatena, se ce n'è uno in tasca e non ce n'è già uno aperto. Lo
    // chiamano L1 e, in modalità facile, EasyAutoPowers.
    public bool TryCast()
    {
        if (charges <= 0 || fx == null || fx.IsPlaying)
        {
            return false;
        }

        charges--;
        UpdateUI();
        Cast();
        return true;
    }

    // Chiamato dalle ammo Hoffman raccolte.
    public void AddCharges(int amount)
    {
        charges += amount;
        UpdateUI();
    }

    private void UpdateUI()
    {
        if (hud != null)
        {
            hud.SetBlackHoles(charges);
        }
    }

    // Un solo effetto, creato all'avvio e riusato a ogni lancio: ApeironVFX si riaccende da solo
    // con Cast e si spegne alla fine della sua animazione.
    private ApeironVFX CreateEffect()
    {
        Material material = Resources.Load<Material>(MaterialPath);
        if (material == null)
        {
            Debug.LogError($"PlayerHoffman: materiale Resources/{MaterialPath} non trovato, L1 non farà niente.");
            return null;
        }

        GameObject go = new GameObject("Hoffman");
        ApeironVFX effect = go.AddComponent<ApeironVFX>();
        // Awake di ApeironVFX è già girato con material vuoto: il materiale va messo anche sul renderer.
        effect.material = material;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        // Le uccisioni le facciamo noi passando da EnemyHealth (polvere, occhi, evento di uccisione):
        // il Destroy secco del pacchetto toglierebbe anche Player, Raver e oggetti di scena nell'area.
        effect.destroyEnemies = false;
        // Il suo tremolio sposta la camera direttamente e litigherebbe con CameraFollow: si usa CameraShake.
        effect.cameraToShake = null;
        effect.onEnemyKilled = new UnityEngine.Events.UnityEvent<GameObject>();
        effect.onEnemyKilled.AddListener(Kill);
        return effect;
    }

    private void Cast()
    {
        fx.radius = radius;
        fx.duration = duration;
        fx.vary = vary;
        killed.Clear();

        Vector3 feet = transform.position;
        fx.Cast(feet);
        movement.Root(duration);
        CameraShake.Add(shakeOnCast);
        PlayerMolecules.Show(PlayerMolecules.Molecola.Lsd);
    }

    private void Kill(GameObject hit)
    {
        // I boss (IdroSbirro, CyberGiorgia) non li tocca: si abbattono solo a colpi.
        EnemyHealth enemy = hit.GetComponentInParent<EnemyHealth>();
        if (enemy == null || enemy.IsBoss || !killed.Add(enemy))
        {
            return;
        }

        enemy.HoffmanExplode();
    }
}
