using UnityEngine;

namespace DarkFantasy
{
    public enum MirrorMove : byte
    {
        None, Awaken, SlamL, SlamR, Embrace, Sweep, Reflection, ShardHail, Vortex, Glide, Stagger, Awakening, Death
    }

    /// <summary>
    /// Pose of the Shattered Mirror of Malice in its BODY space (the body hovers and spins on its
    /// magic axis; +Z faces the players). Hands are wrist positions, the arms solve themselves (IK).
    /// </summary>
    public struct MirrorPose
    {
        public Vector3 handL, handR;
        public Vector3 palmL, palmR;      // hand euler (pitch, yaw, roll) relative to the body
        public float curlL, curlR;        // 0 open claw … 1 fist
        public float bodyYaw, tilt, hover;
        public float glow;                // the glass lights up (magic)
        public float shake;               // shards vibrate (instability)
        public float portal;              // the vortex opens in the glass
        public float slump;               // 1 = collapsed (stagger / death)

        public static MirrorPose Lerp(MirrorPose a, MirrorPose b, float t)
        {
            return new MirrorPose
            {
                handL = Vector3.LerpUnclamped(a.handL, b.handL, t), handR = Vector3.LerpUnclamped(a.handR, b.handR, t),
                palmL = Vector3.LerpUnclamped(a.palmL, b.palmL, t), palmR = Vector3.LerpUnclamped(a.palmR, b.palmR, t),
                curlL = Mathf.LerpUnclamped(a.curlL, b.curlL, t), curlR = Mathf.LerpUnclamped(a.curlR, b.curlR, t),
                bodyYaw = Mathf.LerpUnclamped(a.bodyYaw, b.bodyYaw, t), tilt = Mathf.LerpUnclamped(a.tilt, b.tilt, t),
                hover = Mathf.LerpUnclamped(a.hover, b.hover, t), glow = Mathf.LerpUnclamped(a.glow, b.glow, t),
                shake = Mathf.LerpUnclamped(a.shake, b.shake, t), portal = Mathf.LerpUnclamped(a.portal, b.portal, t),
                slump = Mathf.LerpUnclamped(a.slump, b.slump, t),
            };
        }
    }

    /// <summary>Timings (seconds at rate 1) and poses, shared by the server AI and every client's visuals.</summary>
    public static class MirrorMoves
    {
        // key moments
        public const float SlamImpact = 1.2f, SlamDur = 2.3f;
        public const float EmbraceClap = 1.5f, EmbraceDur = 2.6f;
        public const float SweepStart = 0.9f, SweepEnd = 1.9f, SweepDur = 2.6f;
        public const float ReflectFire = 1.05f, ReflectDur = 2.3f;
        public const float HailStart = 1.0f, HailEnd = 2.0f, HailDur = 3.0f;
        public const float VortexOpen = 0.8f, VortexImplode = 4.2f, VortexDur = 5.0f;
        public const float GlideDur = 1.25f;
        public const float AwakenDur = 3.2f, AwakeningDur = 3.4f, DeathDur = 5f, StaggerDur = 2.6f;
        public const float SweepRadius = 6.6f;

        public static float Duration(MirrorMove m) => m switch
        {
            MirrorMove.SlamL or MirrorMove.SlamR => SlamDur,
            MirrorMove.Embrace => EmbraceDur,
            MirrorMove.Sweep => SweepDur,
            MirrorMove.Reflection => ReflectDur,
            MirrorMove.ShardHail => HailDur,
            MirrorMove.Vortex => VortexDur,
            MirrorMove.Glide => GlideDur,
            MirrorMove.Stagger => StaggerDur,
            MirrorMove.Awaken => AwakenDur,
            MirrorMove.Awakening => AwakeningDur,
            MirrorMove.Death => DeathDur,
            _ => 0f
        };

        // ---------------------------------------------------------------- poses
        public static readonly MirrorPose Rest = new MirrorPose
        {
            handL = new Vector3(-3.7f, 0.45f, 2.3f), handR = new Vector3(3.7f, 0.45f, 2.3f),
            palmL = new Vector3(0f, 15f, 0f), palmR = new Vector3(0f, -15f, 0f),
            curlL = 0.45f, curlR = 0.45f, hover = 0.7f, glow = 0.25f
        };

        static float Seg(float t, float a, float b) => Mathf.Clamp01((t - a) / Mathf.Max(0.0001f, b - a));
        static float Ease(float x) => x * x * (3f - 2f * x);
        static float EaseIn(float x) => x * x * x;
        static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);

