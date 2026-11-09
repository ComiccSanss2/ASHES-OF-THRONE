using System.Collections.Generic;
using System.Linq;
using DarkFantasy.Procedural;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DarkFantasy.EditorTools
{
    /// <summary>
    /// The radiant arms (the Mirror's reward), redesigned from scratch so they no longer read as a
    /// recoloured relic: moon-silver, living gold, a core of glowing glass and floating halos.
    ///   Dawnbreaker — a winged greatsword with a glass heart running up the blade and two halos around it;
    ///   Starwoven   — a bow of silver ribs, gold feathers, star crystals on the tips and a string of light;
    ///   Dawnsickle  — a crescent of glowing glass on a silver spine, gilded wings and a crown halo;
    ///   the bard's staff keeps its body in silver and gold but wears a crown of light (halo, star, wings).
    /// </summary>
    public static partial class DarkFantasyBuilder
    {
        /// <summary>Diamond-section blade along +Y (edges ±Z, flats ±X).</summary>
        static void DiamondBlade(MeshBuilder mb, float y0, float y1, float w0, float w1, float t0, float t1, float tipLen)
        {
            Vector3 b = new Vector3(0, y0, 0), e = new Vector3(0, y1, 0), tip = e + Vector3.up * tipLen;
            Vector3[] r0 = { b + Vector3.forward * w0, b + Vector3.right * t0, b - Vector3.forward * w0, b - Vector3.right * t0 };
            Vector3[] r1 = { e + Vector3.forward * w1, e + Vector3.right * t1, e - Vector3.forward * w1, e - Vector3.right * t1 };
            for (int i = 0; i < 4; i++)
            {
                int n = (i + 1) % 4;
                Vector3 mid = (r0[i] + r0[n]) * 0.5f - b;
                mb.AddQuad(r0[i], r0[n], r1[n], r1[i], mid);
                mb.AddTri(r1[i], r1[n], tip, mid + Vector3.up * 0.1f);
                mb.AddTri(r0[n], r0[i], b + Vector3.down * 0.001f, Vector3.down);
            }
        }

        /// <summary>A fan of feathers (tapered spikes) from 'root', spreading from dir0 to dir1.</summary>
        static void Wing(MeshBuilder mb, Vector3 root, Vector3 dir0, Vector3 dir1, int count, float len0, float len1, float r)
        {
            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0f : i / (float)(count - 1);
                Vector3 d = Vector3.Slerp(dir0.normalized, dir1.normalized, t);
                float len = Mathf.Lerp(len0, len1, t);
                mb.AddPrism(root + d * 0.01f, root + d * len, r, 0.002f, 4, true, false, 700 + i, 0f);
            }
        }

        /// <summary>A lens/gem: a squashed sphere.</summary>
        static void Gem(MeshBuilder mb, Vector3 c, Vector3 r) => Dome(mb, c, r, -90f, 90f, 8, 4);

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t) => (1 - t) * (1 - t) * a + 2 * (1 - t) * t * b + t * t * c;

        static void BuildRadiantMeshes()
        {
            // ------------------------------------------------ Dawnbreaker (grip origin, blade +Y, edges ±Z)
            {
                var silver = new MeshBuilder { uvScale = 2f }; var gold = new MeshBuilder { uvScale = 3f };
                var glass = new MeshBuilder(); var halo = new MeshBuilder();
                DiamondBlade(silver, 0.2f, 1.48f, 0.085f, 0.055f, 0.017f, 0.013f, 0.22f);
                // flared ricasso "wings" of the blade root
                foreach (int s in new[] { -1, 1 })
                    gold.AddPrism(new Vector3(0, 0.19f, s * 0.07f), new Vector3(0, 0.36f, s * 0.11f), 0.018f, 0.003f, 4, true, false, 701, 0f);
                // the glowing heart up the middle of the blade (proud of the flats)
                DiamondBlade(glass, 0.3f, 1.42f, 0.024f, 0.012f, 0.021f, 0.016f, 0.12f);
                // crossguard: two gilded wings swept up toward the blade
                foreach (int s in new[] { -1, 1 })
                {
                    Wing(gold, new Vector3(0, 0.15f, s * 0.05f), new Vector3(0, -0.15f, s), new Vector3(0, 0.75f, s * 0.6f), 6, 0.30f, 0.17f, 0.024f);
                    Wing(silver, new Vector3(0, 0.13f, s * 0.04f), new Vector3(0, -0.45f, s), new Vector3(0, 0.1f, s), 4, 0.2f, 0.24f, 0.016f);
                }
                Dome(gold, new Vector3(0, 0.15f, 0), new Vector3(0.035f, 0.075f, 0.075f), -90f, 90f, 12, 5);   // sun disc
                Gem(glass, new Vector3(0, 0.15f, 0), new Vector3(0.045f, 0.04f, 0.04f));                     // its jewel
                // grip and pommel
                gold.AddPrism(new Vector3(0, -0.3f, 0), new Vector3(0, 0.1f, 0), 0.025f, 0.027f, 8, false, false, 702, 0f);
                for (int i = 0; i < 5; i++) silver.AddPrism(new Vector3(0, -0.27f + i * 0.08f, 0), new Vector3(0, -0.245f + i * 0.08f, 0), 0.03f, 0.03f, 8, true, true, 703 + i, 0f);
                gold.AddPrism(new Vector3(0, -0.36f, 0), new Vector3(0, -0.3f, 0), 0.05f, 0.03f, 8, true, false, 709, 0f);
                Horn(glass, new Vector3(0, -0.36f, 0), Vector3.down, 0.12f, 0.035f, 0f, Vector3.right, 2);
                // two floating halos around the blade
                Ring(halo, new Vector3(0, 0.5f, 0), 0.15f, 0.014f, 0.01f, 28);
                Ring(halo, new Vector3(0, 1.15f, 0), 0.1f, 0.01f, 0.008f, 24);
                CombineParts("Dawnbreaker", new List<(string, MeshBuilder)> { ("RadiantSilver", silver), ("RadiantGold", gold), ("ArmorGlass", glass), ("ArmorHalo", halo) });
            }

            // ------------------------------------------------ Starwoven (grip origin, up +Y, tips at (0, ±tipUp, tipFwd))
            if (RelicData.TryGetValue("Thornwraith", out var bowData))
            {
                float up = bowData.tipUp, fw = bowData.tipFwd;
                float k = up / 0.75f;   // the design is drawn for a 1.5 m bow
                var silver = new MeshBuilder { uvScale = 2f }; var gold = new MeshBuilder { uvScale = 3f };
                var glass = new MeshBuilder(); var halo = new MeshBuilder();
                Vector3 back = new Vector3(0, 0, -Mathf.Sign(fw == 0f ? 1f : fw));   // the side away from the tips
                foreach (int s in new[] { -1, 1 })
                {
                    Vector3 a = new Vector3(0, s * 0.09f * k, 0), c = new Vector3(0, s * up, fw), b = new Vector3(0, s * up * 0.55f, fw * 0.15f) - back * 0.04f * k;
                    int n = 14;
                    for (int i = 0; i < n; i++)
                    {
                        float t0 = i / (float)n, t1 = (i + 1) / (float)n;
                        Vector3 p0 = Bezier(a, b, c, t0), p1 = Bezier(a, b, c, t1);
                        silver.AddPrism(p0, p1, Mathf.Lerp(0.03f, 0.013f, t0) * k, Mathf.Lerp(0.03f, 0.013f, t1) * k, 6, i == 0, false, 720 + i, 0f);
                        // gilded feathers along the back of the limb, longest at mid-limb
                        if (i % 2 == 1 && i < n - 1)
                        {
                            Vector3 tan = (p1 - p0).normalized;
                            Vector3 outw = (Vector3.Cross(tan, Vector3.right) * -s).normalized;
                            if (Vector3.Dot(outw, back) < 0) outw = -outw;
                            float len = (0.05f + 0.07f * Mathf.Sin(t0 * Mathf.PI)) * k;
                            gold.AddPrism(p0, p0 + (outw + tan * s * 0.8f).normalized * len, 0.012f * k, 0.002f, 4, true, false, 740 + i, 0f);
                        }
                    }
                    // recurve flick and star crystal on the tip
                    Vector3 tipDir = (c - Bezier(a, b, c, 0.93f)).normalized;
                    Horn(silver, c, tipDir + new Vector3(0, 0, fw) * 0.3f, 0.07f * k, 0.013f * k, -30f * s, Vector3.right, 3);
                    Vector3 star = c + tipDir * 0.06f * k;
                    Gem(glass, star, new Vector3(0.03f, 0.045f, 0.03f) * k);
                    for (int j = 0; j < 4; j++)
                    {
                        float ang = j * 90f + 45f;
                        Vector3 d = Quaternion.AngleAxis(ang, Vector3.right) * Vector3.up;
                        glass.AddPrism(star, star + d * 0.06f * k, 0.01f * k, 0.001f, 4, false, false, 760 + j, 0f);
                    }
                    // the string of light (tip to the nock point at mid-height)
                    halo.AddPrism(c, new Vector3(0, 0, fw), 0.004f * k, 0.004f * k, 4, false, false, 770, 0f);
                }
                // grip: gold wrap, a jewel facing the target, a halo around it
                gold.AddPrism(new Vector3(0, -0.11f * k, 0), new Vector3(0, 0.11f * k, 0), 0.032f * k, 0.032f * k, 8, true, true, 780, 0f);
                Gem(glass, back * -0.035f * k, new Vector3(0.022f, 0.035f, 0.016f) * k);
                Ring(halo, Vector3.zero, 0.11f * k, 0.01f * k, 0.008f * k, 24);
                CombineParts("Starwoven", new List<(string, MeshBuilder)> { ("RadiantSilver", silver), ("RadiantGold", gold), ("ArmorGlass", glass), ("ArmorHalo", halo) });
                RelicData["Starwoven"] = bowData;
            }

            // ------------------------------------------------ the bard's crown of light (Y = staff axis, origin = staff tip)
            {
                var gold = new MeshBuilder { uvScale = 3f }; var glass = new MeshBuilder(); var halo = new MeshBuilder();
                Horn(glass, Vector3.zero, Vector3.up, 0.18f, 0.045f, 0f, Vector3.right, 2);
                for (int j = 0; j < 6; j++)
                {
                    Vector3 d = Quaternion.AngleAxis(j * 60f, Vector3.up) * new Vector3(0, 0.45f, 1f);
                    glass.AddPrism(new Vector3(0, 0.05f, 0), new Vector3(0, 0.05f, 0) + d.normalized * 0.1f, 0.016f, 0.002f, 4, false, false, 790 + j, 0f);
                }
                foreach (int s in new[] { -1, 1 })
                    Wing(gold, new Vector3(s * 0.03f, -0.02f, 0), new Vector3(s, -0.3f, 0), new Vector3(s * 0.4f, 1f, 0), 5, 0.2f, 0.13f, 0.02f);
                Ring(halo, new Vector3(0, 0.07f, 0), 0.16f, 0.014f, 0.01f, 28);
                CombineParts("BardRadiantCrown", new List<(string, MeshBuilder)> { ("RadiantGold", gold), ("ArmorGlass", glass), ("ArmorHalo", halo) });
            }
        }

        /// <summary>Dawnsickle: snath +Y (0..L), blade toward +Z at the head, edge facing down.</summary>
        static void BuildRadiantScythe()
        {
            float L = SnathLength;
            var gold = new MeshBuilder { uvScale = 2f }; var silver = new MeshBuilder { uvScale = 2f };
            var glass = new MeshBuilder(); var halo = new MeshBuilder();
            gold.AddPrism(Vector3.zero, new Vector3(0f, L, 0f), 0.027f, 0.024f, 8, true, true, 801, 0f);
            // a silver ribbon spiralling up the snath
            int turns = 60;
            for (int i = 0; i < turns; i++)
            {
                float a0 = i * 0.42f, a1 = (i + 1) * 0.42f, y0 = 0.08f + i * (L - 0.2f) / turns, y1 = 0.08f + (i + 1) * (L - 0.2f) / turns;
                silver.AddPrism(new Vector3(Mathf.Sin(a0) * 0.028f, y0, Mathf.Cos(a0) * 0.028f), new Vector3(Mathf.Sin(a1) * 0.028f, y1, Mathf.Cos(a1) * 0.028f), 0.007f, 0.007f, 4, false, false, 0, 0f);
            }
            silver.AddPrism(new Vector3(0f, 0.6f, 0f), new Vector3(0f, 0.65f, 0.13f), 0.016f, 0.014f, 6, true, true, 802, 0f);   // nib
            // a crescent of glowing glass with a silver spine and gold collar
            ScytheBlade(glass, 1.06f, 0.21f, 0.2f, 0.44f, 0.009f, hook: 0.14f);
            ScytheBlade(silver, 1.08f, 0.045f, 0.2f, 0.44f, 0.017f, hook: 0.14f);
            gold.AddPrism(new Vector3(0f, L - 0.1f, 0f), new Vector3(0f, L + 0.05f, 0f), 0.045f, 0.04f, 8, true, true, 803, 0f);
            // gilded wings behind the head, and a crown halo
            foreach (int s in new[] { -1, 1 })
                Wing(gold, new Vector3(s * 0.03f, L - 0.02f, -0.03f), new Vector3(s * 0.4f, -0.5f, -1f), new Vector3(s * 0.25f, 1f, -0.5f), 6, 0.28f, 0.18f, 0.02f);
            Ring(halo, new Vector3(0f, L + 0.12f, -0.02f), 0.12f, 0.012f, 0.009f, 26);
            Gem(glass, new Vector3(0f, L + 0.02f, 0.05f), new Vector3(0.035f, 0.035f, 0.025f));
            // the butt: a star crystal and a small counter-crescent
            Horn(glass, new Vector3(0f, 0.01f, 0f), Vector3.down, 0.16f, 0.034f, 0f, Vector3.right, 2);
            var butt = new MeshBuilder();
            ScytheBlade(butt, 0.32f, 0.06f, 0.02f, 0.06f, 0.008f);
            silver.Append(butt, Matrix4x4.TRS(new Vector3(0f, L - 0.02f, 0f), Quaternion.Euler(180f, 0f, 0f), Vector3.one) * Matrix4x4.Translate(new Vector3(0f, -(L - 0.02f), 0f)));
            CombineParts("ScytheRadiant", new List<(string, MeshBuilder)> { ("RadiantGold", gold), ("RadiantSilver", silver), ("ArmorGlass", glass), ("ArmorHalo", halo) });
        }
    }
}
