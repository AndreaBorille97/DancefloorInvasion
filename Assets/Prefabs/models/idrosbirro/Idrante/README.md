# Getto idrante — Unity

## Installazione
1. Copia `WaterJet.cs` (e opzionale `WaterJetDemo.cs`) in `Assets/Scripts/`.
2. Crea un GameObject vuoto "WaterJet" → Add Component → `WaterJet` (aggiunge il LineRenderer).
3. Assegna `nozzle` (transform dell'ugello sulla camionetta).
4. Da codice: `jet.Fire(nozzle, bersaglio);` — `jet.Stop();` per interrompere prima.

## Parametri (stessi del prototipo HTML)
- `arcHeight` — altezza arco come frazione della distanza (scala con chi spara).
  Nel prototipo 0.07 era in spazio schermo; in mondo con camera inclinata parti da ~0.12 e regola.
- `thickness` — spessore in unità mondo.
- `startTime / sustainTime / endTime` — inizio, getto, fine. `sustainTime = 0` → continuo fino a `Stop()`.
- `endPressureDrop` — quanto si abbassa l'arco durante la fine.

## Gameplay
- `OnHitStart` / `OnHitEnd` e `IsHitting` per applicare danno/spinta solo quando l'acqua arriva.

## Particelle (opzionali)
Crea 4 ParticleSystem figli e assegnali:
- `headSpray` — Looping, Rate 60, Lifetime 0.3–0.6, Speed 1–2, Size 0.05–0.15, Gravity 1, colore azzurro chiaro.
- `impactSplash` — Looping, Rate 40, Shape Cone 60° verso l'alto, Speed 2–4, Gravity 1.5.
- `finalSplash` — non Looping, Play On Awake off, Emission 0 (lo script chiama `Emit(26)`).
- `nozzleMist` — Looping, Rate 20, Speed 0.5, Size 0.1–0.3, alpha basso.

## URP / HDRP
Il materiale di default usa `Sprites/Default`. In URP assegna al LineRenderer un materiale
`Universal Render Pipeline/Particles/Unlit` (Surface: Transparent, Blend: Alpha) con una texture
che si ripete in orizzontale: lo script ne fa scorrere l'offset.
