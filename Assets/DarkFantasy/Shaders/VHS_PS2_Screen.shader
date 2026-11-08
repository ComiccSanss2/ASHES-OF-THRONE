// Shader a schermo intero "VHS / PS2".
// Viene applicato alla RawImage che mostra la RenderTexture a bassa risoluzione della camera
// (vedi ScreenPresenter.cs). Effetti:
//  - wobble del nastro + banda di tracking che scorre
//  - chromatic aberration orizzontale e "chroma bleed" (crominanza sfocata come su VHS)
//  - riduzione della profondità colore (stile PS2 15/16-bit) con dither ordinato 4x4 (dither grid)
//  - scanlines regolabili, rumore/grana del nastro, vignettatura CRT, leggera curvatura
//  - color grading cupo (desaturazione + tinta ombre/luci verso grigio/marrone/verde palude)
Shader "DarkFantasy/VHS_PS2_Screen"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Tape)]
        _TapeWobble ("Tape Wobble", Range(0,3)) = 1
        _TrackingBand ("Tracking Band", Range(0,3)) = 1
        _NoiseIntensity ("Noise", Range(0,0.3)) = 0.06

        [Header(Color)]
        _ChromaticAberration ("Chromatic Aberration", Range(0,4)) = 1.2
        _ChromaBleed ("Chroma Bleed", Range(0,1)) = 0.6
        _ColorLevels ("Color Levels per channel", Range(4,256)) = 32
        _DitherStrength ("Dither Strength", Range(0,2)) = 1
        _Desaturation ("Desaturation", Range(0,1)) = 0.25
        _ShadowTint ("Shadow Tint", Color) = (0.38,0.42,0.33,1)
        _HighlightTint ("Highlight Tint", Color) = (0.62,0.56,0.47,1)
        _TintAmount ("Tint Amount", Range(0,1)) = 0.22
        _Contrast ("Contrast", Range(0.5,1.5)) = 1.05
        _ShadowGamma ("Shadow Gamma (lifts the darks the CRT effects eat)", Range(0.6,1.2)) = 0.86

        [Header(CRT)]
        _ScanlineIntensity ("Scanline Intensity", Range(0,1)) = 0.35
        _ScanlineCount ("Scanline Count", Float) = 240
        _Vignette ("Vignette", Range(0,1.5)) = 0.35
        _Curvature ("Curvature", Range(0,0.2)) = 0.03
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="False" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One Zero

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            float _TapeWobble, _TrackingBand, _NoiseIntensity;
            float _ChromaticAberration, _ChromaBleed, _ColorLevels, _DitherStrength, _Desaturation;
            float4 _ShadowTint, _HighlightTint;
            float _TintAmount, _Contrast, _ShadowGamma;
            float _ScanlineIntensity, _ScanlineCount, _Vignette, _Curvature;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            float hash11(float p) { p = frac(p * 0.1031); p *= p + 33.33; p *= p + p; return frac(p); }
            float hash21(float2 p) { float3 p3 = frac(float3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }

            // Matrice di Bayer 4x4 (dither ordinato tipico delle console a 16 bit/PS1/PS2)
            float bayer4(float2 pixel)
            {
                int x = (int)fmod(pixel.x, 4.0);
                int y = (int)fmod(pixel.y, 4.0);
                int idx = y * 4 + x;
                float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
                return m[idx] / 16.0;
            }

            float3 rgb2yiq(float3 c) { return float3(dot(c, float3(0.299, 0.587, 0.114)), dot(c, float3(0.596, -0.274, -0.322)), dot(c, float3(0.211, -0.523, 0.312))); }
            float3 yiq2rgb(float3 c) { return float3(c.x + 0.956 * c.y + 0.621 * c.z, c.x - 0.272 * c.y - 0.647 * c.z, c.x - 1.106 * c.y + 1.703 * c.z); }

            float3 sampleGamma(float2 uv)
            {
                float3 c = tex2D(_MainTex, uv).rgb;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                c = LinearToGammaSpace(max(c, 0));
                #endif
                return c;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float t = _Time.y;
                float2 uv = i.uv;

                // --- leggera curvatura CRT
                float2 cc = uv - 0.5;
                uv = 0.5 + cc * (1.0 + _Curvature * dot(cc, cc) * 4.0);
                float outside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);

                // --- wobble orizzontale del nastro + banda di tracking che scende lentamente
                float scanRow = floor(uv.y * _ScanlineCount);
                float wobble = (sin(uv.y * 38.0 + t * 2.1) * 0.0006 + sin(uv.y * 7.0 - t * 1.3) * 0.0004) * _TapeWobble;
                float bandPos = 1.0 - frac(t * 0.045);
                float band = saturate(1.0 - abs(uv.y - bandPos) / 0.035);
                wobble += band * (hash11(scanRow + floor(t * 24.0)) - 0.5) * 0.012 * _TrackingBand;
                uv.x += wobble;

                // --- campionamento con aberrazione cromatica + chroma bleed (I/Q sfocati in orizzontale)
                float px = _MainTex_TexelSize.x;
                float ca = _ChromaticAberration * px;
                float3 col;
                col.r = sampleGamma(uv + float2(ca, 0)).r;
                col.g = sampleGamma(uv).g;
                col.b = sampleGamma(uv - float2(ca, 0)).b;

                float3 yiq = rgb2yiq(col);
                float3 bl = 0;
                bl += rgb2yiq(sampleGamma(uv + float2(-3.0 * px, 0)));
                bl += rgb2yiq(sampleGamma(uv + float2(-1.5 * px, 0)));
                bl += rgb2yiq(sampleGamma(uv + float2( 1.5 * px, 0)));
                bl += rgb2yiq(sampleGamma(uv + float2( 3.0 * px, 0)));
                bl *= 0.25;
                yiq.yz = lerp(yiq.yz, bl.yz, _ChromaBleed);
                // rumore extra nella banda di tracking
                yiq.x += band * (hash21(float2(uv.x * 300.0, scanRow + t)) - 0.5) * 0.25 * _TrackingBand;
                col = yiq2rgb(yiq);

                // --- color grading cupo
                float lum = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(col, lum.xxx, _Desaturation);
                // tint without darkening: the tint colour is normalised to luminance 1
                float3 tint = lerp(_ShadowTint.rgb, _HighlightTint.rgb, saturate(lum * 1.4));
                tint /= max(dot(tint, float3(0.299, 0.587, 0.114)), 0.05);
                col = lerp(col, col * tint, _TintAmount);
                // contrast pivots low (keeps the darks readable), then a shadow gamma that pays back
                // what the scanlines, vignette and colour quantisation take away
                col = max((col - 0.18) * _Contrast + 0.18, 0);
                col = pow(col, _ShadowGamma);

                // --- rumore del nastro (grana)
                float n = hash21(floor(i.uv * _MainTex_TexelSize.zw) + frac(t * 13.17) * 117.0) - 0.5;
                col += n * _NoiseIntensity;

                // --- riduzione profondità colore con dither grid (in spazio pixel della RT)
                float2 pixel = floor(uv * _MainTex_TexelSize.zw);
                float d = (bayer4(pixel) - 0.5) * _DitherStrength;
                float levels = max(_ColorLevels - 1.0, 1.0);
                col = floor(saturate(col) * levels + d + 0.5) / levels;

                // --- scanlines (seguono le righe della risoluzione interna)
                float s = sin(uv.y * _ScanlineCount * 3.14159265 * 2.0) * 0.5 + 0.5;
                col *= lerp(1.0, 0.55 + 0.45 * s, _ScanlineIntensity) / (1.0 - 0.225 * _ScanlineIntensity);   // same mean brightness

                // --- vignettatura
                float v = saturate(1.0 - dot(cc, cc) * _Vignette * 1.6);
                col *= v;
                col *= outside;

                col = saturate(col);
                #if !defined(UNITY_COLORSPACE_GAMMA)
                col = GammaToLinearSpace(col);
                #endif
                return fixed4(col * i.color.rgb, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
