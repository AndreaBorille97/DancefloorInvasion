# Buco nero power-up per Unity

Unity 2021.3+ (anche Unity 6), Built-in o URP.

## Installazione
1. Copia `BlackHoleSolo` dentro `Assets/` (sostituisci la versione precedente).
2. GameObject vuoto → Add Component → **BlackHole**. Salvalo come **prefab**.
3. **URP**: URP Asset → spunta **Opaque Texture**. Built-in: niente da fare.
4. Attiva il **Bloom**.

Per le build crea tre materiali dagli shader `BlackHole/Horizon`, `BlackHole/AccretionDisk`, `BlackHole/Lens` e assegnali nei campi corrispondenti. Colori, Doppler e turbolenza si regolano sul materiale del **disco**: la lente li copia in automatico.

## Uso come power-up
```csharp
public BlackHole blackHolePrefab;

void LanciaPowerUp(Vector3 pos, float secondi)
{
    BlackHole bh = Instantiate(blackHolePrefab, pos, Quaternion.identity);
    bh.launchOnStart = false;
    bh.Launch(secondi);   // nascita → attivo → morte → si distrugge
}
```
Oppure lascia `launchOnStart` attivo e imposta `duration` sul prefab: parte da solo all'Instantiate.

- `duration` – durata totale in secondi, **nascita e morte incluse** (0 = infinito)
- `birthDuration`, `deathDuration`
- `Kill()` – fa morire subito il buco nero (es. power-up annullato)
- `deathPushForce` – onda d'urto finale che spinge via gli oggetti vicini
- `destroyOnDeath` – se falso il buco nero si nasconde e puoi richiamare `Launch()` (pooling)
- Eventi: `onBirth`, `onAlive`, `onDeath`, `onDied`, `onConsumed` (per suoni, camera shake, VFX)
- `State` – Inattivo / Nascita / Attivo / Morte / Morto

**Nascita:** flash, l'orizzonte si apre con un piccolo rimbalzo, onda d'urto, il disco si espande mentre gira veloce e la gravità sale.
**Morte:** il disco accelera e viene risucchiato, l'orizzonte vibra e collassa, poi flash, onda d'urto e spinta finale.

## Lente gravitazionale
Ora la lente traccia i raggi di luce: il disco si curva sopra e sotto l'ombra, così lo vedi avvolto attorno al buco nero. In più c'è l'anello fotonico.
- `lensing`, `lensStrength`, `lensSize`
- Con la lente attiva, disco e orizzonte sono disegnati dalla lente; le mesh servono solo da fallback quando la lente è spenta.
- Funziona anche con camera ortografica (isometrica).
- Con `diskOrientation = VersoCamera` il disco si vede quasi di taglio anche dall'alto: l'effetto avvolgente risalta di più.

## Altri parametri
Gravità (`mass`, `influenceRadius`, `eventHorizonRadius`, `swirl`, `drag`, `affectedLayers`), inghiottimento (`spaghettify`, `stretchStrength`, `consumeDuration`), crescita (`growOnConsume`), disco (`diskSize`, `diskSpin`), orientamento (`edgeAngle`, `rockAmount`, `precessionSpeed`, `tilt`), `psyche`.
