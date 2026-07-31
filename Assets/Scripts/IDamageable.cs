// Implementata da tutto ciò che può subire danno a quantità (EnemyHealth, RobosbirroHealth),
// così RaverAttack non deve conoscere il tipo esatto del bersaglio che ha colpito.
public interface IDamageable
{
    void TakeDamage(float amount);
}
