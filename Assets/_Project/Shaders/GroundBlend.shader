// V-02: Ada zemini. WorldTriplanarLit'in zemine özel sürümü:
//  - üst yüzde aynı dokunun döndürülmüş ikinci örneği gürültüyle karışır (tekrar deseni kırılır)
//  - ormanda nemli/koyu çamur lekeleri
//  - kıyıda (dünya Z < _ShoreZ, gürültülü sınır) kum katmanı
//  - su kenarında gelgitle ileri geri oynayan ıslak, parlak bant
// Lightmap / ışık probu, gölge, ek ışıklar (fener), sis destekli.
Shader "Goblin/GroundBlend"
{
    Properties
    {
        _BaseMap("Albedo", 2D) = "white" {}
        _BaseColor("Renk", Color) = (1,1,1,1)
        [Normal][NoScaleOffset] _BumpMap("Normal", 2D) = "bump" {}
        _BumpScale("Normal gücü", Float) = 1
        _Tiling("Döşeme (metre / doku)", Float) = 3
        [NoScaleOffset] _MacroMap("Makro gürültü (gri)", 2D) = "gray" {}
        _MacroScale("Makro ölçek (1/m)", Float) = 0.035
        _MacroStrength("Makro güç", Range(0,1)) = 0.45
        _Smoothness("Pürüzsüzlük", Range(0,1)) = 0.12
        _BlendSharpness("Triplanar keskinlik", Range(1,16)) = 6

        [Header(Yosun)]
        [NoScaleOffset] _MossMap("Yosun albedo", 2D) = "white" {}
        _MossTiling("Yosun döşeme (m)", Float) = 1.6
        _MossAmount("Yosun miktarı", Range(0,1)) = 0.5

        [Header(Camur)]
        _MudTint("Çamur rengi", Color) = (0.5, 0.44, 0.4, 1)
        _MudAmount("Çamur miktarı", Range(0,1)) = 0.5
        _MudSmoothness("Çamur pürüzsüzlüğü", Range(0,1)) = 0.5

        [Header(Kum)]
        [NoScaleOffset] _SandMap("Kum albedo", 2D) = "white" {}
        [Normal][NoScaleOffset] _SandBump("Kum normal", 2D) = "bump" {}
        _SandColor("Kum rengi", Color) = (1,1,1,1)
        _SandTiling("Kum döşeme (m)", Float) = 2.5
        _SandBumpScale("Kum normal gücü", Float) = 0.5
        _ShoreZ("Kum sınırı (dünya Z)", Float) = -70
        _ShoreNoise("Sınır dalgalanması (m)", Float) = 9
        _ShoreBlend("Sınır yumuşaklığı (m)", Float) = 2.5

        [Header(Islak bant)]
        _WetZ("Su kenarı (dünya Z)", Float) = -88
        _WetTide("Gelgit genliği (m)", Float) = 1.2
        _WetDarken("Islak koyuluk", Range(0,1)) = 0.5
        _WetSmoothness("Islak pürüzsüzlük", Range(0,1)) = 0.85
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);  SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);  SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MacroMap); SAMPLER(sampler_MacroMap);
            TEXTURE2D(_MossMap);  SAMPLER(sampler_MossMap);
            TEXTURE2D(_SandMap);  SAMPLER(sampler_SandMap);
            TEXTURE2D(_SandBump); SAMPLER(sampler_SandBump);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _BumpScale;
                float _Tiling;
                float _MacroScale;
                half _MacroStrength;
                half _Smoothness;
                half _BlendSharpness;
                half _Cutoff;
                half4 _MudTint;
                half _MudAmount;
                half _MudSmoothness;
                half4 _SandColor;
                float _SandTiling;
                half _SandBumpScale;
                float _ShoreZ;
                float _ShoreNoise;
                float _ShoreBlend;
                float _WetZ;
                float _WetTide;
                half _WetDarken;
                half _WetSmoothness;
                float _MossTiling;
                half _MossAmount;
            CBUFFER_END
            float _GlobalWetness;   // V-05: RainWetness yazar (0 kuru .. 1 ıslak)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 lightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 lightmapUV : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.lightmapUV = input.lightmapUV * unity_LightmapST.xy + unity_LightmapST.zw;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 pw = input.positionWS;
                float3 n = normalize(input.normalWS);
                float3 w = pow(abs(n), _BlendSharpness);
                w /= (w.x + w.y + w.z + 1e-5);
                float3 p = pw / max(_Tiling, 0.01);

                // makro gürültüler (üç farklı ölçek)
                half m1 = SAMPLE_TEXTURE2D(_MacroMap, sampler_MacroMap, pw.xz * _MacroScale).r;
                half m2 = SAMPLE_TEXTURE2D(_MacroMap, sampler_MacroMap, pw.zx * _MacroScale * 0.31 + 0.37).r;
                half m3 = SAMPLE_TEXTURE2D(_MacroMap, sampler_MacroMap, pw.xz * _MacroScale * 3.3 + 0.11).r;

                // üst yüz: aynı dokunun döndürülmüş + kaydırılmış ikinci örneği
                const float2x2 rot = float2x2(0.7986, -0.6018, 0.6018, 0.7986);     // 37°
                float2 uvA = p.xz;
                float2 uvB = mul(rot, p.xz) * 0.83 + float2(0.37, 0.61);
                half mixB = smoothstep(0.42, 0.58, m3);

                half3 topAlb = lerp(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvA).rgb,
                                    SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvB).rgb, mixB);
                half3 nA = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvA), _BumpScale);
                half3 nB = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvB), _BumpScale);
                nB.xy = mul(nB.xy, rot);                                            // döndürmeyi geri al
                half3 ty = lerp(nA, nB, mixB);

                // büyük ölçekli parlaklık varyasyonu
                topAlb *= lerp(1.0h, saturate(m1 * 2.0h), _MacroStrength);
                topAlb *= _BaseColor.rgb;

                // çamur lekeleri
                half mud = smoothstep(0.52, 0.68, m2 * 0.7 + m3 * 0.3) * _MudAmount;
                topAlb = lerp(topAlb, topAlb * _MudTint.rgb, mud);
                half smooth = lerp(_Smoothness, _MudSmoothness, mud);
                ty.xy *= lerp(1.0, 0.55, mud);

                // yosun yastıkları: dokuya gömülü değil, dünya gürültüsünden → tekrar etmez
                half m4 = SAMPLE_TEXTURE2D(_MacroMap, sampler_MacroMap, pw.xz * _MacroScale * 1.9 + 0.63).r;
                half mossMask = smoothstep(0.5, 0.62, m4 * 0.65 + m3 * 0.35 - mud * 0.3) * _MossAmount;
                half3 mossAlb = SAMPLE_TEXTURE2D(_MossMap, sampler_MossMap, pw.xz / max(_MossTiling, 0.01)).rgb;
                mossAlb *= lerp(1.0h, saturate(m1 * 2.0h), _MacroStrength * 0.5);
                topAlb = lerp(topAlb, mossAlb, mossMask);
                ty.xy *= lerp(1.0, 0.35, mossMask);
                smooth = lerp(smooth, 0.08, mossMask);

                // kum
                float shoreEdge = _ShoreZ + (m2 - 0.5) * _ShoreNoise + (m3 - 0.5) * 2.5;
                half sand = smoothstep(shoreEdge + _ShoreBlend, shoreEdge - _ShoreBlend, pw.z);
                float2 uvS = pw.xz / max(_SandTiling, 0.01);
                half3 sandAlb = SAMPLE_TEXTURE2D(_SandMap, sampler_SandMap, uvS).rgb * _SandColor.rgb;
                sandAlb *= lerp(1.0h, saturate(m1 * 2.0h), _MacroStrength * 0.6);
                half3 sandN = UnpackNormalScale(SAMPLE_TEXTURE2D(_SandBump, sampler_SandBump, uvS), _SandBumpScale);
                topAlb = lerp(topAlb, sandAlb, sand);
                ty = lerp(ty, sandN, sand);
                smooth = lerp(smooth, 0.2, sand);

                // ıslak bant: gelgit
                float tide = _WetZ + (sin(_Time.y * 0.45 + pw.x * 0.11) * 0.6 + sin(_Time.y * 0.27 - pw.x * 0.047) * 0.4) * _WetTide + (m3 - 0.5) * 1.6;
                half wet = smoothstep(tide + 1.6, tide - 0.4, pw.z);
                topAlb *= lerp(1.0, 1.0 - _WetDarken, wet);
                smooth = lerp(smooth, _WetSmoothness, wet);
                ty.xy *= lerp(1.0, 0.45, wet);

                // yağmur ıslaklığı: her yer koyulaşır ve parlar; çamurda su birikir (daha parlak)
                half rainWet = saturate(_GlobalWetness);
                topAlb *= lerp(1.0, 0.74, rainWet);
                smooth = lerp(smooth, max(smooth, 0.5 + 0.3 * mud), rainWet);
                ty.xy *= lerp(1.0, 0.7 - 0.4 * mud, rainWet);

                // yan yüzler (kutu kenarları): düz triplanar
                half3 albedo = topAlb * w.y;
                half3 tx = half3(0, 0, 1), tz = half3(0, 0, 1);
                if (w.y < 0.999)
                {
                    half3 side = (SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, p.zy).rgb * w.x +
                                  SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, p.xy).rgb * w.z) * _BaseColor.rgb;
                    albedo += side;
                    tx = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, p.zy), _BumpScale);
                    tz = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, p.xy), _BumpScale);
                }

                // whiteout triplanar normal
                tx = half3(tx.xy + n.zy, abs(tx.z) * n.x);
                ty = half3(ty.xy + n.xz, abs(ty.z) * n.y);
                tz = half3(tz.xy + n.xy, abs(tz.z) * n.z);
                float3 nWS = normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);

                InputData d = (InputData)0;
                d.positionWS = pw;
                d.positionCS = input.positionCS;
                d.normalWS = nWS;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(pw);
                d.shadowCoord = TransformWorldToShadowCoord(pw);
                d.fogCoord = InitializeInputDataFog(float4(pw, 1.0), input.fogFactor);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);
            #if defined(LIGHTMAP_ON)
                d.bakedGI = SampleLightmap(input.lightmapUV, nWS);
            #else
                d.bakedGI = SampleSH(nWS);
            #endif

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.alpha = 1;
                s.metallic = 0;
                s.specular = half3(0, 0, 0);
                s.smoothness = smooth;
                s.occlusion = 1;
                s.normalTS = half3(0, 0, 1);

                half4 c = UniversalFragmentPBR(d, s);
                c.rgb = MixFog(c.rgb, d.fogCoord);
                return c;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
        UsePass "Universal Render Pipeline/Lit/META"
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
