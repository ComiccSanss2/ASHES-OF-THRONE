using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// Boss armour worn on the player (helmet, armour, legs, boots). The pieces are rigid shells fitted
    /// on the bones by the builder (inactive); the equipped set of each slot is replicated in 16 bits
    /// and everybody shows the same pieces. A piece of a set whose boss has fallen in this lobby
    /// also reduces the damage taken.
    /// </summary>
    public class PlayerArmor : NetworkBehaviour
    {
        [System.Serializable]
        public class Piece
        {
            public byte set;
            public Inventory.Slot slot;
            public GameObject go;
        }

        public List<Piece> pieces = new List<Piece>();
        [Tooltip("Parts of the class's own look hidden by a helmet (hood, hat…).")]
        public GameObject[] hideWithHelmet;

        readonly NetworkVariable<ushort> _equipped = new NetworkVariable<ushort>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        PlayerController _pc;

        public ushort Packed => _equipped.Value;
        public byte Equipped(Inventory.Slot s) => Inventory.Unpack(_equipped.Value, s);
        public static event System.Action<PlayerArmor> VisualChanged;

        void Awake() => _pc = GetComponent<PlayerController>();

        public override void OnNetworkSpawn()
        {
            _equipped.OnValueChanged += OnChanged;
            if (IsOwner)
            {
                Inventory.Changed += OwnerSync;
                OwnerSync();
            }
            Apply(_equipped.Value);
        }

        public override void OnNetworkDespawn()
        {
            _equipped.OnValueChanged -= OnChanged;
            Inventory.Changed -= OwnerSync;
        }

        void OwnerSync()
        {
            if (!IsOwner || _pc == null) return;
            ushort v = Inventory.PackArmor(_pc.playerClass);
            if (_equipped.Value != v) _equipped.Value = v;
            else Apply(v);
        }

        void OnChanged(ushort a, ushort b) => Apply(b);

        /// <summary>Shows the pieces of a packed equipment (also used by the inventory's preview).</summary>
        public void Apply(ushort packed)
        {
            foreach (var p in pieces)
                if (p.go) p.go.SetActive(p.set != Inventory.ArmorNone && Inventory.Unpack(packed, p.slot) == p.set);
            bool helm = Inventory.Unpack(packed, Inventory.Slot.Helmet) != Inventory.ArmorNone;
            if (hideWithHelmet != null) foreach (var g in hideWithHelmet) if (g) g.SetActive(!helm);
            TuckBody(packed);
            VisualChanged?.Invoke(this);
        }

        // ================================================================== the body under the plates
        [Tooltip("The character's skin: under every worn piece it is drawn in toward the bones, so it never pokes through.")]
        public SkinnedMeshRenderer body;

        Mesh _origMesh, _tucked;
        Vector3[] _origVerts, _work;
        int[] _vBone;            // dominant bone of each vertex
        Vector3[] _segA, _segB;  // each bone as a segment (bind space): joint → first child joint
        int _lastTuck = -1;

        static readonly (Inventory.Slot slot, string[] bones, float keep)[] Cover =
        {
            (Inventory.Slot.Helmet, new[] { "Head" }, 0.78f),
            (Inventory.Slot.Chest, new[] { "Hips", "Spine", "Spine 1", "Chest", "Neck", "Shoulder_L", "Shoulder_R" }, 0.8f),
            (Inventory.Slot.Legs, new[] { "Upper_Leg_L", "Upper_Leg_R", "Lower_Leg_L", "Lower_Leg_R" }, 0.78f),
            (Inventory.Slot.Boots, new[] { "Foot_L", "Foot_R", "Toes_L", "Toes_R" }, 0.75f),
        };

        void PrepareTuck()
        {
            if (_origMesh != null || body == null || body.sharedMesh == null) return;
            _origMesh = body.sharedMesh;
            if (!_origMesh.isReadable) { body = null; return; }
            _origVerts = _origMesh.vertices;
            _work = (Vector3[])_origVerts.Clone();
            var w = _origMesh.boneWeights;
            _vBone = new int[_origVerts.Length];
            for (int i = 0; i < _vBone.Length; i++) _vBone[i] = i < w.Length ? w[i].boneIndex0 : -1;
            var bones = body.bones; var bind = _origMesh.bindposes;
            _segA = new Vector3[bones.Length]; _segB = new Vector3[bones.Length];
            for (int b = 0; b < bones.Length; b++)
            {
                _segA[b] = bind[b].inverse.GetColumn(3);
                _segB[b] = _segA[b];
                if (bones[b] == null) continue;
                for (int c = 0; c < bones.Length; c++)
                    if (bones[c] != null && bones[c].parent == bones[b]) { _segB[b] = bind[c].inverse.GetColumn(3); break; }
            }
            _tucked = Instantiate(_origMesh);
            _tucked.name = _origMesh.name + "_tucked";
            body.sharedMesh = _tucked;
        }

        void TuckBody(ushort packed)
        {
            if (body == null || _lastTuck == packed) return;
            PrepareTuck();
            if (_tucked == null) return;
            _lastTuck = packed;
            var bones = body.bones;
            var keep = new float[bones.Length];
            for (int b = 0; b < keep.Length; b++) keep[b] = 1f;
            foreach (var (slot, names, k) in Cover)
            {
                if (Inventory.Unpack(packed, slot) == Inventory.ArmorNone) continue;
                for (int b = 0; b < bones.Length; b++)
                    if (bones[b] != null && System.Array.IndexOf(names, bones[b].name) >= 0) keep[b] = k;
            }
            for (int i = 0; i < _origVerts.Length; i++)
            {
                int b = _vBone[i];
                float k = b >= 0 && b < keep.Length ? keep[b] : 1f;
                if (k >= 1f) { _work[i] = _origVerts[i]; continue; }
                Vector3 a = _segA[b], ab = _segB[b] - a, v = _origVerts[i];
                float t = ab.sqrMagnitude < 1e-8f ? 0f : Mathf.Clamp01(Vector3.Dot(v - a, ab) / ab.sqrMagnitude);
                Vector3 axis = a + ab * t;
                _work[i] = axis + (v - axis) * k;
            }
            _tucked.vertices = _work;
            _tucked.RecalculateBounds();
        }

        /// <summary>Owner: damage taken multiplier from the pieces whose boss has fallen in this lobby.</summary>
        public float DamageTakenMul
        {
            get
            {
                float red = 0f;
                for (int i = 0; i < Inventory.ArmorSlotCount; i++)
                {
                    byte set = Equipped((Inventory.Slot)i);
                    if (set != Inventory.ArmorNone && CastleProgress.ArmorEarned(set)) red += Inventory.ArmorReduction(set);
                }
                return 1f - Mathf.Min(red, 0.35f);
            }
        }
    }
}
