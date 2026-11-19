// V-01 (görsel cila): Fener huzmesi siste görünür. Ağır volumetrik sistem DEĞİL: yalnızca huzme konisinin içinde
// kısa (sabit adımlı) bir ışın yürüyüşü; koni dışında hiçbir maliyet yok. Koni mesh'i: tepe orijinde, eksen +Z,
// birim boy ve birim yarıçap (FlashlightBeam ölçekler). Arka yüzler çizilir → kamera koninin içindeyken de çalışır.
Shader "Goblin/LightBeam"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 0.93, 0.78, 1)
        _Intensity ("Intensity", Float) = 0.35
        _InnerRatio ("Inner cone ratio", Range(0, 0.95)) = 0.45
        _Falloff ("Distance falloff", Float) = 0.06
        _NearFade ("Camera near fade (m)", Float) = 1.6
        _NoiseScale ("Dust noise scale", Float) = 0.55
        _NoiseSpeed ("Dust noise speed", Float) = 0.12
        _NoiseAmount ("Dust noise amount", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+20" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Beam"
            Blend One One
            ZWrite Off
            ZTest Always
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
                half _InnerRatio;
                half _Falloff;
                half _NearFade;
                half _NoiseScale;
                half _NoiseSpeed;
                half _NoiseAmount;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            float hash31 (float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float vnoise (float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = lerp(lerp(hash31(i), hash31(i + float3(1, 0, 0)), f.x),
                               lerp(hash31(i + float3(0, 1, 0)), hash31(i + float3(1, 1, 0)), f.x), f.y);
                float b = lerp(lerp(hash31(i + float3(0, 0, 1)), hash31(i + float3(1, 0, 1)), f.x),
                               lerp(hash31(i + float3(0, 1, 1)), hash31(i + float3(1, 1, 1)), f.x), f.y);
                return lerp(a, b, f.z);
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 camWS = GetCameraPositionWS();
                float3 toFrag = i.positionWS - camWS;
                float tExit = length(toFrag);
                float3 dirWS = toFrag / max(tExit, 1e-4);

                // sahne derinliği: huzme duvarın arkasına geçmez
                float2 uv = i.screenPos.xy / i.screenPos.w;
                float rawDepth = SampleSceneDepth(uv);
                float sceneEye = LinearEyeDepth(rawDepth, _ZBufferParams);
                float3 camFwd = -UNITY_MATRIX_V[2].xyz;
                float sceneDist = sceneEye / max(dot(dirWS, camFwd), 1e-3);
                float tEnd = min(tExit, sceneDist);

                // koni uzayı (birim koni: x²+y² <= z², 0 <= z <= 1); t dünya uzunluğu olarak kalır
                float3 o = mul(GetWorldToObjectMatrix(), float4(camWS, 1)).xyz;
                float3 d = mul((float3x3)GetWorldToObjectMatrix(), dirWS);

                float a = d.x * d.x + d.y * d.y - d.z * d.z;
                float b = o.x * d.x + o.y * d.y - o.z * d.z;
                float c = o.x * o.x + o.y * o.y - o.z * o.z;
                float tIn = 0;
                bool inside = (c < 0) && (o.z > 0) && (o.z < 1);
                if (!inside)
                {
                    float aa = abs(a) < 1e-5 ? 1e-5 : a;
                    float disc = max(b * b - aa * c, 0);
                    float tCone = (-b - sqrt(disc)) / aa;
                    if (aa > 0 || d.z > 0) tIn = max(tIn, tCone);
                    if (d.z < 0) tIn = max(tIn, (1 - o.z) / d.z);
                }
                if (tEnd <= tIn) return half4(0, 0, 0, 0);

                const int STEPS = 20;
                float stepLen = (tEnd - tIn) / STEPS;
                float jitter = InterleavedGradientNoise(i.positionCS.xy, 0);
                float beamLen = length(mul((float3x3)GetObjectToWorldMatrix(), float3(0, 0, 1)));
                float fogK = unity_FogParams.x;
                float sum = 0;
                for (int s = 0; s < STEPS; s++)
                {
                    float t = tIn + (s + jitter) * stepLen;
                    float3 p = o + d * t;
                    if (p.z <= 0.002 || p.z >= 1) continue;
                    float r = length(p.xy) / p.z;                       // 0 eksen .. 1 kenar
                    float ang = 1 - smoothstep(_InnerRatio, 1.0, r);
                    float dist = p.z * beamLen;                         // fenerden uzaklık (m)
                    float atten = 1.0 / (1.0 + _Falloff * dist * dist);
                    float endFade = saturate((1 - p.z) * 4);
                    float f = fogK * t;                                 // kameraya kadar sis geçirgenliği
                    float trans = exp2(-f * f);
                    float nearFade = saturate(t / max(_NearFade, 0.01));
                    float3 pw = camWS + dirWS * t;
                    float n = vnoise(pw * _NoiseScale + float3(_Time.y * _NoiseSpeed, -_Time.y * _NoiseSpeed * 0.6, 0));
                    float dust = lerp(1, n * 1.6, _NoiseAmount);
                    sum += ang * atten * endFade * trans * nearFade * nearFade * dust;
                }
                sum *= stepLen;
                return half4(_Color.rgb * (_Intensity * sum), 0);
            }
            ENDHLSL
        }
    }
}
