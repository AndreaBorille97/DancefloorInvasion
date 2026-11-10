# Mini fungo atomico 3D — Unity

Porting 1:1 del prototipo "Fungo Atomico 3D": stessa simulazione, stesso shader.
È un effetto 3D vero: puoi girarci attorno con la camera.

## Installazione
1. Copia `MiniNuke3D.cs` e `MiniNukePuff.shader` in `Assets/` (stessa cartella va bene).
2. **Per le build**: Project Settings → Graphics → Always Included Shaders → aggiungi `MiniNuke/Puff`
   (oppure passa lo shader a `Spawn`, vedi sotto). In Editor funziona anche senza.

## Uso
```csharp
public Shader nukeShader; // trascina MiniNukePuff.shader (opzionale se è in Always Included)

void OnTruckDestroyed(Transform truck)
{
    MiniNuke3D.Spawn(truck.position, 1f, nukeShader);
    Destroy(truck.gameObject);
}
```
Si autodistrugge dopo 3.6 s.

## Parametri
- `scale` — grandezza (1 = fungo alto ~3.7 m). `unitsPerPx` cambia la scala base.
- `speed` — velocità animazione.
- `cameraShake` / `shakeAmount` — disattiva se usi già un tuo sistema di shake o Cinemachine.
- `flashLight` — luce puntiforme del flash.
- `loop` — per provarlo in scena.

## Note
- Built-in e URP: sì. HDRP: no (serve uno shader HDRP).
- Usa `Camera.main` per orientare i puff: la camera di gioco deve avere il tag MainCamera.
- Funziona sia in Linear sia in Gamma color space.
