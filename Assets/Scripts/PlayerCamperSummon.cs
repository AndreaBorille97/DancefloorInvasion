using UnityEngine;
using UnityEngine.InputSystem;

// Da mettere sul Player. In qualsiasi momento della partita, premendo il grilletto
// sinistro (L2/LT) del gamepad manda il Raver più vicino al Camper piazzato in scena
// (vedi CamperVehicle.TrySendNearestRaver), a patto che ci sia benzina e che nessun altro
// Raver sia già assegnato al mezzo.
public class PlayerCamperSummon : MonoBehaviour
{
    void Update()
    {
        Gamepad pad = Gamepad.current;
        if (pad == null)
        {
            return;
        }

        // wasPressedThisFrame: scatta una sola volta alla pressione, non ripete finché il tasto resta premuto.
        if (pad.leftTrigger.wasPressedThisFrame)
        {
            FindAnyObjectByType<CamperVehicle>()?.TrySendNearestRaver();
        }
    }
}
