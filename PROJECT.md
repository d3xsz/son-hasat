# PROJECT.md — Son Hasat

> Oyun adı KULLANICI kararıyla "Son Hasat" oldu (2026-11-09; eski: Sisli Ada: Goblin Seferi). Bölüm 1: Sisli Ada, Bölüm 2: Derinlikler.

> Kısa proje özeti ve değişmez kurallar (v0.3). Detaylar: `GDD.md`. İlerleme ve görev sırası: `PROGRESS.md`.

## Tek Cümlelik Özet
4 kişilik co-op, bölüm bazlı, karanlık ve sisli bir adada geçen goblin macera oyunu. Güvenli sandığın totemler aslında adayı uyandırır.

## Teknik Kurallar
- **Motor:** Unity 6 LTS, URP
- **Multiplayer:** Netcode for GameObjects (Unity Relay + Lobby veya LAN/IP). Steam yok.
- **Girdi:** Input System
- **Kamera:** 1. şahıs (Cinemachine veya basit FPS kamera)
- **Dil:** C#
- **Hedef:** PC, 60 FPS. Üniversite vize projesi (yayın yok)

## Temel Kurallar (Değişmez)
1. Ada **her zaman karanlık ve sisli**.
2. Envanter **4 slot**. Slot 1: el feneri (kilitli, zorunlu). Slot 2: mızrak (başlangıç eşyası, bırakılabilir). Geriye 2 slot.
3. Fener: **pil yok**, **ısınma + cooldown** mekaniği.
4. Mızrak çok amaçlı alet (silah değil). Oyun kaçış + bulmaca.
5. Maksimum **4 oyuncu**.
6. Karakter stili: Clash Royale goblinleri (büyük kafa, sivri kulak, iri burun, stilize).
7. Totemler güvenli alan sağlar **ama alınınca Ada Öfkesi +20 artar**. Totem **ağır eşyadır**: 1 slot, taşırken hız ~%40 düşer, koşulamaz, mızrak kullanılamaz, aynı anda en fazla 1 ağır eşya.
8. Haritada **5 totem** var. Öfke sadece totem alınca artar (0/20/40/60/80/100).
9. **Zehir tek değerdir (0–100).** Dolunca oyuncu bayılır; **45 sn** içinde kaldırılmazsa ölür (izleyici olur). Fiziksel yaratıklar hasar yerine zehir ekler.
10. **Mağara çöküşü sadece Öfke 100'de** (5 totemin hepsi alınınca) gerçekleşir. Totemsiz geçiş yok.
11. Ragdoll sadece bayılma/kayma anlarında, görsel amaçlı, ağda senkronize edilmez.
12. Bölüm 1 sonunda mağara girişinde olay → kayarak düşüş → Bölüm 2.

## Bölüm 1 Akışı
1. Tekne ile varış sinematiği
2. Kıyı (öğretici)
3. Orman: hastalık çiçekleri + totemler
4. Öfke artar: yağmur ↑, hayvan sesleri ↑, hastalık yayılır
5. Yönlendirme: yollar kapanır, mağara ışığı çeker
6. Mağara olayı: çöküş + kayma sekansı
7. Bölüm 2'ye geçiş

## Ada Öfkesi (Wrath) Tablosu
| Alınan Totem | Öfke | Etki |
|---|---|---|
| 0 | 0 | Hafif rüzgar |
| 1 | 20 | Hafif yağmur, ilk hayvan sesleri, akan su izleri |
| 2 | 40 | Güçlü yağmur, şimşek, hastalık yayılmaya başlar, düşen ağaçlar geri yolları kapatır |
| 3 | 60 | Hayvan sesleri yükselir, sis kalınlaşır, hastalık duvarı genişler, mağaradan kırmızı ışık + uğultu, gölge kademesi |
| 4 | 80 | Fırtına, dev gölgeler, görüş zayıf, geride kalan totem ışıkları söner, fiziksel yaratıklar başlar |
| 5 | 100 | Mağara tetikleyicisi aktif |

## Tehdit Kademesi
Ses (Öfke 20–40) → Gölge (60) → Fiziksel yaratık (80–100, aynı anda max 2–3, zehir verir, mızrakla itilir).

## Yönlendirme (Görünmez Duvar Yok)
Düşen ağaçlar (40), yayılan hastalık duvarı (60), mağaradan kırmızı ışık + uğultu (60, 80'de yoğunlaşır), mağaraya akan yağmur suyu (20), mağara yönünde sıralı totemler.

## Işık / Sis
Baked lighting + max 4 fener spot + totem ışıkları + şimşek. Unity Fog (Öfke ile yoğunlaşır) + sis partikülleri. Volumetrik yok. URP Volume ile vignette / zehir efekti.

## Script Listesi
`GameManager`, `WrathSystem`, `WeatherController`, `ThreatDirector`, `GuidanceEvents`, `AmbientAudioController`, `NetworkPlayer`, `InventorySystem` (+ `ItemData` ScriptableObject, `isHeavy`), `FlashlightController` (ısı/cooldown), `Spear`, `PoisonSystem`, `PoisonZone`, `Totem`, `CaveTrigger`, `CollapseSequence`, `ObjectiveSystem`, `ChapterLoader`

## Klasör Yapısı
```
Assets/
  _Project/
    Art/ (Characters, Environment, VFX, UI)
    Audio/ (Music, SFX, Ambience)
    Prefabs/
    Scenes/ (MainMenu, Lobby, Ch1_Intro, Ch1_Island, Ch1_Slide)
    ScriptableObjects/ (Items, Chapters)
    Scripts/
      Core/ Player/ Inventory/ Environment/ Network/ UI/
    Settings/ (URP, Input)
```

## Kapsam (Vize)
Bölüm 1 tam + Bölüm 2'ye geçiş. Steam, başarım, çoklu bölüm, dedicated server, late join yok.

## Açık Kararlar
Oyun adı, asset kaynakları. (Varsayımlar: bayılma 45 sn, ölen izleyici olur, yaşayan totem alanında ~5 sn diriltme. Denge testinde değişebilir.)
