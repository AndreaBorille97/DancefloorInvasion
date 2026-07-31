// Implementata da tutto ciò che uno Sbirro può attaccare corpo a corpo (Player, Raver),
// così EnemyAttack non deve conoscere il tipo esatto del bersaglio con cui è a contatto.
public interface IEnemyAttackTarget
{
    void TakeHit();
}
