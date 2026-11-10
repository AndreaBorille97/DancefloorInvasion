using UnityEngine;

// Implementata da tutto ciò che uno Sbirro può attaccare corpo a corpo (Player, Raver),
// così EnemyAttack non deve conoscere il tipo esatto del bersaglio con cui è a contatto.
public interface IEnemyAttackTarget
{
    void TakeHit();

    // false quando il bersaglio è già fuori gioco e non va più toccato: un Raver sconfitto
    // (RaverHealth.IsDown, che sta andando in chillout o ci è già arrivato), un Player morto,
    // una Console o un SoundSystem già distrutti.
    //
    // Serve perché questi bersagli restano fisicamente lì, con il collider attivo: il Raver a
    // terra ce l'ha apposta, così il cono del Player continua a rilevarlo e può rianimarlo. Chi
    // attacca (EnemyAttack, RobosbirroMeleeAttack, SbirroCloud, ParabolicProjectile) li
    // incontrerebbe quindi come qualunque altro bersaglio.
    //
    // Non basta che TakeHit() non faccia danno — e infatti già non ne fa: un attacco parte
    // comunque, con tanto di animazione, e soprattutto tiene occupato chi lo sferra per tutta
    // la carica più il cooldown. Un Raver sconfitto che attraversa la mischia per andare in
    // chillout faceva così da esca gratis, assorbendo gli attacchi che sarebbero andati al
    // Player, ai Raver vivi o alla Console.
    bool CanBeAttacked { get; }
}
