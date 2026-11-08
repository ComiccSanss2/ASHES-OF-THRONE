// Sunset sky for the terrace: an inside-out dome that follows the camera.
// Unlit texture (equirectangular: u = azimuth, v = elevation), no fog, drawn first.
Shader "DarkFantasy/SkyDome"
{
    Properties
    {
        _MainTex ("Sky", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _Alpha ("Alpha", Range(0,1)) = 1
        _Saturation ("Saturation (pays back the global grade)", Range(0,3)) = 1.6
    }
    SubShader
    {
        Tags { "Queue"="Background+10" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Front
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Tint;
                float _Alpha;
                float _Saturation;
            CBUFFER_END
            struct A { float4 pos : POSITION; float3 normal : NORMAL; };
            struct V { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            V vert(A a)
            {
                V o;
                o.pos = TransformObjectToHClip(a.pos.xyz);
                o.dir = normalize(a.pos.xyz);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float u = atan2(d.x, d.z) / (2.0 * PI) + 0.5;
                float v = saturate(asin(clamp(d.y, -1, 1)) / (0.5 * PI) * 0.5 + 0.5);
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(u, v));
                half l = dot(c.rgb, half3(0.299, 0.587, 0.114));
                c.rgb = max(lerp(l.xxx, c.rgb, _Saturation), 0) * _Tint.rgb;
                c.a = _Alpha;
                return c;
            }
            ENDHLSL
        }
    }
}
