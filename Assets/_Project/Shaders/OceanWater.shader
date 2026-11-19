// Son Hasat — gerçekçi deniz (URP). Gerstner dalgaları (vertex), iki katmanlı dalgacık normali, derinliğe göre renk,
// fresnel + ortam yansıması, ay ışığı parlaması, dalga tepesi ve kıyı köpüğü (sahne derinliğinden), yumuşak kıyı kenarı, sis.
// _WaveAmp = 0 → düz (uzak deniz plakası / menü); dalga ağında 1. Vertex rengi R (varsa) dalgayı kenarda söndürmek için.
Shader "Goblin/OceanWater"
{
    Properties
    {
        _DeepColor ("Derin renk", Color) = (0.01, 0.03, 0.045, 1)
        _ShallowColor ("Sığ renk", Color) = (0.05, 0.12, 0.13, 1)
        _CrestColor ("Tepe rengi (ışık geçirgen)", Color) = (0.12, 0.22, 0.2, 1)
        _FoamColor ("Köpük", Color) = (0.75, 0.8, 0.82, 1)
        _BumpMap ("Dalgacık normali", 2D) = "bump" {}
        _BumpScale ("Dalgacık gücü", Float) = 0.6
        _Tiling ("Dalgacık ölçeği (m)", Float) = 9
        _Speed ("Dalgacık hızı", Float) = 0.04
        _Smoothness ("Pürüzsüzlük", Range(0,1)) = 0.9
        _FresnelPower ("Fresnel üssü", Float) = 4
        _ReflectStrength ("Yansıma gücü", Range(0,2)) = 0.8
        _SpecStrength ("Parlama gücü", Range(0,4)) = 1.6

        _WaveAmp ("Dalga çarpanı", Range(0,2)) = 1
        _WaveSpeed ("Dalga hızı", Float) = 1
        _WaveA ("Dalga A (yön xy, diklik, boy)", Vector) = (1, 0.6, 0.16, 22)
        _WaveB ("Dalga B", Vector) = (0.7, 1, 0.12, 13)
        _WaveC ("Dalga C", Vector) = (-0.4, 1, 0.1, 7)
        _WaveD ("Dalga D", Vector) = (0.2, -1, 0.07, 4.2)

        _DepthFade ("Renk derinliği (m)", Float) = 6
        _EdgeSoft ("Kıyı yumuşaklığı (m)", Float) = 0.35
        _ShoreFoamDist ("Kıyı köpüğü (m)", Float) = 1.4
        _CrestFoam ("Tepe köpüğü", Range(0,2)) = 0.7
        _BaseY ("Durgun su yüksekliği", Float) = -0.12
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-50" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Ocean"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor, _ShallowColor, _CrestColor, _FoamColor, _BumpMap_ST;
                float _BumpScale, _Tiling, _Speed, _Smoothness, _FresnelPower, _ReflectStrength, _SpecStrength;
                float _WaveAmp, _WaveSpeed; float4 _WaveA, _WaveB, _WaveC, _WaveD;
                float _DepthFade, _EdgeSoft, _ShoreFoamDist, _CrestFoam, _BaseY;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 screenPos  : TEXCOORD2;
                float  height     : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
            };

            float3 Gerstner(float4 w, float3 p, float amp, inout float3 tangent, inout float3 binormal)
            {
                float steep = w.z * amp;
                float k = 6.2831853 / w.w;
                float c = sqrt(9.8 / k);
                float2 d = normalize(w.xy);
                float f = k * (dot(d, p.xz) - c * _Time.y * _WaveSpeed);
                float a = steep / k;
                float sf = sin(f), cf = cos(f);
                tangent  += float3(-d.x * d.x * steep * sf, d.x * steep * cf, -d.x * d.y * steep * sf);
                binormal += float3(-d.x * d.y * steep * sf, d.y * steep * cf, -d.y * d.y * steep * sf);
                return float3(d.x * a * cf, a * sf, d.y * a * cf);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                float amp = _WaveAmp * v.color.r;
                float3 t = float3(1, 0, 0), b = float3(0, 0, 1);
                float3 off = 0;
                if (amp > 0.001)
                {
                    off += Gerstner(_WaveA, p, amp, t, b);
                    off += Gerstner(_WaveB, p, amp, t, b);
                    off += Gerstner(_WaveC, p, amp, t, b);
                    off += Gerstner(_WaveD, p, amp, t, b);
                }
                p += off;
                o.positionWS = p;
                o.normalWS = normalize(cross(b, t));
                o.height = off.y;
                o.positionCS = TransformWorldToHClip(p);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float3 Ripple(float2 uv)
            {
                return UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv), _BumpScale);
            }

            half4 frag(Varyings i) : SV_Target
            {
                // dalgacık: iki ters yönde kayan katman
                float2 uv = i.positionWS.xz / _Tiling;
                float t = _Time.y * _Speed;
                float3 r1 = Ripple(uv + float2(t, t * 0.6));
                float3 r2 = Ripple(uv * 1.9 + float2(-t * 0.8, t * 0.45));
                float3 rip = normalize(float3(r1.xy + r2.xy, r1.z * r2.z));
                float3 n = normalize(i.normalWS + float3(rip.x, 0, rip.y));

                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                float ndv = saturate(dot(n, V));
                float fres = pow(1 - ndv, _FresnelPower);

                // sahne derinliği → su kalınlığı
                float2 suv = i.screenPos.xy / i.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float surfEye = i.screenPos.w;
                float waterDepth = max(0, sceneEye - surfEye);

                float3 col = lerp(_ShallowColor.rgb, _DeepColor.rgb, saturate(waterDepth / _DepthFade));
                // dalga tepesinde ışık geçirgenliği (yeşilimsi parlama)
                col = lerp(col, _CrestColor.rgb, saturate(i.height * 1.6) * 0.7);

                // yansıma (ortam/gökyüzü) + ay ışığı parlaması
                float3 R = reflect(-V, n);
                float3 env = GlossyEnvironmentReflection(R, 1 - _Smoothness, 1);
                col = lerp(col, env, saturate(fres * _ReflectStrength));
                Light L = GetMainLight();
                float3 H = normalize(L.direction + V);
                float spec = pow(saturate(dot(n, H)), lerp(64, 512, _Smoothness)) * _SpecStrength;
                col += L.color * spec * (0.3 + 0.7 * fres);

                // köpük: kıyıda (sığ) + dalga tepelerinde; dalgacıkla kırılmış
                float noise = saturate((r1.x + r2.y) * 0.5 + 0.5);
                float shore = 1 - saturate(waterDepth / _ShoreFoamDist);
                float shoreBand = shore * saturate(0.55 + 0.45 * sin(_Time.y * 1.3 + waterDepth * 7)) * step(0.35, noise + shore * 0.4);
                float crest = saturate((i.height - 0.28) * 3) * _CrestFoam * smoothstep(0.55, 0.75, noise);
                float foam = saturate(shoreBand + crest);
                col = lerp(col, _FoamColor.rgb, foam * 0.85);

                float alpha = saturate(waterDepth / _EdgeSoft);
                alpha = max(alpha, foam * 0.9);
                col = MixFog(col, i.fogFactor);
                return half4(col, saturate(alpha));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
