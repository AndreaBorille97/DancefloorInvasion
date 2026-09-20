using UnityEngine;

// Implementata da tutto ciò che può subire danno a quantità (EnemyHealth, RobosbirroHealth),
// così RaverAttack non deve conoscere il tipo esatto del bersaglio che ha colpito.
// sourcePosition è la posizione di chi infligge il danno nel momento del colpo: serve a
// EnemyHealth per far svanire lo Sbirro in una polvere di particelle che vola dalla parte
// opposta rispetto a dove ha preso il colpo (vedi EnemyHealth.Die).
public interface IDamageable
{
    void TakeDamage(float amount, Vector3 sourcePosition);
}
