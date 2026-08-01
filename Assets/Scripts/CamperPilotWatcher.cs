using UnityEngine;

// Aggiunto a runtime al Raver assegnato a un Camper (vedi CamperVehicle.TrySendNearestRaver).
// Avvisa il CamperVehicle di annullare/terminare subito il power-up appena il Raver assegnato
// non è più in grado di guidarlo: normalmente perché va a terra (RaverHealth.IsDown, mentre si
// avvicina al mezzo o mentre lo sta pilotando — il Raver non sparisce più alla morte, resta a
// terra rianimabile, quindi il vecchio OnDestroy da solo non basterebbe più), altrimenti (per
// sicurezza, se il GameObject viene comunque distrutto per qualche altro motivo) via OnDestroy.
// Si autodistrugge appena rileva lo stato a terra: OnDestroy scatta comunque e notifica una
// sola volta il CamperVehicle, senza lasciare il watcher agganciato a un Raver ormai fuori uso.
public class CamperPilotWatcher : MonoBehaviour
{
    private CamperVehicle vehicle;
    private RaverHealth raverHealth;

    public void Init(CamperVehicle vehicle)
    {
        this.vehicle = vehicle;
        raverHealth = GetComponent<RaverHealth>();
    }

    void Update()
    {
        if (raverHealth != null && raverHealth.IsDown)
        {
            Destroy(this); // OnDestroy si occupa di notificare il CamperVehicle
        }
    }

    void OnDestroy()
    {
        vehicle?.OnPilotDestroyed();
    }
}
