using UnityEngine;
using UnityEngine.InputSystem;

// Potere serotonina: premendo R2 (grilletto destro del gamepad), per activeSeconds secondi le
// onde del Player diventano onde serotonina (gli anelli di Prefabs/particles/Onde Serotonina,
// vedi SoundWaveProjectile.MakeSerotonin, chiamato da PlayerShooting). Fanno lo stesso danno
// delle onde normali, ma il colpo che ucciderebbe uno Sbirro lo converte: invece di morire
// passa dalla parte del Player e si mette a picchiare gli altri nemici (vedi SerotoninBrawl).
//
// Funziona a cariche come il khole (PlayerKhole): ogni ammo serotonina raccolta
// (SerotoninAmmoPickup) ne mette una in tasca, ogni R2 ne spende una. A tasca vuota, o mentre il
// potere è già attivo, R2 non fa niente. Il numero di cariche sta nell'HUD accanto agli altri
// poteri (FuelGaugeHUD.SetSerotonins, rettangolo arancione).
//
// Insieme alle onde, gli effetti del pacchetto Onde Serotonina (CozyBeatFX): ogni due onde un
// fuoco d'artificio parte da dietro il Player e scoppia in aria, e ogni tanto un cuoricino vola
// via insieme all'onda. PlayerShooting chiama OnShot ad ogni onda serotonina sparata.
//
// Si aggiunge da solo al Player all'avvio, come PlayerKhole.
public class PlayerSerotonin : MonoBehaviour
{
    [Tooltip("Cariche in tasca a inizio partita. Le altre si guadagnano raccogliendo le ammo serotonina.")]
    [SerializeField] private int startingCharges = 0;
    // Deve bastare a convertire uno Sbirro2 a vita piena: 175.5 hp / 12 di danno = 15 onde, che a
    // 172 bpm (un'onda ogni 0.35 s) partono in circa 5.2 s. 6 s lasciano un paio di onde di margine.
    [Tooltip("Quanti secondi durano le onde serotonina.")]
    [SerializeField] private float activeSeconds = 6f;

    // Il materiale sta in Resources per lo stesso motivo di PlayerKhole: il componente nasce a
    // runtime senza riferimenti serializzati, e così il materiale entra nella build col suo shader.
    private const string CozyMaterialPath = "SerotoninCozyParticle";

    private int charges;
    private float activeTimer;
    private FuelGaugeHUD hud;
    private CozyBeatFX cozy;

    public int Charges => charges;
    public bool IsActive => activeTimer > 0f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToPlayer()
    {
        PlayerMovement player = FindAnyObjectByType<PlayerMovement>();
        if (player != null && player.GetComponent<PlayerSerotonin>() == null)
        {
            player.gameObject.AddComponent<PlayerSerotonin>();
        }
    }

    void Awake()
    {
        charges = startingCharges;
        hud = FindAnyObjectByType<FuelGaugeHUD>();
        UpdateUI();
        CreateCozyFX();
    }

    // CozyBeatFX legge il materiale nel suo Awake, che parte appena aggiunto: lo si aggiunge a un
    // figlio ancora spento, gli si dà il materiale e solo dopo lo si accende. Il figlio sta sul
    // Player con la sua stessa rotazione, così "dietro" e "avanti" sono quelli del Player.
    private void CreateCozyFX()
    {
        GameObject fxObject = new GameObject("SerotoninFX");
        fxObject.SetActive(false);
        fxObject.transform.SetParent(transform, false);

        cozy = fxObject.AddComponent<CozyBeatFX>();
        cozy.particleMaterial = Resources.Load<Material>(CozyMaterialPath);

        // Fuochi d'artificio più sgargianti dei valori del pacchetto (pastello e tenui): colori
        // saturi sopra 1 (lo shader è additivo, con il Bloom brillano), più scintille, scoppio
        // più largo, scintille più grosse e lampo più grande.
        cozy.fireworkColors = new[]
        {
            new Color(2.2f, 0.15f, 0.5f),  // magenta
            new Color(2.2f, 0.7f, 0f),     // arancio
            new Color(2f, 1.8f, 0.1f),     // giallo
            new Color(0.2f, 1.9f, 0.4f),   // verde
            new Color(0.2f, 0.9f, 2.2f),   // azzurro
            new Color(1.3f, 0.2f, 2.2f),   // viola
        };
        cozy.sparksPerBurst = 110;
        cozy.innerSparks = 60;
        cozy.burstSpeed = 10f;
        cozy.sparkSize = 0.5f;
        cozy.flashSize = 13f;
        if (cozy.particleMaterial == null)
        {
            Debug.LogWarning($"[PlayerSerotonin] Materiale Resources/{CozyMaterialPath} non trovato: provo con lo shader VFX/CozyParticle.");
        }

        fxObject.SetActive(true);
    }

    // Chiamato da PlayerShooting ad ogni onda serotonina sparata. source = da dove parte l'onda,
    // rivolto nella direzione di tiro: i cuoricini volano via da lì.
    public void OnShot(Transform source)
    {
        if (cozy != null)
        {
            cozy.Beat(source);
        }
    }

    void Update()
    {
        Gamepad pad = Gamepad.current;
        if (pad != null && pad.rightTrigger.wasPressedThisFrame && charges > 0 && !IsActive)
        {
            charges--;
            UpdateUI();
            ActivateNow();
        }

        if (IsActive)
        {
            activeTimer -= Time.deltaTime;
        }
    }

    // Accende le onde serotonina senza spendere cariche: in modalità facile la ammo raccolta parte
    // subito (vedi SerotoninAmmoPickup). Se sono già accese, riparte il conto dei secondi.
    public void ActivateNow()
    {
        activeTimer = activeSeconds;
        PlayerMolecules.Show(PlayerMolecules.Molecola.Mdma);
    }

    // Chiamato dalle ammo serotonina raccolte.
    public void AddCharges(int amount)
    {
        charges += amount;
        UpdateUI();
    }

    private void UpdateUI()
    {
        if (hud != null)
        {
            hud.SetSerotonins(charges);
        }
    }
}
