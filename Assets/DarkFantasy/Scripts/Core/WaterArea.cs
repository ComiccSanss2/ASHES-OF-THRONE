using System.Collections.Generic;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Shallow water lying on the floor of the castle corridors (a box: anything whose feet are
    /// inside wades in it). Visual surface + registration for FootSplash. Scene object.
    /// </summary>
    public class WaterArea : MonoBehaviour
    {
        public static readonly List<WaterArea> All = new List<WaterArea>();
        [Tooltip("Local box of the water (feet inside = wading).")]
        public Vector3 size = new Vector3(4, 0.5f, 10);
        public float surfaceY;   // world height of the surface

        Bounds _b;
        void OnEnable() { All.Add(this); _b = new Bounds(transform.position, size); surfaceY = transform.position.y + size.y * 0.5f - 0.2f; }
        void OnDisable() => All.Remove(this);

        public static bool Find(Vector3 feet, out float surface)
        {
            foreach (var w in All)
                if (w._b.Contains(feet)) { surface = w.surfaceY; return true; }
            surface = 0f;
            return false;
        }
    }
}
