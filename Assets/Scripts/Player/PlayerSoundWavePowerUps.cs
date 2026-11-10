using UnityEngine;

// Da mettere sul Player, insieme a PlayerShooting. Tiene traccia dei bonus temporanei
// dati da AreaSoundWavePowerUpPickup (apertura del cono) e DeepSoundWavePowerUpPickup
// (raggio del cono). I due bonus hanno timer indipendenti, quindi possono essere attivi
// insieme. PlayerShooting legge AreaBonusConeAngle/DeepBonusMaxRadius e li applica ad
// ogni SoundWaveProjectile appena istanziato (vedi SoundWaveProjectile.ApplyPowerUps).
public class PlayerSoundWavePowerUps : MonoBehaviour
{
    private float areaBonusConeAngle;
    private float areaTimer;

    private float deepBonusMaxRadius;
    private float deepTimer;

    public float AreaBonusConeAngle => areaTimer > 0f ? areaBonusConeAngle : 0f;
    public float DeepBonusMaxRadius => deepTimer > 0f ? deepBonusMaxRadius : 0f;

    void Update()
    {
        if (areaTimer > 0f)
        {
            areaTimer -= Time.deltaTime;
        }

        if (deepTimer > 0f)
        {
            deepTimer -= Time.deltaTime;
        }
    }

    // Raccogliendo di nuovo lo stesso powerup mentre è già attivo, il timer si rinnova
    // (non si somma alla durata rimasta), stesso comportamento semplice degli altri pickup.
    public void ActivateAreaPowerUp(float bonusConeAngle, float duration)
    {
        areaBonusConeAngle = bonusConeAngle;
        areaTimer = duration;
    }

    public void ActivateDeepPowerUp(float bonusMaxRadius, float duration)
    {
        deepBonusMaxRadius = bonusMaxRadius;
        deepTimer = duration;
    }
}
