using UnityEngine;

// Come si comporta il mezzo mentre è guidato. A guidarlo è RaverChase (vedi DriveVehicle), ma
// questi valori si regolano sul Camper, che è chi li passa al Raver quando sale a bordo: è il
// mezzo ad avere una velocità e un raggio di sterzata, non il Raver che ci sale sopra, e
// nell'Inspector devono stare dove uno va a cercarli.
[System.Serializable]
public class VehicleTuning
{
    [Tooltip("Velocità massima del mezzo, in unità al secondo: il Player a piedi va a 6, quindi sopra quel valore il Camper è davvero un mezzo.")]
    public float maxSpeed = 8f;
    [Tooltip("Quanto in fretta prende e perde velocità (unità al secondo quadrato): dà lo spunto alla partenza e la frenata in arrivo, invece di passare di colpo da fermo a velocità piena.")]
    public float acceleration = 7f;
    [Tooltip("Raggio minimo di sterzata, in unità: quanto stretto può girare a fondo corsa. È il parametro che dà la taglia al mezzo — 5 è una curva da furgone, 10 da camion. Non esiste una velocità di rotazione: i gradi girati vengono da quanto si è avanzato, come su un mezzo con le ruote.")]
    public float minTurnRadius = 5.5f;
    [Tooltip("Quanto in fretta gira il volante, in frazioni di sterzo al secondo: a 2.5 ci vogliono 0.4 secondi per andare da dritto a fondo corsa. È quello che rende morbido l'ingresso e l'uscita di curva.")]
    public float steerRate = 2.5f;
    [Tooltip("Errore di direzione (gradi) oltre il quale il volante è già a fondo corsa. Sotto, lo sterzo è proporzionale all'errore: correzioni piccole per errori piccoli.")]
    public float fullLockAngle = 35f;
    [Tooltip("Sotto questo errore di direzione (gradi) il mezzo non corregge affatto: serve a non farlo serpeggiare dietro a un bersaglio che si muove.")]
    public float steerDeadZone = 3f;
    [Tooltip("Quanto rallenta col volante tutto girato (1 = non rallenta mai e gira come una trottola, 0.4 = in curva stretta va al 40%). È quello che gli dà il peso da veicolo.")]
    public float minThrottle = 0.55f;
}
