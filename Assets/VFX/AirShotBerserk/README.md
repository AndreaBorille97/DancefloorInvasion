# AirShot BERSERK — Unity Package

Versione berserk del colpo d'aria: linea di plasma bianco/rossa, scariche elettriche rosse e bianche,
onde d'urto rosse ondulate e tremolanti, scintille rosse, detriti bianchi, flash rosso e shake raddoppiato.
Indipendente dal pacchetto AirShotVFX (namespace e shader separati: possono convivere).

## Setup
1. Assets → Import Package → Custom Package → AirShotBerserk.unitypackage
2. Su un GameObject davanti al player: Add Component → AirShot → Air Shot BERSERK VFX
   (+ Air Shot BERSERK Demo Input per testare con SPAZIO / click)
3. Opzionali: Muzzle, Target, Shake Camera

## API
```csharp
var fx = GetComponent<AirShotBerserk.AirShotBerserkVFX>();
fx.Fire();               // verso Target o in avanti per 'range'
fx.Fire(hit.point);      // verso un punto
fx.Stop();
```

## Parametri chiave
- Discharges / Discharge Chaos / Discharge Linger — numero, caos e persistenza delle scariche
- Ring Waviness / Ring Flicker — ondulazione e tremolio delle onde rosse
- Power, Speed, Density, Trail Sparks, Line Thickness — come nella preview
- Red Color / Hot Color — palette

## URP / HDRP
Lo shader incluso è Built-in. Su URP/HDRP sostituisci con un Unlit additivo della tua pipeline
(lo script fa fallback automatico a Legacy Particles/Additive o Sprites/Default).
