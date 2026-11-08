using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DarkFantasy.EditorTools
{
    /// <summary>
    /// Generatore di texture "stile PS2": bassa risoluzione (64–256 px), palette desaturata
    /// (grigio, marrone, verde palude), ambient occlusion dipinta nei giunti, tiling senza cuciture.
    /// Tutte le texture sono deterministiche (stesso seed → stessi pixel).
    /// </summary>
    public static class TextureGen
    {
        // ------------------------------------------------------------------ rumore tileable
        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int n = x * 374761393 + y * 668265263 + seed * 2147483647;
                n = (n ^ (n >> 13)) * 1274126177;
                return ((n ^ (n >> 16)) & 0x7fffffff) / 2147483647f;
            }
        }

        static float S(float t) => t * t * (3 - 2 * t);

        /// <summary>Value noise con periodo (per texture che si ripetono senza giunture).</summary>
        public static float TNoise(float x, float y, int period, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = S(x - xi), yf = S(y - yi);
            int x0 = Mod(xi, period), x1 = Mod(xi + 1, period), y0 = Mod(yi, period), y1 = Mod(yi + 1, period);
            float a = Hash(x0, y0, seed), b = Hash(x1, y0, seed), c = Hash(x0, y1, seed), d = Hash(x1, y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, xf), Mathf.Lerp(c, d, xf), yf);
        }

        static int Mod(int a, int m) => ((a % m) + m) % m;

        /// <summary>fBm tileable: u,v in [0,1), frequenza base "freq" celle per lato.</summary>
        public static float TFbm(float u, float v, int freq, int octaves, int seed)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            int f = freq;
            for (int i = 0; i < octaves; i++)
            {
                sum += TNoise(u * f, v * f, f, seed + i * 31) * amp;
                norm += amp; amp *= 0.5f; f *= 2;
            }
            return sum / norm;
        }

        /// <summary>Voronoi tileable: distanza al seme più vicino e al secondo (F1, F2) + id cella.</summary>
        static void Voronoi(float u, float v, int cells, int seed, out float f1, out float f2, out int id)
        {
            float x = u * cells, y = v * cells;
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            f1 = f2 = 9f; id = 0;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    int cx = xi + i, cy = yi + j;
                    int wx = Mod(cx, cells), wy = Mod(cy, cells);
                    float px = cx + 0.15f + Hash(wx, wy, seed) * 0.7f;
                    float py = cy + 0.15f + Hash(wx, wy, seed + 7) * 0.7f;
                    float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                    if (d < f1) { f2 = f1; f1 = d; id = wx + wy * 131; }
                    else if (d < f2) f2 = d;
                }
        }

        static Color Desat(Color c, float amount)
        {
            float l = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            return Color.Lerp(c, new Color(l, l, l, c.a), amount);
        }

        static Color Quantize(Color c, int levels = 24)
        {
            return new Color(Mathf.Round(c.r * levels) / levels, Mathf.Round(c.g * levels) / levels, Mathf.Round(c.b * levels) / levels, c.a);
        }

        // ------------------------------------------------------------------ salvataggio
        public static Texture2D Save(string assetPath, int w, int h, Func<float, float, Color> fn, bool alpha = false, FilterMode filter = FilterMode.Bilinear, bool mips = true, TextureWrapMode wrap = TextureWrapMode.Repeat)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = fn(x / (float)w, y / (float)h);
                    if (!alpha) c.a = 1f;
                    px[y * w + x] = Quantize(c);
                }
            tex.SetPixels(px);
            tex.Apply();
            string full = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            imp.textureType = TextureImporterType.Default;
            imp.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            imp.alphaIsTransparency = alpha;
            imp.filterMode = filter;
            imp.mipmapEnabled = mips;
            imp.wrapMode = wrap;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.maxTextureSize = Mathf.Max(w, h);
            imp.anisoLevel = 0;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        // ------------------------------------------------------------------ texture del livello
        public static Texture2D Ground(string path) => Save(path, 256, 256, (u, v) =>
        {
            float n = TFbm(u, v, 4, 5, 1);
            float patches = TFbm(u, v, 3, 3, 9);
            float grit = TFbm(u, v, 64, 1, 4);
            Color mud = new Color(0.29f, 0.26f, 0.2f);
            Color mud2 = new Color(0.2f, 0.18f, 0.15f);
            Color grass = new Color(0.33f, 0.31f, 0.21f);
            Color moss = new Color(0.2f, 0.23f, 0.16f);
            Color c = Color.Lerp(mud2, mud, n);
            c = Color.Lerp(c, grass, Mathf.SmoothStep(0.45f, 0.65f, patches) * 0.85f);
            c = Color.Lerp(c, moss, Mathf.SmoothStep(0.6f, 0.75f, TFbm(u, v, 6, 3, 21)) * 0.5f);
            // fili d'erba secca: tratti chiari verticali
            float blades = TNoise(u * 128, v * 16, 128, 12);
            if (patches > 0.5f && blades > 0.78f) c = Color.Lerp(c, new Color(0.45f, 0.41f, 0.28f), 0.6f);
            // sassolini
            Voronoi(u, v, 24, 5, out float f1, out _, out int id);
            if (f1 < 0.18f && Hash(id, 0, 3) > 0.72f) c = Color.Lerp(c, new Color(0.36f, 0.35f, 0.33f), 0.7f * (1 - f1 / 0.18f));
            c *= 0.85f + grit * 0.25f;
            return Desat(c, 0.2f);
        });

        public static Texture2D StoneBricks(string path, int seed = 1, float mossAmount = 0.5f) => Save(path, 256, 256, (u, v) =>
        {
            int rows = 8;
            float row = v * rows;
            int ri = Mathf.FloorToInt(row);
            float offset = (ri % 2) * 0.5f + Hash(ri, 0, seed) * 0.2f;
            int cols = 4;
            float col = (u * cols + offset);
            int ci = Mathf.FloorToInt(col);
            int cw = Mod(ci, cols);
            float fx = col - ci, fy = row - ri;
            // giunti irregolari
            float jitter = (TFbm(u, v, 16, 2, seed + 3) - 0.5f) * 0.08f;
            float edge = Mathf.Min(Mathf.Min(fx, 1 - fx) * 2.2f, Mathf.Min(fy, 1 - fy) * 1.1f) + jitter;
            float tone = Hash(cw, ri, seed);
            Color baseC = Color.Lerp(new Color(0.3f, 0.29f, 0.27f), new Color(0.42f, 0.4f, 0.36f), tone);
            baseC = Color.Lerp(baseC, new Color(0.33f, 0.28f, 0.22f), Hash(cw, ri, seed + 9) * 0.35f); // pietre più brune
            float n = TFbm(u, v, 8, 4, seed + 11);
            Color c = baseC * (0.8f + n * 0.35f);
            // smussatura dei bordi (AO dipinta) e malta
            float bevel = Mathf.SmoothStep(0.02f, 0.14f, edge);
            c *= Mathf.Lerp(0.45f, 1f, bevel);
            if (edge < 0.035f) c = new Color(0.1f, 0.095f, 0.09f);
            // crepe
            float crack = Mathf.Abs(TFbm(u, v, 6, 3, seed + 21) - 0.5f);
            if (crack < 0.012f && Hash(cw, ri, seed + 2) > 0.5f) c *= 0.45f;
            // muschio dal basso dei blocchi e macchie d'umidità
            float moss = Mathf.SmoothStep(0.55f, 0.75f, TFbm(u, v, 5, 3, seed + 31)) * mossAmount;
            c = Color.Lerp(c, new Color(0.2f, 0.24f, 0.15f), moss * (1 - fy * 0.6f));
            float damp = Mathf.SmoothStep(0.6f, 0.8f, TFbm(u, v, 3, 3, seed + 41));
            c *= 1f - damp * 0.25f;
            return Desat(c, 0.25f);
        });

        public static Texture2D Flagstones(string path) => Save(path, 256, 256, (u, v) =>
        {
            Voronoi(u, v, 5, 77, out float f1, out float f2, out int id);
            float edge = f2 - f1;
            float tone = Hash(id, 1, 4);
            Color c = Color.Lerp(new Color(0.24f, 0.23f, 0.22f), new Color(0.36f, 0.34f, 0.31f), tone);
            c *= 0.8f + TFbm(u, v, 8, 4, 5) * 0.35f;
            c *= Mathf.Lerp(0.5f, 1f, Mathf.SmoothStep(0.02f, 0.12f, edge));
            if (edge < 0.035f) c = new Color(0.07f, 0.065f, 0.06f);
            float crack = Mathf.Abs(TFbm(u, v, 5, 3, 13) - 0.5f);
            if (crack < 0.01f) c *= 0.5f;
            // sporco/sangue secco
            float stain = Mathf.SmoothStep(0.62f, 0.78f, TFbm(u, v, 3, 3, 99));
            c = Color.Lerp(c, new Color(0.18f, 0.1f, 0.08f), stain * 0.4f);
            return Desat(c, 0.2f);
        });

        public static Texture2D Bark(string path) => Save(path, 128, 128, (u, v) =>
        {
            float streak = TFbm(u * 1f, v * 0.12f, 16, 3, 3);
            float n = TFbm(u, v, 8, 3, 7);
            Color c = Color.Lerp(new Color(0.12f, 0.1f, 0.085f), new Color(0.3f, 0.27f, 0.23f), streak * 0.7f + n * 0.3f);
            if (TNoise(u * 32, v * 4, 32, 5) > 0.8f) c *= 0.55f; // fessure profonde
            return Desat(c, 0.3f);
        });

        public static Texture2D RottenWood(string path) => Save(path, 128, 128, (u, v) =>
        {
            int planks = 4;
            float p = u * planks;
            int pi = Mathf.FloorToInt(p);
            float fx = p - pi;
            float grain = TFbm(u * 0.25f + pi * 0.37f, v, 32, 3, 9 + pi);
            Color c = Color.Lerp(new Color(0.16f, 0.12f, 0.09f), new Color(0.33f, 0.25f, 0.18f), grain);
            c *= 0.75f + Hash(pi, 0, 2) * 0.3f;
            if (fx < 0.05f || fx > 0.95f) c *= 0.35f;
            float rot = Mathf.SmoothStep(0.55f, 0.75f, TFbm(u, v, 4, 3, 17));
            c = Color.Lerp(c, new Color(0.12f, 0.13f, 0.09f), rot * 0.6f);
            // chiodi
            float ny = v * 2 % 1;
            if (Mathf.Abs(fx - 0.5f) < 0.04f && Mathf.Abs(ny - 0.1f) < 0.02f) c = new Color(0.25f, 0.2f, 0.17f);
            return Desat(c, 0.25f);
        });

        public static Texture2D RustMetal(string path) => Save(path, 128, 128, (u, v) =>
        {
            float n = TFbm(u, v, 6, 4, 3);
            float rust = Mathf.SmoothStep(0.45f, 0.7f, TFbm(u, v, 4, 4, 8));
            Color steel = Color.Lerp(new Color(0.25f, 0.25f, 0.26f), new Color(0.45f, 0.44f, 0.43f), n);
            Color rustC = Color.Lerp(new Color(0.28f, 0.15f, 0.08f), new Color(0.42f, 0.25f, 0.13f), TFbm(u, v, 16, 2, 4));
            Color c = Color.Lerp(steel, rustC, rust * 0.8f);
            if (TNoise(u * 64, v * 2, 64, 6) > 0.85f) c *= 1.25f; // graffi
            return Desat(c, 0.35f);
        });

        public static Texture2D KnightSteel(string path) => Save(path, 64, 64, (u, v) =>
        {
            float n = TFbm(u, v, 4, 3, 13);
            Color c = Color.Lerp(new Color(0.38f, 0.38f, 0.39f), new Color(0.6f, 0.59f, 0.57f), n);
            if (v < 0.08f) c *= 0.6f;
            return Desat(c, 0.4f);
        });

        public static Texture2D Banner(string path) => Save(path, 128, 256, (u, v) =>
        {
            float n = TFbm(u, v, 6, 3, 5);
            Color cloth = Color.Lerp(new Color(0.18f, 0.03f, 0.05f), new Color(0.32f, 0.07f, 0.08f), n);
            // emblema: corona stilizzata / teschio a rombo in oro spento
            float cx = u - 0.5f, cy = v - 0.62f;
            bool emblem = Mathf.Abs(cx) + Mathf.Abs(cy) * 0.8f < 0.18f && Mathf.Abs(cx) + Mathf.Abs(cy) * 0.8f > 0.13f;
            emblem |= Mathf.Abs(cx) < 0.02f && Mathf.Abs(cy) < 0.22f;
            if (emblem) cloth = Color.Lerp(cloth, new Color(0.45f, 0.36f, 0.2f), 0.85f);
            // bordo
            if (u < 0.06f || u > 0.94f) cloth *= 0.6f;
            // buchi e logoramento in basso
            float holes = TFbm(u, v, 8, 3, 23);
            float wear = Mathf.Lerp(0.85f, 0.45f, v);
            cloth.a = holes > wear ? 0f : 1f;
            if (v < 0.04f) cloth.a = 0f;
            return Desat(cloth, 0.15f);
        }, alpha: true, wrap: TextureWrapMode.Clamp);

        public static Texture2D StainedGlass(string path) => Save(path, 128, 256, (u, v) =>
        {
            Voronoi(u, v * 2, 6, 31, out float f1, out float f2, out int id);
            float edge = f2 - f1;
            Color[] glass =
            {
                new Color(0.35f, 0.08f, 0.08f), new Color(0.12f, 0.16f, 0.3f), new Color(0.45f, 0.32f, 0.12f),
                new Color(0.12f, 0.24f, 0.2f), new Color(0.3f, 0.26f, 0.22f)
            };
            Color c = glass[Mathf.Abs(id) % glass.Length] * (0.8f + TFbm(u, v, 8, 2, 4) * 0.4f);
            if (edge < 0.06f) c = new Color(0.06f, 0.06f, 0.06f); // piombo
            // vetri rotti: celle mancanti con bordi frastagliati
            bool broken = Hash(id, 3, 9) > 0.62f;
            float jag = TFbm(u, v, 16, 2, 7);
            c.a = broken && edge > 0.06f + jag * 0.05f ? 0f : 1f;
            return Desat(c, 0.2f);
        }, alpha: true, wrap: TextureWrapMode.Clamp);

        public static Texture2D GrassTuft(string path) => Save(path, 64, 64, (u, v) =>
        {
            Color c = new Color(0, 0, 0, 0);
            for (int i = 0; i < 9; i++)
            {
                float bx = 0.1f + Hash(i, 0, 1) * 0.8f;
                float lean = (Hash(i, 0, 2) - 0.5f) * 0.4f;
                float hgt = 0.5f + Hash(i, 0, 3) * 0.5f;
                float x = bx + lean * v;
                float w = 0.035f * (1f - v / hgt);
                if (v < hgt && Mathf.Abs(u - x) < w)
                    c = Color.Lerp(new Color(0.24f, 0.21f, 0.13f, 1), new Color(0.5f, 0.45f, 0.3f, 1), v / hgt);
            }
            return c;
        }, alpha: true, wrap: TextureWrapMode.Clamp);

        public static Texture2D Fog(string path) => Save(path, 128, 128, (u, v) =>
        {
            float n = TFbm(u, v, 4, 5, 3);
            float n2 = TFbm(u, v, 8, 3, 11);
            float a = Mathf.SmoothStep(0.25f, 0.85f, n * 0.7f + n2 * 0.3f);
            return new Color(0.85f, 0.83f, 0.78f, 0.35f + a * 0.6f);
        }, alpha: true);

        public static Texture2D SoftDot(string path) => Save(path, 32, 32, (u, v) =>
        {
            float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            float a = Mathf.Clamp01(1f - d);
            return new Color(1, 1, 1, a * a);
        }, alpha: true, wrap: TextureWrapMode.Clamp);

        public static Texture2D Ash(string path) => Save(path, 128, 128, (u, v) =>
        {
            float n = TFbm(u, v, 6, 4, 2);
            Color c = Color.Lerp(new Color(0.1f, 0.1f, 0.1f), new Color(0.32f, 0.31f, 0.3f), n);
            if (TNoise(u * 64, v * 64, 64, 4) > 0.93f) c = new Color(0.55f, 0.22f, 0.08f);
            return c;
        });

        /// <summary>Slash trail: bright toward the tip edge (v=1), soft toward the hilt, streaky.</summary>
        public static Texture2D Trail(string path) => Save(path, 64, 32, (u, v) =>
        {
            float streak = 0.75f + 0.25f * TNoise(u * 16, v * 2, 16, 5);
            float a = Mathf.SmoothStep(0f, 1f, v) * Mathf.SmoothStep(1f, 0.85f, v) * streak;
            return new Color(1, 1, 1, a);
        }, alpha: true, wrap: TextureWrapMode.Clamp);

        public static Texture2D Ring(string path) => Save(path, 64, 16, (u, v) =>
        {
            float a = Mathf.Sin(v * Mathf.PI) * (0.6f + TNoise(u * 32, v * 4, 32, 2) * 0.4f);
            return new Color(1, 1, 1, a);
        }, alpha: true);
    }
}
