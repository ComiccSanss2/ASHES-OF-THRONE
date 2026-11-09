using UnityEngine;

namespace DarkFantasy
{
    public enum DragonMove : byte { None, Bite, DoubleBite, TailSweep, Breath, Stomp, Charge, Roar, TakeOff, FireRain, Land, Stagger, Death, Awaken, Pounce }

    /// <summary>
    /// Procedural pose of the dragon at time t of a move, evaluated identically by the server (for
    /// the damage timing) and by every client (DragonVisuals, layered on top of the animation clips).
    /// Angles in degrees: neckPitch &gt; 0 brings the head down/forward, &lt; 0 rears it up;
    /// bodyPitch &lt; 0 lifts the front of the body (rearing).
    /// </summary>
    public struct DragonPose
    {
        public float neckPitch, neckYaw, headPitch, tailYaw, bodyPitch, bodyRoll, bodyDrop, lunge, glow;
        public bool breathing;

        public static DragonPose Lerp(DragonPose a, DragonPose b, float t)
        {
            return new DragonPose
            {
                neckPitch = Mathf.Lerp(a.neckPitch, b.neckPitch, t), neckYaw = Mathf.Lerp(a.neckYaw, b.neckYaw, t),
                headPitch = Mathf.Lerp(a.headPitch, b.headPitch, t), tailYaw = Mathf.Lerp(a.tailYaw, b.tailYaw, t),
                bodyPitch = Mathf.Lerp(a.bodyPitch, b.bodyPitch, t), bodyRoll = Mathf.Lerp(a.bodyRoll, b.bodyRoll, t),
                bodyDrop = Mathf.Lerp(a.bodyDrop, b.bodyDrop, t), lunge = Mathf.Lerp(a.lunge, b.lunge, t),
                glow = Mathf.Lerp(a.glow, b.glow, t), breathing = t < 0.5f ? a.breathing : b.breathing
            };
        }
    }

    public static class DragonMoves
    {
        // ---- timings (seconds) shared by AI and visuals
        public const float BiteDur = 1.55f, BiteHit = 0.78f;
        public const float DoubleBiteDur = 2.5f, DoubleBiteHit2 = 1.7f;
        public const float SweepDur = 1.9f, SweepSpinStart = 0.55f, SweepSpinEnd = 1.15f;
        public const float BreathDur = 4.4f, BreathStart = 1.15f, BreathEnd = 3.8f;
        public const float StompDur = 1.9f, StompHit = 0.92f;
        public const float RoarDur = 2.6f, RoarBlast = 0.75f;
        public const float TakeOffDur = 1.9f;
        public const float FireRainDur = 5.6f;
        public const float LandDur = 1.6f, LandHit = 1.0f;
        public const float StaggerDur = 3.2f;
        public const float AwakenDur = 2.8f;
        public const float ChargeLock = 0.7f, ChargeRun = 1.35f, ChargeRunEnd = 2.75f, ChargeDur = 3.5f;
        public const float PounceLock = 1.0f, PounceLand = 1.85f, PounceDur = 3.2f;

        public static float Duration(DragonMove m)
        {
            switch (m)
            {
                case DragonMove.Bite: return BiteDur;
                case DragonMove.DoubleBite: return DoubleBiteDur;
                case DragonMove.TailSweep: return SweepDur;
                case DragonMove.Breath: return BreathDur;
                case DragonMove.Stomp: return StompDur;
                case DragonMove.Charge: return ChargeDur;
                case DragonMove.Pounce: return PounceDur;
                case DragonMove.Roar: return RoarDur;
                case DragonMove.TakeOff: return TakeOffDur;
                case DragonMove.FireRain: return FireRainDur;
                case DragonMove.Land: return LandDur;
                case DragonMove.Stagger: return StaggerDur;
                case DragonMove.Awaken: return AwakenDur;
                case DragonMove.Death: return 999f;
                default: return 0f;
            }
        }

        static float S(float x) => x * x * (3f - 2f * x);
        static float Seg(float t, float a, float b) => S(Mathf.Clamp01((t - a) / Mathf.Max(0.0001f, b - a)));

        static DragonPose P(float neckPitch = 0, float headPitch = 0, float tailYaw = 0, float bodyPitch = 0, float lunge = 0, float glow = 0, float neckYaw = 0)
            => new DragonPose { neckPitch = neckPitch, headPitch = headPitch, tailYaw = tailYaw, bodyPitch = bodyPitch, lunge = lunge, glow = glow, neckYaw = neckYaw };

