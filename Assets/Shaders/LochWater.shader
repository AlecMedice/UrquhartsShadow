// Loch Ness water for URP. Vertex displacement uses the same four Gerstner waves as
// OceanSurface.cs (which pushes _WaveHeight and _WaveA.._WaveD each frame), so physics and visuals agree.
// Features: Gerstner displacement + analytic normals, depth-tinted colour, fresnel, moon specular,
// foam on crests, soft shoreline via scene depth. Transparent queue.
Shader "UrquhartsShadow/LochWater"
{
    Properties
    {
        _ShallowColor ("Shallow Colour", Color) = (0.05, 0.12, 0.14, 0.85)
        _DeepColor ("Deep Colour", Color) = (0.005, 0.02, 0.035, 0.98)
        _DepthFade ("Depth Fade Distance", Float) = 6.0
        _WaveHeight ("Wave Height", Float) = 0.4
        _WaveA ("Wave A (dir.x, dir.y, steepness, wavelength)", Vector) = (1, 0.3, 0.25, 18)
        _WaveB ("Wave B", Vector) = (-0.4, 1, 0.18, 11)
        _WaveC ("Wave C", Vector) = (0.7, -0.6, 0.12, 6)
        _WaveD ("Wave D", Vector) = (-0.9, -0.2, 0.08, 3)
        _NormalMap ("Ripple Normal", 2D) = "bump" {}
        _RippleScale ("Ripple Scale", Float) = 0.08
        _RippleSpeed ("Ripple Speed", Float) = 0.03
        _RippleStrength ("Ripple Strength", Range(0, 1)) = 0.35
        _Smoothness ("Smoothness", Range(0, 1)) = 0.95
        _SpecularPower ("Moon Specular Power", Float) = 220
        _SpecularStrength ("Moon Specular Strength", Float) = 1.5
        _FresnelPower ("Fresnel Power", Float) = 4
        _FoamColor ("Foam Colour", Color) = (0.55, 0.6, 0.6, 1)
        _FoamThreshold ("Foam Crest Threshold", Range(0, 1)) = 0.75
        _FoamNoise ("Foam Noise", 2D) = "gray" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_FoamNoise); SAMPLER(sampler_FoamNoise);

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor, _DeepColor, _FoamColor;
                float4 _WaveA, _WaveB, _WaveC, _WaveD;
                float4 _NormalMap_ST, _FoamNoise_ST;
                float _DepthFade, _WaveHeight, _RippleScale, _RippleSpeed, _RippleStrength;
                float _Smoothness, _SpecularPower, _SpecularStrength, _FresnelPower, _FoamThreshold;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 screenPos  : TEXCOORD2;
                float  crest      : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
            };

            // Same formulation as OceanSurface.GetHeightAt: k = 2pi/lambda, c = sqrt(g/k), f = k*(dot(d,xz) - c*t), a = steepness/k.
            // Returns displacement and accumulates tangent/binormal for the analytic normal.
            float3 Gerstner(float4 wave, float3 p, inout float3 tangent, inout float3 binormal, inout float crest)
            {
                float steepness = wave.z;
                float wavelength = max(wave.w, 0.01);
                float k = 2.0 * PI / wavelength;
                float c = sqrt(9.81 / k);
                float2 d = normalize(wave.xy);
                float f = k * (dot(d, p.xz) - c * _Time.y);
                float a = steepness / k * _WaveHeight;

                float s = sin(f), co = cos(f);
                tangent += float3(-d.x * d.x * steepness * s * _WaveHeight, d.x * steepness * co * _WaveHeight, -d.x * d.y * steepness * s * _WaveHeight);
                binormal += float3(-d.x * d.y * steepness * s * _WaveHeight, d.y * steepness * co * _WaveHeight, -d.y * d.y * steepness * s * _WaveHeight);
                crest += saturate(s) * steepness;
                return float3(d.x * a * co, a * s, d.y * a * co);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 tangent = float3(1, 0, 0), binormal = float3(0, 0, 1);
                float crest = 0;
                float3 p = posWS;
                posWS += Gerstner(_WaveA, p, tangent, binormal, crest);
                posWS += Gerstner(_WaveB, p, tangent, binormal, crest);
                posWS += Gerstner(_WaveC, p, tangent, binormal, crest);
                posWS += Gerstner(_WaveD, p, tangent, binormal, crest);

                OUT.positionWS = posWS;
                OUT.normalWS = normalize(cross(binormal, tangent));
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);
                OUT.crest = crest / (_WaveA.z + _WaveB.z + _WaveC.z + _WaveD.z + 1e-4);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Scene depth for shoreline / hull fade.
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float surfaceDepth = IN.screenPos.w;
                float depthDiff = saturate((sceneDepth - surfaceDepth) / _DepthFade);

                // Detail ripples layered over the analytic normal.
                float2 uv1 = IN.positionWS.xz * _RippleScale + _Time.y * _RippleSpeed * float2(1, 0.6);
                float2 uv2 = IN.positionWS.xz * _RippleScale * 1.7 - _Time.y * _RippleSpeed * float2(0.7, 1);
                float3 n1 = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv1));
                float3 n2 = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv2));
                float3 ripple = normalize(float3(n1.xy + n2.xy, n1.z * n2.z));
                float3 N = normalize(IN.normalWS + float3(ripple.x, 0, ripple.y) * _RippleStrength);

                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));
                Light moon = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                float3 L = normalize(moon.direction);

                // Base colour by depth, darker at night by ambient.
                float3 baseCol = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthDiff);
                float3 ambient = SampleSH(N);
                float ndl = saturate(dot(N, L));
                float3 diffuse = baseCol * (ambient + moon.color * ndl * moon.shadowAttenuation * 0.4);

                // Moon path: tight Blinn-Phong highlight streaked along the wave normals.
                float3 H = normalize(L + V);
                float spec = pow(saturate(dot(N, H)), _SpecularPower) * _SpecularStrength * moon.shadowAttenuation;
                float3 specular = moon.color * spec;

                // Fresnel: glancing angles reflect the sky (dark blue-grey at night).
                float fresnel = pow(1.0 - saturate(dot(N, V)), _FresnelPower);
                float3 sky = ambient * 1.5 + moon.color * 0.05;
                float3 col = lerp(diffuse, sky, fresnel * 0.6) + specular;

                // Foam on crests and where waves meet the hull/shore.
                float foamNoise = SAMPLE_TEXTURE2D(_FoamNoise, sampler_FoamNoise, IN.positionWS.xz * 0.15 + _Time.y * 0.02).r;
                float foam = saturate((IN.crest - _FoamThreshold) * 4.0) * foamNoise;
                foam += (1.0 - depthDiff) * foamNoise * 0.6;
                col = lerp(col, _FoamColor.rgb * (ambient + 0.2), saturate(foam));

                float alpha = lerp(_ShallowColor.a, _DeepColor.a, depthDiff);
                alpha = lerp(alpha * 0.4, alpha, depthDiff); // soft edge against the hull
                col = MixFog(col, IN.fogFactor);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
