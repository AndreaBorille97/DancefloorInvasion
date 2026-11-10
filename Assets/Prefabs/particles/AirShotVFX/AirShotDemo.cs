using UnityEngine;

namespace AirShot
{
    /// <summary>Demo: SPAZIO o click sinistro = spara. Mettilo sullo stesso GameObject di AirShotVFX.</summary>
    [RequireComponent(typeof(AirShotVFX))]
    [AddComponentMenu("AirShot/Air Shot Demo Input")]
    public class AirShotDemo : MonoBehaviour
    {
        public KeyCode fireKey = KeyCode.Space;
        public bool fireOnMouseClick = true;
        public bool autoFire = false;
        public float autoFireInterval = 1.6f;
        AirShotVFX vfx; float next;

        void Awake() { vfx = GetComponent<AirShotVFX>(); }

        void Update()
        {
            bool fire = Input.GetKeyDown(fireKey) || (fireOnMouseClick && Input.GetMouseButtonDown(0));
            if (autoFire && Time.time >= next) { fire = true; next = Time.time + autoFireInterval; }
            if (fire) vfx.Fire();
        }
    }
}
