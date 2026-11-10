# Sonic Double Wave – Unity (2021.3+ / Unity 6, Built-in o URP)

Doppio getto di anelli sonici 3D, uno da ogni cassa sulla schiena del robot, a 175 BPM.

## Installazione
1. Copia la cartella `SonicDoubleWave` in `Assets/` (percorso: `Assets/SonicDoubleWave`), inclusi i file `.meta`.
2. Prefab pronto: `Prefabs/SonicRingShot` (anelli caldi e morbidi, materiale `M_SonicRing`).
3. Seleziona il robot → **Tools > Sonic Double Wave > Configura robot** (aggiunge l'emitter e collega il prefab), oppure trascinalo a mano su `DualSpeakerEmitter.ringPrefab`.
4. Sposta `Speaker_L` / `Speaker_R` sulle casse: asse **Z (blu)** = direzione di sparo.
5. Nemici: implementa `IDamageable` (o Rigidbody per la sola spinta).

## Parametri principali
- `DualSpeakerEmitter`: `bpm` (175), `ringsPerBeat` (2), `ringGap`, `power`, `autoFire`; opzionali `speakerMembranes` (pulsano a ritmo) e `beatAudio`.
- `SonicRingShot` (sul prefab): `startRadius` 0.25 → `endRadius` 3 (apertura del cono), `range`, `duration`, `wobble`, `damage`, `knockback`, `hitMask`.
- `M_SonicRing`: colori HDR, `_Intensity`, increspature.

## Consigli
- Bloom leggero nel post-processing.
- La distorsione dell'aria della demo è un effetto di post-processing: in URP aggiungi un *Full Screen Pass* con un materiale heat-distortion se la vuoi.
- Con il nuovo Input System imposta *Active Input Handling = Both* (serve solo se `autoFire` è spento).


## Scia
`SonicRingShot` ha `trailEchoes` (anelli fantasma che seguono il principale) e `trailStrength`. 0 = nessuna scia.

## Cuoricini e fuochi d'artificio
`CozyBeatFX` (aggiunto da *Configura robot*): a ogni battuta escono cuoricini dalle casse insieme alle onde; ogni `everyNBeats` battute parte un piccolo fuoco d'artificio da dietro il player.
Si configura tutto dall'Inspector: colori, quantità, velocità, dimensioni, `heartInBurstChance`. Le texture di cuore e scintilla vengono generate a runtime, il materiale è `M_CozyParticle`.
