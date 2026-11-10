using UnityEngine;

namespace AirShotBerserk
{
    /// <summary>Demo: SPAZIO o click sinistro = spara BERSERK.</summary>
    [RequireComponent(typeof(AirShotBerserkVFX))]
    [AddComponentMenu("AirShot/Air Shot BERSERK Demo Input")]
    public class AirShotBerserkDemo : MonoBehaviour
    {
        public KeyCode fireKey = KeyCode.Space;
        public bool fireOnMouseClick = true;
        public bool autoFire = false;
        public float autoFireInterval = 1.6f;
        AirShotBerserkVFX vfx; float next;
        void Awake() { vfx = GetComponent<AirShotBerserkVFX>(); }
        void Update()
        {
            bool fire = Input.GetKeyDown(fireKey) || (fireOnMouseClick && Input.GetMouseButtonDown(0));
            if (autoFire && Time.time >= next) { fire = true; next = Time.time + autoFireInterval; }
            if (fire) vfx.Fire();
        }
    }
}
