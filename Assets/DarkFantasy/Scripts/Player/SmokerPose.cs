using UnityEngine;

namespace DarkFantasy
{
    /// <summary>
    /// The smoker brings the mouthpiece of his pipe to his lips: a two-bone IK on the right arm,
    /// layered on top of the bard clips after the Animator.
    ///  - Drawing (right button held): the pipe stays at the mouth, breathing in slowly, for as long
    ///    as the button is held.
    ///  - Puff / Choking Gust / Exhale / Great Exhale: the pipe comes up to the mouth, he takes a pull,
    ///    then lowers it as the smoke (or the spirits) leaves his mouth.
    /// Runs on every client (the drawing flag and the cast animations are networked).
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class SmokerPose : MonoBehaviour
    {
        public SmokerCaster smoker;
        public Animator animator;
        public Transform upperArm, foreArm, hand, head, pipe;
        [Tooltip("Distance from the grip (pipe origin) down to the mouthpiece, metres.")]
        public float mouthpiece = 0.4f;
        public float mouthUp = 0.13f, mouthForward = 0.13f;
        [Tooltip("How far the pipe points down from the horizontal while smoked.")]
        public float pitchDown = 28f;

        float _w, _breath;
        Vector3 _offLocal;
        Quaternion _rotLocal;
        bool _ready;

        public float Weight => _w;
        public static bool Debug;

        void Start()
        {
            if (!hand || !pipe || !upperArm || !foreArm || !head) { enabled = false; return; }
            _offLocal = Quaternion.Inverse(hand.rotation) * (pipe.position - hand.position);
            _rotLocal = Quaternion.Inverse(hand.rotation) * pipe.rotation;
            _ready = true;
        }

        static float Curve(float t)
        {
            // up to the mouth by 32 %, the pull, the smoke leaves at ~45 %, lowered by 85 %
            if (t < 0.32f) return Mathf.SmoothStep(0f, 1f, t / 0.32f);
            if (t < 0.55f) return 1f;
            if (t < 0.85f) return Mathf.SmoothStep(1f, 0f, (t - 0.55f) / 0.3f);
            return 0f;
        }

        static bool IsCast(AnimatorStateInfo s) => s.IsName(CharacterAnimator.Attack1) || s.IsName(CharacterAnimator.Spin) || s.IsName(CharacterAnimator.Heavy);

        float Target(out float pull)
        {
            pull = 0f;
            if (smoker && smoker.pc && smoker.pc.IsDead) return 0f;
            if (smoker && smoker.Inhaling) return 1f;
            if (!animator) return 0f;
            var cur = animator.GetCurrentAnimatorStateInfo(0);
            float w = 0f;
            if (IsCast(cur)) { float t = cur.normalizedTime; w = Curve(t); pull = Mathf.Clamp01((t - 0.42f) / 0.15f) * (t < 0.6f ? 1f : Mathf.Clamp01((0.85f - t) / 0.25f)); }
            if (animator.IsInTransition(0))
            {
                var nx = animator.GetNextAnimatorStateInfo(0);
                if (IsCast(nx)) w = Mathf.Max(w, Curve(nx.normalizedTime));
            }
            return w;
        }

        void LateUpdate()
        {
            if (!_ready) return;
            float pull;
            float target = Target(out pull);
            _w = Mathf.Lerp(_w, target, 1f - Mathf.Exp(-16f * Time.deltaTime));
            if (_w < 0.002f) return;
            _breath += Time.deltaTime * (smoker && smoker.Inhaling ? 1.6f : 0f);

            Transform root = smoker ? smoker.transform : transform.root;
            Vector3 up = Vector3.up, fwd = root.forward, right = root.right;
            Vector3 mouth = head.position + up * mouthUp + fwd * mouthForward;
            Vector3 dir = (Quaternion.AngleAxis(pitchDown, right) * fwd + right * 0.12f).normalized;
            // breathing in: the pipe trembles at the lips; exhaling: it is pulled away from the mouth
            float gap = pull * 0.14f + (smoker && smoker.Inhaling ? Mathf.Sin(_breath * 6f) * 0.006f : 0f);
            Vector3 pipePos = mouth + dir * (mouthpiece + gap);
            Quaternion pipeRot = Quaternion.LookRotation(Vector3.ProjectOnPlane(up, dir).normalized, dir);
            Quaternion handRot = pipeRot * Quaternion.Inverse(_rotLocal);
            Vector3 handPos = pipePos - handRot * _offLocal;

            Quaternion a0 = upperArm.rotation, b0 = foreArm.rotation, c0 = hand.rotation;
            Vector3 hint = upperArm.position + right * 0.35f - up * 0.35f + fwd * 0.05f;
            SolveTwoBone(upperArm, foreArm, hand, handPos, hint);
            hand.rotation = handRot;
            Quaternion a1 = upperArm.rotation, b1 = foreArm.rotation, c1 = hand.rotation;
            upperArm.rotation = Quaternion.Slerp(a0, a1, _w);
            foreArm.rotation = Quaternion.Slerp(b0, b1, _w);
            hand.rotation = Quaternion.Slerp(c0, c1, _w);
            if (Debug && Time.frameCount % 30 == 0)
                UnityEngine.Debug.Log($"[DF] smoker pose w={_w:0.00} mouth={mouth} wantHand={handPos} hand={hand.position} pipe0={pipe.position} mouthpiece={pipe.position - pipe.up * mouthpiece * pipe.lossyScale.y} lossy={pipe.lossyScale.y:0.000} shoulder={upperArm.position}");
            // the head bows a little into the pipe while drawing, lifts as the smoke goes out
            float nod = (smoker && smoker.Inhaling ? 6f : 0f) - pull * 10f;
            if (Mathf.Abs(nod) > 0.01f) head.rotation = Quaternion.AngleAxis(nod * _w, right) * head.rotation;
        }

        static void SolveTwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint)
        {
            Vector3 aP = a.position, bP = b.position, cP = c.position;
            float lab = (bP - aP).magnitude, lcb = (cP - bP).magnitude;
            if (lab < 1e-4f || lcb < 1e-4f) return;
            float lat = Mathf.Clamp((target - aP).magnitude, Mathf.Abs(lab - lcb) + 0.01f, lab + lcb - 0.002f);
            // 1. the elbow angle that gives the right distance
            Vector3 u = aP - bP, v = cP - bP;
            float cur = Vector3.Angle(u, v);
            float want = Mathf.Acos(Mathf.Clamp((lab * lab + lcb * lcb - lat * lat) / (2f * lab * lcb), -1f, 1f)) * Mathf.Rad2Deg;
            Vector3 axis = Vector3.Cross(u, v);
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(u, hint - bP);
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.right;
            b.rotation = Quaternion.AngleAxis(want - cur, axis.normalized) * b.rotation;
            // 2. swing the chain onto the target
            a.rotation = Quaternion.FromToRotation(c.position - aP, target - aP) * a.rotation;
            // 3. turn the elbow toward the hint
            Vector3 d = (target - aP).normalized;
            Vector3 e = Vector3.ProjectOnPlane(b.position - aP, d), h = Vector3.ProjectOnPlane(hint - aP, d);
            if (e.sqrMagnitude > 1e-6f && h.sqrMagnitude > 1e-6f)
                a.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(e, h, d), d) * a.rotation;
        }
    }
}