        /// <summary>Pose of a move at time t. p1/p2 = move parameters (breath sweep yaws…).</summary>
        public static DragonPose Evaluate(DragonMove m, float t, float p1, float p2)
        {
            var rest = new DragonPose();
            switch (m)
            {
                case DragonMove.Bite:
                {
                    var wind = P(-28, -18, 12, -4);
                    var strike = P(38, 22, -8, 3, 1.4f);
                    if (t < 0.65f) return DragonPose.Lerp(rest, wind, Seg(t, 0f, 0.65f));
                    if (t < BiteHit + 0.06f) return DragonPose.Lerp(wind, strike, Seg(t, 0.65f, BiteHit));
                    return DragonPose.Lerp(strike, rest, Seg(t, BiteHit + 0.15f, BiteDur));
                }
                case DragonMove.DoubleBite:
                {
                    var wind = P(-28, -18, 12, -4);
                    var strike = P(38, 22, -8, 3, 1.4f);
                    var wind2 = P(-20, -12, -12, -2, 0.4f, 0, p1 * 0.4f);
                    var strike2 = P(42, 25, 10, 4, 2f, 0, p1 * 0.6f);
                    if (t < 0.6f) return DragonPose.Lerp(rest, wind, Seg(t, 0f, 0.6f));
                    if (t < 0.84f) return DragonPose.Lerp(wind, strike, Seg(t, 0.6f, 0.78f));
                    if (t < 1.45f) return DragonPose.Lerp(strike, wind2, Seg(t, 0.95f, 1.45f));
                    if (t < DoubleBiteHit2 + 0.06f) return DragonPose.Lerp(wind2, strike2, Seg(t, 1.45f, DoubleBiteHit2));
                    return DragonPose.Lerp(strike2, rest, Seg(t, DoubleBiteHit2 + 0.15f, DoubleBiteDur));
                }
                case DragonMove.TailSweep:
                {
                    var coil = P(-8, 0, 45, 0, 0); coil.bodyDrop = -0.25f;
                    var whip = P(10, 5, -55, 2, 0);
                    if (t < SweepSpinStart) return DragonPose.Lerp(rest, coil, Seg(t, 0f, SweepSpinStart));
                    if (t < SweepSpinEnd) return DragonPose.Lerp(coil, whip, Seg(t, SweepSpinStart, SweepSpinEnd));
                    return DragonPose.Lerp(whip, rest, Seg(t, SweepSpinEnd, SweepDur));
                }
                case DragonMove.Breath:
                {
                    var rear = P(-38, -22, 15, -10, 0, 1f, p1 * 0.3f);
                    var fireA = P(14, 8, -10, 2, 0.3f, 1f, p1); fireA.breathing = true;
                    var fireB = fireA; fireB.neckYaw = p2;
                    if (t < BreathStart) return DragonPose.Lerp(rest, rear, Seg(t, 0f, BreathStart - 0.15f));
                    if (t < BreathStart + 0.2f) return DragonPose.Lerp(rear, fireA, Seg(t, BreathStart - 0.15f, BreathStart + 0.2f));
                    if (t < BreathEnd) { var pz = DragonPose.Lerp(fireA, fireB, Mathf.Clamp01((t - BreathStart - 0.2f) / (BreathEnd - BreathStart - 0.2f))); pz.breathing = true; return pz; }
                    var end = DragonPose.Lerp(fireB, rest, Seg(t, BreathEnd, BreathDur)); end.glow *= 0.5f; return end;
                }
                case DragonMove.Stomp:
                {
                    var rearUp = P(-30, -15, 20, -26); rearUp.bodyDrop = 0.2f;
                    var slam = P(18, 10, -10, 5); slam.bodyDrop = -0.3f;
                    if (t < 0.8f) return DragonPose.Lerp(rest, rearUp, Seg(t, 0f, 0.8f));
                    if (t < StompHit + 0.05f) return DragonPose.Lerp(rearUp, slam, Seg(t, 0.8f, StompHit));
                    return DragonPose.Lerp(slam, rest, Seg(t, StompHit + 0.2f, StompDur));
                }
                case DragonMove.Pounce:
                {
                    // Elden-Ring style leap: sinks low on its haunches, chest glowing, wings half open → springs
                    // up in an arc (nose up, then down) → crashes down on the marked spot → rises out of the dust
                    var coil = P(28, 20, 18, 8, -0.6f, 0.6f); coil.bodyDrop = -0.75f;
                    var air = P(-20, -12, -10, -16, 0.6f, 0.4f);
                    var dive = P(30, 18, 10, 18, 0.8f, 0.9f);
                    var crash = P(40, 26, -20, 10, 0.9f, 0.8f); crash.bodyDrop = -0.55f;
                    if (t < PounceLock) { var c = DragonPose.Lerp(rest, coil, Seg(t, 0f, PounceLock * 0.8f)); c.bodyRoll = Mathf.Sin(t * 30f) * 1.5f * Seg(t, 0.4f, PounceLock); return c; }
                    if (t < PounceLand)
                    {
                        float u = (t - PounceLock) / (PounceLand - PounceLock);
                        var a = DragonPose.Lerp(air, dive, Seg(u, 0.35f, 1f));
                        a.bodyDrop = Mathf.Sin(u * Mathf.PI) * 5.2f - 0.2f * u;
                        return a;
                    }
                    if (t < PounceLand + 0.5f) return DragonPose.Lerp(dive, crash, Seg(t, PounceLand, PounceLand + 0.12f));
                    return DragonPose.Lerp(crash, rest, Seg(t, PounceLand + 0.5f, PounceDur));
                }
                case DragonMove.Charge:
                {
                    // crouch and snarl, head low and tail up (the lane appears) → run → skid, rearing back
                    var crouch = P(34, 18, 0, 6, -0.4f, 0.35f); crouch.bodyDrop = -0.45f;
                    var paw = crouch; paw.bodyRoll = 4f; paw.headPitch = 26f;
                    var run = P(24, 10, 0, 4, 0.5f);
                    var skid = P(-18, -10, 30, -12, 0.2f); skid.bodyDrop = -0.15f;
                    if (t < ChargeLock) return DragonPose.Lerp(rest, crouch, Seg(t, 0f, ChargeLock * 0.7f));
                    if (t < ChargeRun)
                    {
                        float k = Mathf.Sin((t - ChargeLock) * 22f) * 0.5f + 0.5f;    // pawing / trembling with rage
                        var pz = DragonPose.Lerp(crouch, paw, k); pz.neckYaw = Mathf.Sin(t * 18f) * 3f; return pz;
                    }
                    if (t < ChargeRunEnd)
                    {
                        var pz = DragonPose.Lerp(crouch, run, Seg(t, ChargeRun, ChargeRun + 0.2f));
                        float gait = Mathf.Sin(t * 13f);
                        pz.bodyPitch += gait * 3f; pz.neckPitch += gait * 6f; pz.tailYaw = Mathf.Sin(t * 6.5f) * 18f; pz.bodyRoll = gait * 2.5f;
                        return pz;
                    }
                    if (t < ChargeRunEnd + 0.45f) return DragonPose.Lerp(run, skid, Seg(t, ChargeRunEnd, ChargeRunEnd + 0.3f));
                    return DragonPose.Lerp(skid, rest, Seg(t, ChargeRunEnd + 0.45f, ChargeDur));
                }
                case DragonMove.Roar:
                case DragonMove.Awaken:
                {
                    var up = P(-45, -30, 25, -12, 0, 0.6f);
                    float shake = t > RoarBlast && t < RoarDur - 0.6f ? Mathf.Sin(t * 40f) * 4f : 0f;
                    var pz = t < RoarBlast ? DragonPose.Lerp(rest, up, Seg(t, 0f, RoarBlast)) : DragonPose.Lerp(up, rest, Seg(t, RoarDur - 0.6f, RoarDur));
                    pz.neckYaw += shake; pz.headPitch += shake;
                    return pz;
                }
                case DragonMove.TakeOff:
                    return DragonPose.Lerp(rest, P(-15, -10, 0, -8), Seg(t, 0f, 0.8f));
                case DragonMove.FireRain:
                {
                    var pz = P(-10, 12, 0, -8, 0, 0.7f);
                    pz.headPitch += Mathf.Sin(t * 3.5f) * 8f;   // head pumps as it spits fire
                    return pz;
                }
                case DragonMove.Land:
                {
                    var a = P(-15, -10, 0, -8);
                    var b = P(20, 10, -10, 4); b.bodyDrop = -0.3f;
                    if (t < LandHit) return DragonPose.Lerp(a, b, Seg(t, 0.4f, LandHit));
                    return DragonPose.Lerp(b, rest, Seg(t, LandHit + 0.1f, LandDur));
                }
                case DragonMove.Stagger:
                {
                    var down = P(55, 25, 0, 6); down.bodyDrop = -0.6f;
                    if (t < 0.35f) return DragonPose.Lerp(rest, down, Seg(t, 0f, 0.35f));
                    return DragonPose.Lerp(down, rest, Seg(t, StaggerDur - 0.7f, StaggerDur));
                }
                case DragonMove.Death:
                {
                    var dead = P(65, 30, 20, 6); dead.bodyRoll = 72f; dead.bodyDrop = -1.1f;
                    return DragonPose.Lerp(rest, dead, Seg(t, 0f, 1.8f));
                }
            }
            return rest;
        }

        /// <summary>Main clip to play during a move (Locomotion = driven by speed).</summary>
        public static string Clip(DragonMove m, float t)
        {
            switch (m)
            {
                case DragonMove.TakeOff: return t < 0.4f ? DragonVisuals.Locomotion : DragonVisuals.Fly;
                case DragonMove.FireRain: return DragonVisuals.Hover;
                case DragonMove.Land: return t < LandHit ? DragonVisuals.Hover : DragonVisuals.Locomotion;
                case DragonMove.Charge: return DragonVisuals.Locomotion;
                case DragonMove.Pounce: return t > PounceLock - 0.1f && t < PounceLand ? DragonVisuals.Hover : DragonVisuals.Locomotion;
                default: return DragonVisuals.Locomotion;
            }
        }
    }
}
