using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Da mettere sul Player. Tiene il conto delle munizioni "Speed" raccolte
// (vedi SpeedUpPowerUpAmmoPickup) e, premendo il grilletto destro (R2/RT) del gamepad,
// ne consuma una per far fare al Player uno scatto veloce di media lunghezza nella
// direzione in cui si sta muovendo (vedi PlayerMovement.Dash).
[RequireComponent(typeof(PlayerMovement))]
public class PlayerSpeedAmmo : MonoBehaviour
{
    [Header("Munizione Speed")]
    [SerializeField] private float dashDistance = 6f; // N: lunghezza dello scatto (unità)

    [Header("UI")]
    [SerializeField] private Text ammoText; // testo UI che mostra il conteggio munizioni; se vuoto non mostra nulla
    [SerializeField] private FuelGaugeHUD hud; // contatore fulmine del gauge; se vuoto non aggiorna nulla

    private PlayerMovement playerMovement;
    private int ammo;

    void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
        UpdateUI();
    }

    void Update()
    {
        Gamepad pad = Gamepad.current;
        if (pad == null)
        {
            return;
        }

        // wasPressedThisFrame: scatta una sola volta alla pressione, non ripete finché il tasto resta premuto.
        if (pad.rightTrigger.wasPressedThisFrame)
        {
            UseAmmo();
        }
    }

    public void AddAmmo(int amount)
    {
        ammo += amount;
        UpdateUI();
    }

    private void UseAmmo()
    {
        if (ammo <= 0)
        {
            return;
        }

        ammo--;
        UpdateUI();

        playerMovement.Dash(dashDistance);
    }

    private void UpdateUI()
    {
        if (ammoText != null)
        {
            ammoText.text = $"Speed x{ammo}";
        }

        if (hud != null)
        {
            hud.SetDashCount(ammo);
        }
    }
}
