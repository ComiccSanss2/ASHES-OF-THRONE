using System.Collections.Generic;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>Layer usati dal gioco (creati dal builder in ProjectSettings/TagManager).</summary>
    public static class Layers
    {
        public const string CharacterName = "Character";
        static int _character = -2;
        public static int Character
        {
            get
            {
                if (_character == -2) _character = LayerMask.NameToLayer(CharacterName);
                return _character;
            }
        }

        /// <summary>Maschera per geometria statica (camera, linea di vista): tutto tranne personaggi e IgnoreRaycast.</summary>
        public static int EnvironmentMask
        {
            get
            {
                int mask = ~(1 << 2); // Ignore Raycast
                if (Character >= 0) mask &= ~(1 << Character);
                return mask;
            }
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            if (layer < 0) return;
            go.layer = layer;
            foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
        }
    }

    /// <summary>Qualunque cosa abbia una barra della vita (giocatori, nemici, boss).</summary>
    public interface IHealthView
    {
        bool IsAlive { get; }
        float Health01 { get; }
        string DisplayName { get; }
    }


    public static class MathUtil
    {
        /// <summary>Lerp indipendente dal framerate: sharpness ~ "quanto velocemente" raggiunge il target.</summary>
        public static float Damp(float sharpness, float dt) => 1f - Mathf.Exp(-sharpness * dt);

        public static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        public static float SignedAngleY(Vector3 from, Vector3 to) => Vector3.SignedAngle(Flat(from), Flat(to), Vector3.up);
    }
}
