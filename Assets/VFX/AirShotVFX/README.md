# AirShot VFX — Unity Package

Effetto "colpo d'aria supersonico" completamente procedurale: nessun asset esterno.
Valori di default = configurazione approvata in preview.

## Struttura file
```
AirShotVFX/
  AirShotVFX.cs          ← il componente
  AirShotAdditive.shader ← shader additivo per bolt, ring e particelle
```

## Setup in 3 passi

1. **Importa** i due file in qualsiasi cartella del tuo progetto Unity.
2. **Aggiunge il componente** sul GameObject del player (o su un figlio "Muzzle"):
   - Component → AirShot → Air Shot VFX
3. **Assegna i riferimenti** nell'Inspector:
   - `Muzzle` → il transform da cui parte il colpo (default: questo transform)
   - `Target` → il transform del bersaglio (opzionale, altrimenti usa `range`)
   - `ShakeCamera` → Camera da scuotere (default: Camera.main)

## Chiamata da codice

```csharp
// Spara verso il transform Target assegnato (o in avanti per 'range' unità)
GetComponent<AirShotVFX>().Fire();

// Spara verso un punto preciso nel mondo (es. raycast hit)
GetComponent<AirShotVFX>().Fire(hitInfo.point);

// Interrompi prima della fine
GetComponent<AirShotVFX>().Stop();

// Controlla se sta sparando
bool playing = GetComponent<AirShotVFX>().IsPlaying;
```

## Parametri Inspector (mappano 1:1 con la preview)

| Inspector | Preview | Default |
|---|---|---|
| Power | potenza | 1.1 |
| Speed | velocità | 6.0 |
| Trail Sparks | scintille di scia | 0.0 |
| Bolt Thickness | spessore fulmine | 1.1 |
| Zigzag | zigzag / dispersione | 0.55 |
| Cel Fps | cel rate | 12 |
| Rings | onda d'urto (partenza) | 2 |
| Density | densità scintille | 54 |
| Camera Shake | camera shake | 0.5 |
| Hot Color | palette ARIA (bianco) | white |
| Glow Color | alone | #80D6FF |

## Note URP / HDRP

Il `AirShotAdditive.shader` è per Built-in RP. Per URP/HDRP:
- sostituisce il shader con uno `Unlit/Additive` del tuo RP, oppure
- assegna `Particles/Standard Unlit` con Blending Mode = Additive
  (il codice fa già fallback automatico a `Legacy Shaders/Particles/Additive`
  e poi `Sprites/Default`).

## Compatibilità

Unity 2021 LTS+ · Built-in RP, URP, HDRP (con shader swap) · Nessuna dipendenza esterna.
