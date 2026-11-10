using UnityEngine;
using UnityEngine.InputSystem;

// Da mettere sul Player. Lo scatto (R1) funziona a cariche, come l'Hoffman
// (PlayerHoffman) e il khole (PlayerKhole): ogni scatola SpeedUp raccolta
// (SpeedUpPowerUpAmmoPickup) ne mette una in tasca, ogni R1 ne spende una. A tasca vuota R1 non
// fa niente. Il numero di cariche sta nell'HUD accanto agli altri poteri (FuelGaugeHUD.SetDashes).
//
// Prima questo era PlayerStamina (barra di stamina consumata da sparo e scatto), e prima ancora
// PlayerSpeedAmmo: il file è stato rinominato tenendo lo stesso GUID, quindi il componente già
// messo sul Player in scena è diventato questo, e dashDistance ha tenuto il valore che aveva.
[RequireComponent(typeof(PlayerMovement))]
public class PlayerDash : MonoBehaviour
{
    [Header("Scatto (R1)")]
    [Tooltip("Cariche in tasca a inizio partita. Le altre si guadagnano raccogliendo le scatole SpeedUp.")]
    [SerializeField] private int startingCharges = 3;
    [SerializeField] private float dashDistance = 12f; // lunghezza dello scatto (unità)

    [Header("Scatto in berserk")]
    [Tooltip("In modalità berserk (PlayerBerserk) lo scatto è dritto come quello normale, ma più lungo: " +
             "lunghezza rispetto a dashDistance. Effetto rosso, tremolio e fascia di uccisione più larga " +
             "li fa PlayerMovement.")]
    [SerializeField] private float berserkDashLength = 1.25f;

    [Header("UI")]
    [SerializeField] private FuelGaugeHUD hud; // rettangolo degli scatti accanto agli altri poteri; se vuoto non aggiorna nulla

    private PlayerMovement playerMovement;
    private PlayerBerserk berserk;
    private int charges;

    public int Charges => charges;

    void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
        charges = startingCharges;

        // L'HUD è un oggetto di scena, non nel prefab del Player: fallback all'unico
        // FuelGaugeHUD in scena se il riferimento serializzato è andato perso.
        if (hud == null)
        {
            hud = FindAnyObjectByType<FuelGaugeHUD>();
        }

        UpdateUI();
    }

    void Update()
    {
        Gamepad pad = Gamepad.current;
        if (pad == null)
        {
            return;
        }

        // wasPressedThisFrame: uno scatto per pressione, non ripete finché il tasto resta premuto.
        // Da fermo (Hoffman in corso, vedi PlayerMovement.Root) lo scatto non partirebbe: la carica
        // non va sprecata.
        if (pad.rightShoulder.wasPressedThisFrame && charges > 0 && !playerMovement.IsRooted)
        {
            charges--;
            UpdateUI();
            PlayerMolecules.Show(PlayerMolecules.Molecola.Cocaina);
            // Cercato qui e non in Awake: PlayerBerserk lo aggiunge al Player il suo
            // AttachToPlayer, che gira dopo gli Awake della scena.
            if (berserk == null)
            {
                berserk = GetComponent<PlayerBerserk>();
            }

            if (berserk != null && berserk.IsActive)
            {
                playerMovement.Dash(dashDistance * berserkDashLength, true);
            }
            else
            {
                playerMovement.Dash(dashDistance);
            }
        }
    }

    // Chiamato dalle scatole SpeedUp raccolte.
    public void AddCharges(int amount)
    {
        charges += amount;
        UpdateUI();
    }

    private void UpdateUI()
    {
        if (hud != null)
        {
            hud.SetDashes(charges);
        }
    }
}
