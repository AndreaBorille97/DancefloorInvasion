using UnityEngine;

// Aggiunto a runtime al Raver assegnato a un Camper (vedi CamperVehicle.SendNearestRaver).
// OnDestroy scatta automaticamente quando il Raver muore, sia mentre si sta avvicinando al
// mezzo sia mentre lo sta pilotando (RaverHealth chiama Destroy(gameObject) a vita
// azzerata): avvisa il CamperVehicle di annullare/terminare subito il power-up, invece di
// dover modificare RaverHealth per farglielo sapere esplicitamente.
public class CamperPilotWatcher : MonoBehaviour
{
    private CamperVehicle vehicle;

    public void Init(CamperVehicle vehicle)
    {
        this.vehicle = vehicle;
    }

    void OnDestroy()
    {
        vehicle?.OnPilotDestroyed();
    }
}
