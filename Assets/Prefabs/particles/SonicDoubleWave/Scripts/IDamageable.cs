using UnityEngine;

/// Implementala sui tuoi nemici per ricevere danno dalle onde sonore.
public interface ISonicDamageable
{
    void TakeSonicDamage(float amount, Vector3 knockback, Vector3 hitPoint);
}