        /// <summary>
        /// p1, p2: the slam target in body space (x, z); for Glide p1 = side. <paramref name="t"/> is
        /// already scaled by the move's rate (phase 2 moves are faster).
        /// </summary>
        public static MirrorPose Evaluate(MirrorMove m, float t, float p1, float p2)
        {
            var r = Rest;
            switch (m)
            {
                case MirrorMove.SlamL:
                case MirrorMove.SlamR:
                {
                    bool left = m == MirrorMove.SlamL;
                    float side = left ? -1f : 1f;
                    Vector3 target = new Vector3(p1, 0.35f, p2);
                    var raise = r;
                    Vector3 up = new Vector3(side * 3.2f, 7.2f, 0.4f);
                    var lift = r; lift.tilt = -6f; lift.glow = 0.4f;
                    var hit = r; hit.tilt = 9f; hit.hover = 0.45f;
                    if (left) { raise.handL = up; raise.palmL = new Vector3(-60f, 20f, 0f); raise.curlL = 0.15f; hit.handL = target; hit.palmL = new Vector3(10f, 10f, 0f); hit.curlL = 0.95f; }
                    else { raise.handR = up; raise.palmR = new Vector3(-60f, -20f, 0f); raise.curlR = 0.15f; hit.handR = target; hit.palmR = new Vector3(10f, -10f, 0f); hit.curlR = 0.95f; }
                    raise.tilt = -8f; raise.glow = 0.45f; raise.shake = 0.2f;
                    if (t < 0.85f) return MirrorPose.Lerp(r, raise, Ease(Seg(t, 0f, 0.85f)));
                    if (t < 1.02f)
                    {
                        var hold = raise; float k = Seg(t, 0.85f, 1.02f);
                        if (left) hold.handL += Vector3.up * 0.4f * k; else hold.handR += Vector3.up * 0.4f * k;
                        return hold;
                    }
                    if (t < SlamImpact) { var up2 = raise; if (left) up2.handL += Vector3.up * 0.4f; else up2.handR += Vector3.up * 0.4f; return MirrorPose.Lerp(up2, hit, EaseIn(Seg(t, 1.02f, SlamImpact))); }
                    if (t < 1.9f) { var planted = hit; planted.tilt = Mathf.Lerp(9f, 4f, Seg(t, SlamImpact, 1.9f)); return planted; }
                    return MirrorPose.Lerp(hit, r, Ease(Seg(t, 1.9f, SlamDur)));
                }
                case MirrorMove.Embrace:
                {
                    var wide = r;
                    wide.handL = new Vector3(-6.4f, 3.2f, 3.6f); wide.handR = new Vector3(6.4f, 3.2f, 3.6f);
                    wide.palmL = new Vector3(0f, 90f, 90f); wide.palmR = new Vector3(0f, -90f, -90f);
                    wide.curlL = wide.curlR = 0.1f; wide.glow = 0.55f; wide.tilt = -4f;
                    var shut = wide;
                    shut.handL = new Vector3(-0.8f, 2.4f, 4.8f); shut.handR = new Vector3(0.8f, 2.4f, 4.8f);
                    shut.curlL = shut.curlR = 0.75f; shut.tilt = 8f; shut.shake = 0.5f;
                    if (t < 1.0f) return MirrorPose.Lerp(r, wide, Ease(Seg(t, 0f, 1.0f)));
                    if (t < 1.3f) { var w = wide; float tr = Mathf.Sin(t * 70f) * 0.06f; w.handL.x += tr; w.handR.x -= tr; w.shake = 0.3f; return w; }
                    if (t < EmbraceClap) return MirrorPose.Lerp(wide, shut, EaseIn(Seg(t, 1.3f, EmbraceClap)));
                    if (t < 1.95f) return shut;
                    return MirrorPose.Lerp(shut, r, Ease(Seg(t, 1.95f, EmbraceDur)));
                }
                case MirrorMove.Sweep:
                {
                    // the right hand drops low behind the frame, then the frame spins a full turn on its axis
                    var low = r;
                    low.handR = new Vector3(SweepRadius, 0.55f, -1.2f);
                    low.palmR = new Vector3(0f, -90f, -80f); low.curlR = 0.2f;
                    low.handL = new Vector3(-3.0f, 1.6f, 1.2f); low.curlL = 0.7f;
                    low.tilt = 6f; low.glow = 0.4f; low.hover = 0.5f;
                    if (t < SweepStart) { var p = MirrorPose.Lerp(r, low, Ease(Seg(t, 0f, SweepStart))); p.bodyYaw = -35f * Ease(Seg(t, 0.2f, SweepStart)); return p; }
                    if (t < SweepEnd) { var p = low; p.bodyYaw = -35f + 395f * Ease(Seg(t, SweepStart, SweepEnd)); p.shake = 0.6f; return p; }
                    var end = low; end.bodyYaw = 360f;
                    var back = r; back.bodyYaw = 360f;
                    return MirrorPose.Lerp(end, back, Ease(Seg(t, SweepEnd, SweepDur)));
                }
                case MirrorMove.Reflection:
                {
                    var open = r;
                    open.handL = new Vector3(-4.4f, 4.8f, 0.6f); open.handR = new Vector3(4.4f, 4.8f, 0.6f);
                    open.palmL = new Vector3(-20f, 60f, 70f); open.palmR = new Vector3(-20f, -60f, -70f);
                    open.curlL = open.curlR = 0f; open.glow = 1.6f; open.tilt = -10f; open.shake = 0.35f;
                    if (t < ReflectFire) return MirrorPose.Lerp(r, open, Ease(Seg(t, 0f, ReflectFire)));
                    if (t < 1.4f) { var o = open; o.glow = Mathf.Lerp(2.4f, 1f, Seg(t, ReflectFire, 1.4f)); o.tilt = -4f; return o; }
                    return MirrorPose.Lerp(open, r, Ease(Seg(t, 1.4f, ReflectDur)));
                }
                case MirrorMove.ShardHail:
                {
                    var brace = r;
                    brace.handL = new Vector3(-3.4f, 0.4f, 1.4f); brace.handR = new Vector3(3.4f, 0.4f, 1.4f);
                    brace.curlL = brace.curlR = 1f; brace.tilt = -12f; brace.glow = 1.2f; brace.shake = 1f; brace.hover = 1.3f;
                    if (t < HailStart) { var p = MirrorPose.Lerp(r, brace, Ease(Seg(t, 0f, HailStart))); p.shake = Seg(t, 0f, HailStart); return p; }
                    if (t < HailEnd) { var p = brace; p.hover += Mathf.Sin(t * 40f) * 0.05f; p.glow = 1.2f + Mathf.Sin(t * 25f) * 0.4f; return p; }
                    return MirrorPose.Lerp(brace, r, Ease(Seg(t, HailEnd, HailDur)));
                }
                case MirrorMove.Vortex:
                {
                    var hold = r;
                    hold.handL = new Vector3(-4.2f, 5.4f, 0.2f); hold.handR = new Vector3(4.2f, 5.4f, 0.2f);
                    hold.palmL = new Vector3(0f, 80f, 90f); hold.palmR = new Vector3(0f, -80f, -90f);
                    hold.curlL = hold.curlR = 0.3f; hold.portal = 1f; hold.glow = 1.4f; hold.shake = 0.7f; hold.tilt = -6f;
                    if (t < VortexOpen) return MirrorPose.Lerp(r, hold, Ease(Seg(t, 0f, VortexOpen)));
                    if (t < VortexImplode) { var p = hold; p.portal = 1f + Mathf.Sin(t * 9f) * 0.08f; return p; }
                    var slam = hold; slam.portal = 0f; slam.glow = 3f; slam.handL = new Vector3(-0.9f, 4.2f, 1.2f); slam.handR = new Vector3(0.9f, 4.2f, 1.2f); slam.curlL = slam.curlR = 1f;
                    if (t < VortexImplode + 0.25f) return MirrorPose.Lerp(hold, slam, EaseIn(Seg(t, VortexImplode, VortexImplode + 0.25f)));
                    var after = slam; after.glow = 0.3f;
                    return MirrorPose.Lerp(after, r, Ease(Seg(t, VortexImplode + 0.25f, VortexDur)));
                }
                case MirrorMove.Glide:
                {
                    // dimensional lurch: hands drag behind, the frame leans into the dash
                    var g = r;
                    g.handL = new Vector3(-3.4f, 0.5f, -2.2f); g.handR = new Vector3(3.4f, 0.5f, -2.2f);
                    g.curlL = g.curlR = 0.8f; g.tilt = 14f; g.shake = 1f; g.hover = 1.1f; g.glow = 0.6f;
                    float k = t < 0.25f ? Ease(Seg(t, 0f, 0.25f)) : 1f - Ease(Seg(t, 0.85f, GlideDur));
                    return MirrorPose.Lerp(r, g, k);
                }
                case MirrorMove.Stagger:
                {
                    var down = r;
                    down.handL = new Vector3(-3.2f, 0.3f, 1.8f); down.handR = new Vector3(3.2f, 0.3f, 1.8f);
                    down.curlL = down.curlR = 0.1f; down.tilt = 18f; down.hover = 0.1f; down.glow = 0.05f; down.slump = 1f;
                    if (t < 0.35f) return MirrorPose.Lerp(r, down, EaseOut(Seg(t, 0f, 0.35f)));
                    if (t < 2.1f) { var d = down; d.tilt += Mathf.Sin(t * 3f) * 1.5f; return d; }
                    return MirrorPose.Lerp(down, r, Ease(Seg(t, 2.1f, StaggerDur)));
                }
                case MirrorMove.Awaken:
                {
                    var dormant = r; dormant.hover = 0f; dormant.tilt = 22f; dormant.glow = 0f; dormant.slump = 1f;
                    dormant.handL = new Vector3(-2.6f, 0.25f, 1.2f); dormant.handR = new Vector3(2.6f, 0.25f, 1.2f); dormant.curlL = dormant.curlR = 0.05f;
                    var rise = r; rise.handL = new Vector3(-4.6f, 6.4f, 1.0f); rise.handR = new Vector3(4.6f, 6.4f, 1.0f); rise.curlL = rise.curlR = 0.9f; rise.tilt = -12f; rise.glow = 1.6f; rise.hover = 1.4f; rise.shake = 1f;
                    var slam = r; slam.handL = new Vector3(-4.0f, 0.35f, 3.4f); slam.handR = new Vector3(4.0f, 0.35f, 3.4f); slam.curlL = slam.curlR = 1f; slam.tilt = 8f; slam.glow = 0.8f;
                    if (t < 0.6f) return dormant;
                    if (t < 2.2f) return MirrorPose.Lerp(dormant, rise, Ease(Seg(t, 0.6f, 2.2f)));
                    if (t < 2.45f) return MirrorPose.Lerp(rise, slam, EaseIn(Seg(t, 2.2f, 2.45f)));
                    return MirrorPose.Lerp(slam, r, Ease(Seg(t, 2.6f, AwakenDur)));
                }
                case MirrorMove.Awakening:
                {
                    // phase 2: the glass cracks through, the frame rises screaming
                    var cry = r;
                    cry.handL = new Vector3(-5.6f, 7.2f, -0.6f); cry.handR = new Vector3(5.6f, 7.2f, -0.6f);
                    cry.palmL = new Vector3(-40f, 40f, 30f); cry.palmR = new Vector3(-40f, -40f, -30f);
                    cry.curlL = cry.curlR = 0f; cry.tilt = -16f; cry.hover = 2.2f; cry.glow = 2.6f; cry.shake = 1f;
                    if (t < 1.2f) return MirrorPose.Lerp(r, cry, Ease(Seg(t, 0f, 1.2f)));
                    if (t < 2.6f) { var c = cry; c.shake = 1f; c.hover += Mathf.Sin(t * 30f) * 0.08f; return c; }
                    return MirrorPose.Lerp(cry, r, Ease(Seg(t, 2.6f, AwakeningDur)));
                }
                case MirrorMove.Death:
                {
                    var fall = r; fall.hover = 0f; fall.tilt = 30f; fall.glow = 0f; fall.slump = 1f;
                    fall.handL = new Vector3(-3.9f, 0.2f, 2.8f); fall.handR = new Vector3(3.9f, 0.2f, 2.8f); fall.curlL = fall.curlR = 0f;
                    var cry = r; cry.handL = new Vector3(-5f, 6f, 0f); cry.handR = new Vector3(5f, 6f, 0f); cry.glow = 3f; cry.shake = 1f; cry.tilt = -14f; cry.hover = 1.5f;
                    if (t < 1.4f) return MirrorPose.Lerp(r, cry, Ease(Seg(t, 0f, 1.4f)));
                    return MirrorPose.Lerp(cry, fall, EaseOut(Seg(t, 1.6f, 3.2f)));
                }
            }
            return r;
        }
    }
}
