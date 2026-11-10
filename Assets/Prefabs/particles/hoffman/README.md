# Apeiron — area VFX for Unity

Unlit transparent shader + controller. Works in Built-in and URP.

## Install
Copy the `Unity` folder into `Assets/` (keep `Editor/` as a subfolder).

## Quick start
1. Hierarchy → right click → **Effects → Apeiron Area VFX**. This creates the object, the material (`Assets/ApeironVFX.mat`) and links the main camera for shake.
2. Press Play, then hit **Space** (or the **Cast (test)** button in the Inspector).
3. From code: `GetComponent<ApeironVFX>().Cast(worldPosition);`

## Inspector
- **Area**: `radius` (world units, default 7), `duration` (s, default 3)
- **Kill**: layers / tag filter, `destroyEnemies`, `onEnemyKilled` event. Enemies need a Collider.
- **Look**: `vary` (off = every cast identical), `phase` (default 20: starting point of the fractal evolution)
- **Camera shake**: camera + strength
- **Effect look (material)**: Fractal, Tentacles, Gas and Color sliders, editable straight from the same Inspector

Defaults match the web preview.
