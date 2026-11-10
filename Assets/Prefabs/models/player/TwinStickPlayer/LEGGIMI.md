# Modello del Player (PLAYER.fbx)

## Montaggio
Apri la scena (GameplayV2), poi menu **Tools > Player > Monta il modello animato** e salva la scena.
Il menu (Assets/Editor/PlayerModelSetup.cs) è ripetibile: toglie il modello vecchio, mette PLAYER.fbx
come figlio "PlayerModel" del Player, lo scala e lo appoggia sulla sabbia, aggiunge
`TwinStickLocomotion` e toglie `PlayerModelAnimation` (serviva al vecchio player.fbx).

## Chi fa cosa
- **PlayerMovement** (invariato): stick sinistro muove, stick destro mira e gira il Player.
- **Runtime/TwinStickLocomotion.cs**: anima il modello con una sola corsa in avanti + Idle:
  - le gambe si girano dove il Player si sposta davvero;
  - il busto ruota verso la mira (fino a 100°): le casse sono attaccate alla testa, quindi
    guardano sempre dove parte l'onda;
  - correndo al contrario della mira (oltre 105°) le gambe si girano e la corsa va all'indietro;
  - da fermo suona l'Idle e il personaggio si gira verso la mira.
- **PlayerShooting**: i due anelli di ogni onda partono da `Muzzle_L` e `Muzzle_R` (le casse),
  alla loro altezza e alla loro distanza (vedi `SoundWaveProjectile.LaunchFromSpeakers`).

## Parametri utili (Inspector, TwinStickLocomotion sul Player)
`Run Speed Per Hip Height` (5 = i piedi non pattinano; più basso = gambe più veloci), `Backpedal Angle`,
`Legs Lean Toward Aim`, `Max Torso Twist`.
