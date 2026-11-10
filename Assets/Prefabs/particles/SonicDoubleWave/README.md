# Sonic Double Wave – Unity (2021.3+ / Unity 6, Built-in o URP)

Doppio getto di anelli sonici 3D, uno da ogni cassa sulla schiena del robot, a 175 BPM.

## Installazione
1. Copia la cartella `SonicDoubleWave` in `Assets/` (percorso: `Assets/SonicDoubleWave`), inclusi i file `.meta`.
2. Prefab pronti in `Prefabs/`:
   - `SonicRingShot` → anelli normali (azzurri, M_SonicRing)
   - `SonicRingShot_Berserk` → anelli berserk (rossi, strappati, più larghi e forti, M_SonicRing_Berserk)
3. Seleziona il robot → **Tools > Sonic Double Wave > Configura robot** (aggiunge emitter + berserk e collega i prefab), oppure trascina i prefab a mano su `DualSpeakerEmitter.ringPrefab` e `BerserkMode.berserkRingPrefab`.
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

## Modalità Berserk
- Tasto **B** (o `SetBerserk(true)` da script, es. quando si riempie una barra rabbia). `duration` > 0 = si spegne da sola.
- Effetto: onde rosse con strappi, stesso ritmo (175 BPM), anelli più larghi e lunghi, potenza ×1.6, dispersione casuale.
- `BerserkMode.OnShake` → collegalo al tuo camera shake. `OnBerserkChanged` → vignetta rossa, musica, UI.
- `tintRenderers`: renderer del robot (casse, occhi) che si illuminano di rosso (_EmissionColor).

## Scia
`SonicRingShot` ha `trailEchoes` (anelli fantasma che seguono il principale) e `trailStrength`. 0 = nessuna scia.
