// KULLANICI (cila): Gölge siluetleri sis içinde kaybolmasın diye sisi yalnızca kısmen uygulayan, saydam, ışıksız shader.
// _FogStrength: 1 = normal sis, 0 = hiç sis yok. Gözler için düşük, gövde için orta değer.
Shader "Goblin/FogResistUnlit"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Color", Color) = (0,0,0,1)
        [HDR] _EmissionColor ("Emission", Color) = (0,0,0,0)
        _FogStrength ("Fog Strength", Range(0,1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Unlit"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _EmissionColor;
                half _FogStrength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float fogFactor : TEXCOORD1; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half4 col = tex * _BaseColor;
                col.rgb += _EmissionColor.rgb * tex.a;
                half3 fogged = MixFog(col.rgb, i.fogFactor);
                col.rgb = lerp(col.rgb, fogged, _FogStrength);
                return col;
            }
            ENDHLSL
        }
    }
}
