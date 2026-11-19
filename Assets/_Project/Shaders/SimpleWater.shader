// C-02: Basit gece denizi (URP). İki yönde kayan normal haritası, yüksek pürüzsüzlük (ay/fener yansıması),
// bakış açısına göre sis rengine kayan ufuk (fresnel). Opak; dalga geometrisi yok.
Shader "Goblin/SimpleWater"
{
    Properties
    {
        _DeepColor("Derin renk", Color) = (0.01, 0.025, 0.035, 1)
        _ShallowColor("Yüzey rengi", Color) = (0.03, 0.06, 0.07, 1)
        [Normal][NoScaleOffset] _BumpMap("Normal", 2D) = "bump" {}
        _BumpScale("Normal gücü", Float) = 0.6
        _Tiling("Döşeme (m)", Float) = 9
        _Speed("Akış hızı", Float) = 0.03
        _Smoothness("Pürüzsüzlük", Range(0,1)) = 0.9
        _FresnelPower("Fresnel", Float) = 4
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
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _ShallowColor;
                half _BumpScale;
                float _Tiling;
                float _Speed;
                half _Smoothness;
                half _FresnelPower;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float fogFactor : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o);
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.positionWS.xz / _Tiling;
                float t = _Time.y * _Speed;
                half3 n1 = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv + float2(t, t * 0.6)), _BumpScale);
                half3 n2 = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv * 1.7 + float2(-t * 0.8, t * 0.3)), _BumpScale);
                half3 tn = normalize(half3(n1.xy + n2.xy, n1.z * n2.z));
                float3 nWS = normalize(float3(tn.x, tn.z, tn.y)); // üst yüz: tanjant x→X, y→Z
                if (i.normalWS.y < 0.5) nWS = normalize(i.normalWS);

                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half fres = pow(1.0h - saturate(dot(nWS, V)), _FresnelPower);

                InputData d = (InputData)0;
                d.positionWS = i.positionWS; d.positionCS = i.positionCS; d.normalWS = nWS; d.viewDirectionWS = V;
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = InitializeInputDataFog(float4(i.positionWS, 1.0), i.fogFactor); d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1,1,1,1);
                d.bakedGI = SampleSH(nWS);

                SurfaceData s = (SurfaceData)0;
                s.albedo = lerp(_DeepColor.rgb, _ShallowColor.rgb, fres);
                s.alpha = 1; s.metallic = 0; s.smoothness = _Smoothness; s.occlusion = 1; s.normalTS = half3(0,0,1);
                half4 c = UniversalFragmentPBR(d, s);
                c.rgb += unity_FogColor.rgb * fres * 0.6; // ufukta sisli parıltı
                c.rgb = MixFog(c.rgb, d.fogCoord);
                return c;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
