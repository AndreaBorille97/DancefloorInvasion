using UnityEngine;

// Da mettere su un GameObject vuoto in scena (es. "GameManager").
// Impostazioni globali applicate all'avvio del gioco.
public class GameManager : MonoBehaviour
{
    [SerializeField] private int targetFrameRate = 60; // fps massimi: senza limite il gioco arriva a 800+, inutile e instabile

    void Awake()
    {
        // VSync ha la priorità su targetFrameRate: se resta attivo, Unity ignora il
        // limite impostato sotto e sincronizza sempre al refresh del monitor.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = targetFrameRate;
    }
}
