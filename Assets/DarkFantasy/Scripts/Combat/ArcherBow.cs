using System;
using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// The archer's war-bow, driven entirely by the animation so it looks the same on every client:
    ///  - the string is a 2/3-point line between the limb tips (bones BowTop / BowBottom exported
    ///    with the animations); while a shot is drawn its middle follows the right hand (NockSocket);
    ///  - an arrow appears on the string from the moment the hand brings it to the bow until the
    ///    release frame;
    ///  - the draw creak and the release twang are timed from the same clip windows.
    /// The windows (clip seconds) come from the Blender timeline (Tools/archer_anim.py).
    /// </summary>
    public class ArcherBow : MonoBehaviour
    {
        [Serializable]
        public struct DrawWindow
        {
            public string state;
            [Tooltip("Clip time the arrow is on the string.")] public float nock;
            [Tooltip("Clip time the string is let go.")] public float release;
        }

        public CharacterAnimator anim;
        public Transform grip, bowTop, bowBottom, nock;
        public LineRenderer stringLine;
        public Transform nockedArrow;
        [Tooltip("Projectile prefab (Arrow component), spawned locally on every client.")]
        public GameObject arrowPrefab;
        public DrawWindow[] draws;

        public bool IsDrawn { get; private set; }
        /// <summary>Where a released arrow starts (the nock point on the string).</summary>
        public Vector3 NockPosition => nock ? nock.position : transform.position;
        public Vector3 GripPosition => grip ? grip.position : transform.position;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Development: logs the draw state (editor test bridge: msg:Bow,DevBow,x).</summary>
        public void DevBow(string _)
        {
            var sb = new System.Text.StringBuilder($"[DF] bow drawn={IsDrawn} amount={_drawAmount:F2} anim={(anim ? anim.name : "null")} draws={(draws == null ? -1 : draws.Length)}");
            if (anim && draws != null) foreach (var d in draws) sb.Append($" | {d.state} t={anim.GetStateTime(d.state):F2} raw={anim.DevRawStateTime(d.state):F2} speed={anim.PlaybackSpeed:F3} [{d.nock:F2},{d.release:F2})");
            Debug.Log(sb.ToString());
        }
#endif

        float _vibrate;
        bool _wasDrawn;
        float _drawAmount;   // 0 = rest, 1 = string on the hand (eases the snap when the hand arrives)

        void Awake()
        {
            if (anim == null) anim = GetComponentInParent<CharacterAnimator>();
            if (stringLine) { stringLine.useWorldSpace = true; stringLine.positionCount = 3; }
            if (nockedArrow) nockedArrow.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (!bowTop || !bowBottom) return;
            bool drawn = false;
            if (anim != null && draws != null)
                foreach (var d in draws)
                {
                    float t = anim.GetStateTime(d.state);
                    if (t >= d.nock && t < d.release) { drawn = true; break; }
                }
            // aiming on the move: the bow is up with an arrow on the string (not drawn)
            bool ready = !drawn && anim != null && anim.GuardRaised && anim.IsInState(CharacterAnimator.Locomotion);

            if (drawn && !_wasDrawn) Sfx.Play(SfxId.BowDraw, GripPosition, 0.9f);
            if (!drawn && _wasDrawn && !ready) { _vibrate = 1f; Sfx.Play(SfxId.BowRelease, GripPosition, 0.9f); }
            _wasDrawn = drawn;
            IsDrawn = drawn;
            bool onString = drawn || ready;
            _drawAmount = Mathf.MoveTowards(_drawAmount, onString ? 1f : 0f, Time.deltaTime * (onString ? 14f : 40f));

            Vector3 top = bowTop.position, bot = bowBottom.position;
            Vector3 rest = (top + bot) * 0.5f;
            // the string follows the hand only once it is behind the braced string (a hand still
            // in front of it, e.g. while nocking, leaves the string straight)
            Vector3 hand = nock ? nock.position : rest;
            if (Vector3.Dot(hand - rest, GripPosition - rest) > 0f) hand = rest;
            Vector3 mid = Vector3.Lerp(rest, hand, _drawAmount);
            if (_vibrate > 0f)
            {
                // the string shivers for a moment after the release
                _vibrate = Mathf.Max(0f, _vibrate - Time.deltaTime * 6f);
                Vector3 side = grip ? grip.right : transform.right;
                mid += side * Mathf.Sin(Time.time * 140f) * 0.018f * _vibrate;
            }
            if (stringLine)
            {
                stringLine.SetPosition(0, top);
                stringLine.SetPosition(1, mid);
                stringLine.SetPosition(2, bot);
            }

            if (nockedArrow)
            {
                bool show = onString && _drawAmount > 0.5f;
                if (nockedArrow.gameObject.activeSelf != show) nockedArrow.gameObject.SetActive(show);
                if (show)
                {
                    Vector3 dir = GripPosition - mid;
                    if (dir.sqrMagnitude > 1e-4f)
                        nockedArrow.SetPositionAndRotation(mid, Quaternion.LookRotation(dir.normalized, grip ? grip.up : Vector3.up));
                }
            }
        }
    }
}
