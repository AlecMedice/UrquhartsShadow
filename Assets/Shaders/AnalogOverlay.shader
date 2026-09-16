// Full-screen UI overlay for the analog-horror feel while filming: film grain, scanlines, vignette,
// a slight chroma shift and a rolling tracking bar. Put it on a stretched UI Image (white sprite) above the
// HUD; HudUI enables the Image while a phone, the 35mm or the ROV feed is active.
Shader "UrquhartsShadow/AnalogOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Grain ("Grain Strength", Range(0, 1)) = 0.25
        _Scanline ("Scanline Strength", Range(0, 1)) = 0.35
        _ScanlineCount ("Scanline Count", Float) = 480
        _Vignette ("Vignette", Range(0, 2)) = 0.9
        _Tracking ("Tracking Bar Strength", Range(0, 1)) = 0.3
        _Tint ("Colour Cast", Color) = (0.6, 0.75, 0.7, 0.12)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _Color, _Tint;
                float _Grain, _Scanline, _ScanlineCount, _Vignette, _Tracking;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            float hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv; OUT.color = IN.color * _Color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float t = _Time.y;
                float2 uv = IN.uv;

                // Grain: per-pixel noise that changes every frame.
                float g = hash(uv * 1024.0 + frac(t * 60.0)) - 0.5;
                float grain = g * _Grain;

                // Scanlines.
                float scan = (sin(uv.y * _ScanlineCount * 3.14159) * 0.5 + 0.5) * _Scanline * 0.5;

                // Vignette: darkens the corners.
                float2 c = uv - 0.5;
                float vig = saturate(dot(c, c) * _Vignette * 2.0);

                // Rolling tracking bar every few seconds.
                float bar = frac(t * 0.08);
                float tracking = smoothstep(0.02, 0.0, abs(uv.y - bar)) * _Tracking * (hash(float2(floor(t * 8.0), 0)) > 0.6 ? 1.0 : 0.0);

                // Compose: darkening in alpha, a faint colour cast, brighten from grain/tracking.
                float dark = saturate(scan + vig);
                float3 col = _Tint.rgb + grain + tracking * 0.6;
                float alpha = saturate(dark * 0.9 + _Tint.a + abs(grain) * 0.8 + tracking * 0.4);
                return half4(col, alpha) * IN.color;
            }
            ENDHLSL
        }
    }
    FallBack "UI/Default"
}
