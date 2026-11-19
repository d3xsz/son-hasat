// Son Hasat — prosedürel gece gökyüzü (skybox). Ufuk sis rengiyle birleşir (sisli ağaç/dağ siluetleri gökle kesik
// çizgi oluşturmasın), yukarı doğru koyu laciverte geçer. Yıldızlar (hafif titrer), soluk ay + hale, yavaş kayan ince bulutlar.
// Asset yok; tümü gürültüden üretilir.
Shader "Goblin/NightSky"
{
    Properties
    {
        _HorizonColor ("Ufuk (sis rengi)", Color) = (0.058, 0.074, 0.1, 1)
        _ZenithColor ("Tepe", Color) = (0.012, 0.018, 0.035, 1)
        _HorizonHeight ("Ufuk geçiş yüksekliği", Range(0.01, 1)) = 0.28
        _StarDensity ("Yıldız yoğunluğu", Range(0.9, 0.999)) = 0.985
        _StarBrightness ("Yıldız parlaklığı", Range(0, 4)) = 1.4
        _MoonDir ("Ay yönü", Vector) = (0.45, 0.32, 0.83, 0)
        _MoonColor ("Ay rengi", Color) = (0.85, 0.9, 1, 1)
        _MoonSize ("Ay boyutu", Range(0.995, 0.9999)) = 0.9993
        _MoonGlow ("Ay halesi", Range(0, 2)) = 0.6
        _CloudColor ("Bulut rengi", Color) = (0.07, 0.085, 0.11, 1)
        _CloudCover ("Bulut örtüsü", Range(0, 1)) = 0.45
        _CloudSpeed ("Bulut hızı", Float) = 0.004
        _MilkyWay ("Samanyolu gücü", Range(0, 2)) = 1
        _ShootingStars ("Kayan yıldız (0 kapalı)", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _HorizonColor, _ZenithColor, _MoonDir, _MoonColor, _CloudColor;
                float _HorizonHeight, _StarDensity, _StarBrightness, _MoonSize, _MoonGlow, _CloudCover, _CloudSpeed, _MilkyWay, _ShootingStars;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float Hash3(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float Noise2(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                float a = Hash3(float3(i, 1)), b = Hash3(float3(i + float2(1, 0), 1));
                float c = Hash3(float3(i + float2(0, 1), 1)), d = Hash3(float3(i + float2(1, 1), 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }
            float Fbm(float2 p) { float s = 0, a = 0.5; for (int i = 0; i < 5; i++) { s += Noise2(p) * a; p *= 2.03; a *= 0.5; } return s; }

            // Bir yıldız katmanı: 3B hücre başına en çok bir yıldız. rgb = renkli parlaklık.
            float3 StarLayer(float3 d, float scale, float density, float seed, float sizeK)
            {
                float3 p = d * scale;
                float3 cell = floor(p);
                float r = Hash3(cell + seed);
                if (r <= density) return 0;
                // yıldız hücre içinde kalmalı (yarıçap + kayma <= 0.5), yoksa hücre sınırında kesilip köşeli görünür
                float3 c = cell + 0.5 + (float3(Hash3(cell + 3.1 + seed), Hash3(cell + 7.7 + seed), Hash3(cell + 1.3 + seed)) - 0.5) * 0.3;
                float dist = length(p - c);
                float mag = (r - density) / (1 - density);                      // 0 sönük .. 1 parlak
                float tw = 0.72 + 0.28 * sin(_Time.y * (1.2 + r * 5.0) + r * 40.0);
                float core = saturate(1.0 - dist * sizeK * (3.6 - mag * 0.8));
                float halo = saturate(1.0 - dist * sizeK * 2.9) * mag * mag * 0.22;   // parlak yıldızlarda yumuşak hale
                float t = Hash3(cell + 11.3 + seed);
                float3 tint = lerp(float3(0.72, 0.84, 1.0), float3(1.0, 0.86, 0.68), smoothstep(0.55, 0.95, t));
                tint = lerp(tint, float3(1, 1, 1), 0.35);
                return tint * (core * (0.5 + 0.9 * mag) + halo) * tw;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float up = saturate(d.y);
                // gradyan: ufukta sis rengi, yukarı doğru koyu
                float h = pow(saturate(up / _HorizonHeight), 0.6);
                float3 col = lerp(_HorizonColor.rgb, _ZenithColor.rgb, h);
                if (d.y < 0) col = _HorizonColor.rgb; // ufkun altı: sis rengi
                float skyFade = smoothstep(0.02, 0.25, up);

                // samanyolu: gökyüzünü çapraz kesen soluk, bulutsu bant
                float3 bandN = normalize(float3(0.42, 0.55, -0.72));
                float3 bandA = normalize(cross(bandN, float3(0, 1, 0))), bandB = cross(bandN, bandA);
                float bd = dot(d, bandN);
                float2 buv = float2(dot(d, bandA), dot(d, bandB));
                float bnoise = Fbm(buv * 5.0 + 3.7);
                float band = exp(-bd * bd * 14.0) * (0.25 + 0.75 * bnoise);
                float dust = smoothstep(0.42, 0.7, Fbm(buv * 9.0 + 11.0)) * exp(-bd * bd * 60.0);   // ortadaki koyu toz şeridi
                float mw = band * (1.0 - dust * 0.6) * _MilkyWay * skyFade;
                col += lerp(float3(0.026, 0.034, 0.064), float3(0.05, 0.042, 0.07), bnoise) * mw;

                // yıldızlar: parlak seyrek katman + ince sık katman (samanyolunda yoğunlaşır)
                float3 stars = StarLayer(d, 260.0, _StarDensity, 0.0, 1.0);
                stars += StarLayer(d, 520.0, saturate(_StarDensity - 0.012 - mw * 0.05), 5.0, 1.15) * 0.45;
                stars += StarLayer(d, 95.0, 0.9965, 9.0, 0.85) * 1.6;            // birkaç iri, parlak yıldız
                stars *= skyFade * _StarBrightness;

                // kayan yıldız: birkaç saniyede bir kısa çizgi
                float shoot = 0;
                if (_ShootingStars > 0.001)
                {
                    float slotLen = 6.5;
                    float slot = floor(_Time.y / slotLen), ph = frac(_Time.y / slotLen) / 0.11;
                    float h1 = Hash3(float3(slot, 1.7, 4.2)), h2 = Hash3(float3(slot, 8.1, 2.3)), h3 = Hash3(float3(slot, 5.5, 9.9));
                    if (ph < 1.0 && h1 < 0.6 * _ShootingStars)
                    {
                        float3 s0 = normalize(float3(h2 * 2 - 1, 0.35 + 0.55 * h3, h1 * 3.3 - 1));
                        float3 vel = normalize(cross(s0, float3(h3 - 0.5, 1.0, h2 - 0.5)));
                        float3 head = normalize(s0 + vel * ph * 0.22);
                        float3 rel = d - head;
                        float along = dot(rel, -vel);
                        float perp = length(rel + vel * along);
                        shoot = smoothstep(0.0022, 0.0, perp) * saturate(1.0 - along / 0.07) * step(0.0, along) * sin(ph * 3.14159);
                    }
                }

                // bulutlar: iki katman, farklı hızda kayar
                float2 cuv = d.xz / max(d.y + 0.15, 0.05) * 0.6 + _Time.y * _CloudSpeed * float2(1, 0.4);
                float cn = Fbm(cuv * 1.3) * 0.7 + Fbm(cuv * 2.9 + _Time.y * _CloudSpeed * float2(-0.6, 0.9) + 4.0) * 0.3;
                float cloud = smoothstep(1.0 - _CloudCover, 1.0 - _CloudCover + 0.35, cn) * smoothstep(0.0, 0.2, up);

                // ay: küre gibi gölgelenir (şişkin evre), yüzeyde koyu denizler; geniş hale + ince taç halkası
                float3 md = normalize(_MoonDir.xyz);
                float mdot = dot(d, md);
                float mRad = sqrt(saturate(1.0 - _MoonSize * _MoonSize));
                float3 mRight = normalize(cross(float3(0, 1, 0), md)), mUp = cross(md, mRight);
                float2 muv = float2(dot(d, mRight), dot(d, mUp)) / max(mRad, 1e-4);
                float mr = length(muv);
                float disc = smoothstep(1.0, 0.94, mr) * step(0.0, mdot);
                float mz = sqrt(saturate(1.0 - mr * mr));
                float3 mNormal = float3(muv, mz);
                float lit = saturate(dot(mNormal, normalize(float3(-0.5, 0.28, 0.82))) * 1.5 + 0.18);
                float maria = smoothstep(0.38, 0.62, Fbm(muv * 2.2 + 7.3));
                float craters = Noise2(muv * 9.0 + 2.0);
                float surf = lerp(0.62, 1.0, maria) * (0.92 + 0.16 * craters) * (0.7 + 0.3 * mz);
                float3 moonCol = _MoonColor.rgb * disc * (lit * surf * 1.2 + 0.035);

                float glow = pow(saturate(mdot), 220.0) * 0.5 + pow(saturate(mdot), 12.0) * 0.12;
                float ang = acos(clamp(mdot, -1.0, 1.0));
                float ring = exp(-pow((ang - mRad * 5.5) / (mRad * 1.1), 2.0)) * 0.05;       // soluk taç halkası
                glow += ring;

                col += stars * (1 - cloud);
                col += float3(0.9, 0.95, 1.0) * shoot * 2.2 * (1 - cloud);
                col += _MoonColor.rgb * glow * _MoonGlow;
                // bulut: ay yakınında kenarları gümüşlenir
                float silver = saturate(cloud * (1.0 - cloud) * 4.0) * pow(saturate(mdot), 6.0);
                float3 cloudCol = _CloudColor.rgb + _MoonColor.rgb * (glow * 0.6 + silver * 0.22);
                col = lerp(col, cloudCol, cloud * 0.85);
                col += moonCol * (1 - cloud * 0.7);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
