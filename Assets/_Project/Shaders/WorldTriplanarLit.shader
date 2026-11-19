// C-02: Dünya koordinatlı triplanar PBR (URP). Kutu ölçeğinden bağımsız döşeme + büyük ölçekli renk varyasyonu
// (tekrar desenini kırar). Lightmap / ışık probu, gölge, ek ışıklar (fener), sis destekli.
Shader "Goblin/WorldTriplanarLit"
{
    Properties
    {
        _BaseMap("Albedo", 2D) = "white" {}
        _BaseColor("Renk", Color) = (1,1,1,1)
        [Normal][NoScaleOffset] _BumpMap("Normal", 2D) = "bump" {}
        _BumpScale("Normal gücü", Float) = 1
        _Tiling("Döşeme (metre / doku)", Float) = 4
        [NoScaleOffset] _MacroMap("Makro gürültü (gri)", 2D) = "gray" {}
        _MacroScale("Makro ölçek (1/m)", Float) = 0.035
        _MacroStrength("Makro güç", Range(0,1)) = 0.4
        _Smoothness("Pürüzsüzlük", Range(0,1)) = 0.12
        _BlendSharpness("Triplanar keskinlik", Range(1,16)) = 6
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
            CBUFFER_END

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
                float3 n = normalize(input.normalWS);
                float3 w = pow(abs(n), _BlendSharpness);
                w /= (w.x + w.y + w.z + 1e-5);
                float3 p = input.positionWS / max(_Tiling, 0.01);

                half3 albedo =
                    SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, p.zy).rgb * w.x +
                    SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, p.xz).rgb * w.y +
                    SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, p.xy).rgb * w.z;

                // büyük ölçekli varyasyon: aynı dokunun tekrarını kırar
                float2 mp = (abs(n.y) > 0.5 ? input.positionWS.xz : input.positionWS.xy + input.positionWS.zy) * _MacroScale;
                half macro = SAMPLE_TEXTURE2D(_MacroMap, sampler_MacroMap, mp).r;
                albedo *= lerp(1.0h, saturate(macro * 2.0h), _MacroStrength);
                albedo *= _BaseColor.rgb;

                // whiteout triplanar normal
                half3 tx = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, p.zy), _BumpScale);
                half3 ty = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, p.xz), _BumpScale);
                half3 tz = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, p.xy), _BumpScale);
                tx = half3(tx.xy + n.zy, abs(tx.z) * n.x);
                ty = half3(ty.xy + n.xz, abs(ty.z) * n.y);
                tz = half3(tz.xy + n.xy, abs(tz.z) * n.z);
                float3 nWS = normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);

                InputData d = (InputData)0;
                d.positionWS = input.positionWS;
                d.positionCS = input.positionCS;
                d.normalWS = nWS;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                // sis piksel başına: dev zemin küplerinde köşe başına sis enterpolasyonu bozuluyordu
                d.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
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
                s.smoothness = _Smoothness;
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
