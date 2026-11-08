using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Hitbox a capsula sull'arma (dall'elsa alla punta). Durante la finestra attiva fa uno
    /// "sweep" tra la posizione del frame precedente e quella attuale (più campioni) così anche
    /// i fendenti veloci non attraversano il bersaglio senza colpirlo.
    ///
    /// Si attiva in due modi:
    ///  1. Da codice (BeginSwing / EndSwing) secondo i tempi di AttackData — usato di default;
    ///  2. Da Animation Event "HitboxOn" / "HitboxOff" (vedi AnimationEventRelay) se aggiungi
    ///     gli eventi ai clip nell'Import Settings dell'FBX.
    /// </summary>
    public class WeaponHitbox : MonoBehaviour
    {
        public Transform basePoint;
        public Transform tipPoint;
        public float radius = 0.12f;
        [Tooltip("Il collider del proprietario da ignorare.")]
        public Transform ownerRoot;
        public bool drawGizmos = true;

        public bool Active { get; private set; }
        public event Action<Collider, Vector3> OnHit;

        readonly HashSet<Transform> _hitRoots = new HashSet<Transform>();
        readonly Collider[] _buffer = new Collider[16];
        Vector3 _prevBase, _prevTip;

        public void BeginSwing()
        {
            _hitRoots.Clear();
            Active = true;
            _prevBase = basePoint.position;
            _prevTip = tipPoint.position;
        }

        public void EndSwing() => Active = false;

        /// <summary>World velocity of the blade tip (direction of the cut, used by the slash VFX).</summary>
        public Vector3 TipVelocity { get; private set; }
        Vector3 _trackTip;

        void LateUpdate()
        {
            if (tipPoint != null)
            {
                Vector3 tp = tipPoint.position;
                if (Time.deltaTime > 0f) TipVelocity = (tp - _trackTip) / Time.deltaTime;
                _trackTip = tp;
            }
            if (!Active || basePoint == null || tipPoint == null) return;
            Vector3 b = basePoint.position, t = tipPoint.position;
            const int samples = 4;
            for (int s = 1; s <= samples; s++)
            {
                float k = s / (float)samples;
                Vector3 sb = Vector3.Lerp(_prevBase, b, k), st = Vector3.Lerp(_prevTip, t, k);
                int n = Physics.OverlapCapsuleNonAlloc(sb, st, radius, _buffer, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < n; i++)
                {
                    var col = _buffer[i];
                    Transform root = col.attachedRigidbody ? col.attachedRigidbody.transform : col.transform.root;
                    if (root == ownerRoot || (ownerRoot != null && col.transform.IsChildOf(ownerRoot))) continue;
                    var hurt = col.GetComponentInParent<IHurtbox>();
                    if (hurt == null) continue;
                    Transform key = ((Component)hurt).transform;
                    if (!_hitRoots.Add(key)) continue;
                    OnHit?.Invoke(col, col.ClosestPoint((sb + st) * 0.5f));
                }
            }
            _prevBase = b; _prevTip = t;
        }

        void OnDrawGizmos()
        {
            if (!drawGizmos || basePoint == null || tipPoint == null) return;
            Gizmos.color = Active ? Color.red : new Color(1, 0.6f, 0, 0.5f);
            Gizmos.DrawWireSphere(basePoint.position, radius);
            Gizmos.DrawWireSphere(tipPoint.position, radius);
            Gizmos.DrawLine(basePoint.position, tipPoint.position);
        }
    }

    /// <summary>Marca un oggetto colpibile (giocatore, nemico, boss).</summary>
    public interface IHurtbox
    {
        bool IsPlayer { get; }
    }

}
