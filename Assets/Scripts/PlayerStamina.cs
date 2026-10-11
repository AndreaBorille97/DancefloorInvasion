using UnityEngine;
using UnityEngine.InputSystem;

// Da mettere sul Player. La stamina è l'unica risorsa del Player: la consumano lo sparo
// (poco per ogni onda, vedi PlayerShooting) e lo scatto (molto di più, qui su R1), e la
// ricaricano solo le scatole ammo raccolte (SpeedUpPowerUpAmmoPickup). Non si ricarica da sola:
// a stamina finita non si spara e non si scatta finché non si raccoglie una scatola.
//
// Un'azione parte solo se la stamina basta a pagarla tutta: niente colpi o scatti "a metà"
// con gli ultimi spiccioli di barra.
//
// La barra sta nell'HUD sotto la vita (FuelGaugeHUD.SetStamina).
//
// Prima questo era PlayerSpeedAmmo (munizioni Speed contate una per una, R2 = scatto): il file
// è stato rinominato tenendo lo stesso GUID, quindi il componente già messo sul Player in scena
// è diventato questo, e dashDistance ha tenuto il valore che aveva.
[RequireComponent(typeof(PlayerMovement))]
public class PlayerStamina : MonoBehaviour
{
    [Header("Stamina")]
    [Tooltip("Stamina a barra piena. Si parte pieni.")]
    [SerializeField] private float maxStamina = 100f;

    [Header("Scatto (R1)")]
    [SerializeField] private float dashDistance = 12f; // lunghezza dello scatto (unità)
    [Tooltip("Stamina consumata da uno scatto. Deve pesare molto più di un'onda (PlayerShooting.staminaPerShot): è la mossa d'emergenza.")]
    [SerializeField] private float dashCost = 15f;

    [Header("UI")]
    [SerializeField] private FuelGaugeHUD hud; // barra stamina sotto la vita; se vuoto non aggiorna nulla

    private PlayerMovement playerMovement;
    private float stamina;

    public float Stamina => stamina;
    public float MaxStamina => maxStamina;

    void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
        stamina = maxStamina;

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
        if (pad.rightShoulder.wasPressedThisFrame && TrySpend(dashCost))
        {
            playerMovement.Dash(dashDistance);
        }
    }

    // Toglie amount dalla stamina se ce n'è abbastanza e dice se l'ha fatto: chi chiama esegue
    // l'azione solo se torna true.
    public bool TrySpend(float amount)
    {
        if (stamina < amount)
        {
            return false;
        }

        stamina -= amount;
        UpdateUI();
        return true;
    }

    // Chiamato dalle scatole ammo raccolte.
    public void AddStamina(float amount)
    {
        stamina = Mathf.Min(maxStamina, stamina + amount);
        UpdateUI();
    }

    private void UpdateUI()
    {
        if (hud != null)
        {
            hud.SetStamina(stamina, maxStamina);
        }
    }
}
