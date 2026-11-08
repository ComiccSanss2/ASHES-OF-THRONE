using System.Collections.Generic;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Bersaglio agganciabile dal lock-on. Si registra in una lista statica così la camera
    /// non deve fare FindObjectsOfType ad ogni pressione.
    /// </summary>
    public class LockOnTarget : MonoBehaviour
    {
        public static readonly List<LockOnTarget> All = new List<LockOnTarget>();

        [Tooltip("Punto mirato dalla camera (petto/testa). Se nullo usa transform + offset.")]
        public Transform lockPoint;
        public float heightOffset = 1.3f;
        [Tooltip("Il boss è più grande: la camera si allontana un po' di più.")]
        public float framingSize = 1f;

        IHealthView _health;

        public Vector3 Point => lockPoint ? lockPoint.position : transform.position + Vector3.up * heightOffset;
        public bool IsValid => isActiveAndEnabled && (_health == null || _health.IsAlive);
        public IHealthView Health => _health;

        void Awake() => _health = GetComponent<IHealthView>();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);
    }
}
