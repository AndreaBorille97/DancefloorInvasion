# Poi di fuoco — pacchetto Unity

Solo fiamme e animazione. Velocità parametrizzabile.

## Installazione

Copia la cartella in `Assets/`:

```
Assets/PoiFire/
├─ PoiFireDance.cs          ← il movimento (runtime)
├─ PoiFire_Flame.png        ← la texture della fiamma
└─ Editor/
   └─ PoiFireBuilder.cs     ← crea l'effetto già configurato
```

`PoiFireBuilder.cs` **deve** stare in una cartella chiamata `Editor`, altrimenti la build fallisce.

## Uso in 10 secondi

Menu **GameObject ▸ Effects ▸ Poi di fuoco (danza)**.

Crea `PoiFireDance` con le due teste e i sei ParticleSystem (fiamma, scia, scintille per lato), più il materiale additivo in `Assets/PoiFire/`.

Per agganciarlo a un personaggio: trascina il root come figlio del pelvis o del root del rig, con Y a terra e +Z davanti. Le teste sono guidate in world space dallo script, non dalle ossa delle mani — è voluto: così la figura resta pulita qualunque cosa faccia l'animazione del corpo.

## Velocità

Sul componente `PoiFireDance`:

| Campo | Cosa fa |
|---|---|
| `spinSpeed` | Giri al secondo. Default 1.15. **Un giro = un passaggio di piano**, quindi governa anche il ritmo della danza: il ciclo completo dura 3 giri. |
| `speedMultiplier` | Moltiplicatore runtime per slow-motion, power-up, hit-stop. Velocità effettiva = `spinSpeed * speedMultiplier`. |

Da codice:

```csharp
var poi = GetComponentInChildren<PoiFireDance>();
poi.spinSpeed = 2.0f;              // più veloce
poi.speedMultiplier = 0.25f;       // slow-motion
float giri = poi.Revolutions;      // per sincronizzare audio o VFX
```

Range utile 0.2 – 3 rev/s. Sotto 0.6 la scia si spezza: alza `Rate over Time` su `FX_Flame`. Sopra 2.2 diventa un anello pieno, e conviene abbassare `Rate over Distance` su `FX_Trail`.

## Cos'è il movimento

Non sono tre animazioni concatenate. È **un solo** movimento rotatorio a cui ruota il piano sotto, a velocità costante e senza mai una tenuta. La normale al piano scivola lungo una geodetica fra tre orientamenti:

1. **Muro frontale** — normale +Z, disco 1.25 m davanti al busto
2. **Ruota sagittale** — normale ±X, piani a 1.15 m dai fianchi
3. **Elica sopra la testa** — normale +Y, piano a 2.62 m

La testa completa un giro esattamente *durante* ogni passaggio, quindi non esiste un istante "fermo su un piano" da cui ripartire. Il frame del cerchio viene trasportato parallelamente, le mani seguono una spline chiusa (Catmull-Rom periodica): nessuno scatto, nessun gomito.

Le due teste girano **sempre in verso opposto** (contro-rotazione a specchio, non split-time). Distanza minima fiamma-corpo misurata sull'intero ciclo: 1.09 m.

## Geometria

| Campo | Default | Note |
|---|---|---|
| `chainRadius` | 0.95 | Distanza pivot → testa, in metri. |
| `rigScale` | 1.0 | Scala globale. 1 = personaggio alto ~1.8 m. |

Se accorci la catena, riavvicina i pivot in proporzione: le posizioni dei tre piani sono costanti in `PlanePivot()`, in cima allo script.

## Texture

`PoiFire_Flame.png` è la stessa texture dell'anteprima: disco a bordo duro, nessun soft glow (il bagliore lo fa il Bloom). Il builder la trova da solo e imposta Alpha Is Transparency + Clamp.

Se vuoi cambiarla, assegna la tua al materiale `Assets/PoiFire/FX_Flame_Cel.mat` — quadrata, bordi a fasce nette, alpha duro. Con un flipbook 4×4 attiva `Texture Sheet Animation` su `FX_Flame` con Frame over Time lineare 0 → 1.

## Post-process

Bloom Intensity ≈ 1.2, Threshold 0.9. Oltre, le fasce si fondono e il look cel sparisce.

## Budget

~190 particelle vive per mano, 380 totali. Su mobile: dimezza `Rate over Time` su `FX_Flame` ed elimina `FX_Sparks`.

## Note

- I ParticleSystem sono in **Simulation Space = World**. È indispensabile: in Local la fiamma ruoterebbe rigida col pivot, senza scia.
- `FX_Trail` usa `Rate over Distance`, non `Rate over Time`: la scia resta uniforme a ogni velocità, anche mentre cambi `spinSpeed` a runtime.
- Sorting Fudge: fiamma −5, scia +5. Così la scia sta dietro.
- Seleziona il root in scena per vedere i gizmo dei due cerchi guida.
- Testato su Unity 2021.3+ con URP. Senza URP il builder ricade su `Legacy Shaders/Particles/Additive`.
