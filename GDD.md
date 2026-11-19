# GAME DESIGN DOCUMENT (GDD)

**Oyun Adı:** Son Hasat *(KULLANICI kararı 2026-11-09; eski çalışma adı "Sisli Ada: Goblin Seferi" — karakterler korkuluk oldu)* — Bölüm 1: Sisli Ada, Bölüm 2: Derinlikler
**Tür:** 4 kişilik co-op, görev bazlı, bölümlü macera / hayatta kalma (hafif korku-komedi)
**Motor:** Unity 6 LTS (URP)
**Platform:** PC (üniversite vize projesi; Steam'e çıkış planlanmıyor)
**Oyuncu Sayısı:** 1–4 (online co-op)
**Referanslar:** Zort (bölüm bazlı ekip oyunu), Clash Royale goblin karakter stili
**Sürüm:** 0.3 – Kararlar işlendi, tutarlılık düzeltildi (1. şahıs, mızrak, fener ısınma, vize kapsamı)

---

## 1. Oyun Özeti

Dört goblin kardeş, efsanevi bir hazineyi aramak için küçük bir tekneyle karanlık ve sisli bir adaya gelir. Ada ilk bakışta ıssız ve sessizdir. Ancak adanın ormanında yayılan bir **çiçek hastalığı** ve ormanı koruyan **totemler** vardır. Goblinler totemlerin sağladığı güvenliğin bedelini bilmeden öderler: totemleri aldıkça ada uyanır, yağmur şiddetlenir, hayvanlar çıldırır ve goblinler kaçınılmaz olarak mağaraya sürüklenir. Bölüm, mağara girişinde yaşanan çöküşle biter ve oyuncular adanın derinliklerine düşerek 2. Bölüm'e geçer.

**Ana fikir:** *Güvende olmak için yaptığın şey seni tehlikeye sürükler.*

## 2. Tasarım Sütunları

1. **Ekip çalışması:** Her görev en az iki kişinin iletişimini gerektirir.
2. **Karanlık ve atmosfer:** Görüş sınırlıdır, ışık bir kaynaktır (el feneri).
3. **Seçimin bedeli:** Totem mekaniği oyuncuyu risk/ödül ikilemine sokar.
4. **Komik ama gergin:** Goblinlerin çizgi film tarzı, tehlikeli ortam ile tezat oluşturur.
5. **Bölüm bölüm yeni mekanik:** Her bölüm yeni bir ortam ve tehdit getirir.

## 3. Hedef Kitle

- 13+ yaş, arkadaşlarıyla oynayan casual/mid-core oyuncular
- Zort, Lethal Company, Content Warning, Phasmophobia sevenler
- Yayıncılar ve "arkadaş grubu" oyunu arayanlar

## 4. Görsel Stil

### 4.1 Karakterler (Goblinler)
- Clash Royale goblinleri gibi: **büyük kafa, büyük sivri kulaklar, iri burun, küçük gövde**, abartılı yüz ifadeleri.
- Stilize low-poly / hafif toon shading. Kalın hatlar yerine yumuşak, parlak renkli yüzeyler.
- Oyuncu özelleştirmesi: şapka, yama, kulak küpesi, deri tonu (yeşil varyasyonlar), maske. Karakter ayrımı için 4 oyuncuya 4 farklı renk aksesuarı.
- Animasyonlar abartılı: korkunca kulaklar yatar, zehirlenince yüz yeşilleşir.

### 4.2 Ada Atmosferi
- **Kesinlikle karanlık ve sisli.** Gündüz/gece yok; sürekli alacakaranlık veya gece.
- Mesafeli sis (distance fog), zeminde alçak sis partikülleri, soğuk mavi-mor ışık paleti.
- Tek güçlü sıcak ışık kaynağı: oyuncunun feneri. Hastalık çiçekleri ise zehirli yeşil/mor ışıldar (oyuncuya uyarı verir).
- Totemler hafif turuncu/altın ışık yayar (güvenli alan hissi).

### 4.3 Renk Paleti
| Öğe | Renk |
|---|---|
| Ada genel | Koyu lacivert, gri-yeşil |
| Sis | Soluk mavi-gri |
| El feneri | Sıcak sarı-beyaz |
| Hastalık çiçekleri | Neon yeşil / mor |
| Totemler | Amber / turuncu |
| Mağara girişi | Koyu kırmızı-kahve, hafif kızıl parıltı |

## 5. Çekirdek Oynanış

### 5.1 Kamera ve Kontrol
- **Birinci şahıs kamera (karar verildi).** Kamera kafa hizasında, hafif head-bob, fener ve mızrak sağ/sol elde görünür.
- Diğer oyuncuların goblinleri tam gövde modelle görünür (kulaklar, şapka vb. özelleştirme burada anlam kazanır).
- WASD, fare ile bakış, Shift koş, E etkileşim, F fener aç/kapa, 1-4 slot seçimi, Q eşya bırak, Sol tık mızrak/kullan, Sağ tık mızrak fırlat *(opsiyonel)*. Sesli sohbet için Discord yeterli.

### 5.2 Envanter
- **4 slotluk çanta** (her oyuncu için ayrı).
- Her slot bir eşya taşır. Yığınlanma yok (basit ve gergin yönetim).
- **El feneri zorunlu başlangıç eşyasıdır** ve 1. slotta kilitli gelir (bırakılamaz, ama kapatılabilir).
- **Mızrak 2. slotta başlangıç eşyasıdır** (bırakılabilir).
- Geriye 2 slot kalır: iksir, anahtar, görev eşyası vb.
- **Ağır eşyalar (totem, sandık, varil):** 1 slot kaplar ama `ItemData` içinde `isHeavy = true` ve bir **ağırlık** değeri vardır. Taşırken: hız ~%40 düşer, koşulamaz, mızrak kullanılamaz (iki el dolu), fener kullanılabilir. Aynı anda en fazla 1 ağır eşya taşınır. Değerler ayarlanabilir.

### 5.3 El Feneri
- Spot ışık, sınırlı menzil. **Pil yok; yerine ısınma (overheat) mekaniği var (karar verildi).**
- Fener açık kaldıkça **Isı** değeri (0–100) yükselir; kapalıyken yavaşça düşer.
- Isı 100'e ulaşınca fener **aşırı ısınır**: kendiliğinden söner, ekranda/elde buhar efekti çıkar ve **cooldown** (ör. 6–8 sn) başlar. Bu sürede fener açılamaz.
- Cooldown bitince fener tekrar kullanılabilir (ısı %40'a düşmüş olur). Böylece "aç-kapa, git-gel" oynanışı oluşur.
- Isı göstergesi diegetic olabilir: fener camı kızarır, ışık rengi sarıdan turuncuya kayar, hafif cızırtı sesi.
- Karanlık anlarda kısa kör kalma gerilim yaratır; takım arkadaşının feneri o sırada hayat kurtarır (ekip çalışması).
- Isınma hızı bölüme göre ayarlanabilir (ör. yağmurda biraz yavaş ısınır, mağarada hızlı).
- Fener ışığı bazı bitkileri ve izleri görünür kılar.

### 5.4 Zehir, Bayılma ve Ölüm
- Her oyuncunun tek bir **Zehir** değeri vardır (0–100). HUD'da sağlık barı olarak gösterilir (zehir arttıkça bar azalır). Ayrı bir hasar sistemi yoktur.
- Zehir kaynakları: hastalık çiçekleri/spor bulutu, zehir bölgeleri (yayılan hastalık duvarı) ve **fiziksel yaratıkların saldırısı** (hasar yerine zehir ekler).
- Zehir etkileri: ekran yeşilimsi bulanır, hareket yavaşlar, öksürük sesi (evreler için bkz. 6.4).
- Zehir tamamen dolarsa oyuncu **bayılır** (ragdoll, sadece görsel). Bayılma süresi **45 sn** (ayarlanabilir): bu sürede takım arkadaşı yanına gelip E'ye basılı tutarak kaldırırsa oyuncu zehir %50 ile ayağa kalkar.
- Süre dolarsa oyuncu **ölür** ve izleyici moduna geçer. Yaşayan bir totemin güvenli alanında takım arkadaşları ~5 sn diriltme etkileşimi yaparsa geri döner.
- Tüm oyuncular bayılır veya ölürse bölüm başarısız → son checkpoint'ten tekrar.

### 5.5 Mızrak (Silah Değil, Araç)
- Oyun **kaçış ve bulmaca ağırlıklı**; mızrak savaş aracı olmaktan çok **çok amaçlı alettir**.
- Kullanım alanları: hastalık çiçeklerini uzaktan dürtüp/kesip geçit açmak, küçük yaratıkları geri püskürtmek, uzaktaki düğmelere basmak, derinliği ölçmek, kayma sekansında tutunma noktası.
- Mızrak envanterde **1 slot** kaplar; herkes başlangıçta bir tane taşır (2. slot).
- Teknik olarak basit tutulur: raycast + kısa animasyon (vurma), fırlatma için Rigidbody.

### 5.6 Ragdoll / Fizik Kararı
- **Tam fizik tabanlı (active ragdoll, Zort/Gang Beasts tarzı) karakter hareketi bu proje için önerilmiyor.** Multiplayer'da senkronize etmek, 1. şahıs kamera ile birleştirmek ve kontrolü hissettirmek zordur; vize süresine sığmaz.
- **Önerilen kolay çözüm:** Normal animasyonlu karakter + **sadece belirli anlarda ragdoll**:
  - Zehirden bayılınca goblin yere ragdoll olarak yığılır.
  - Mağara çöküşünde / kayma sekansında karakterler ragdoll'a geçer (komik ve etkileyici).
  - Totem, sandık, varil gibi eşyalar Rigidbody ile fizikli olur.
- Ragdoll yalnızca **görsel efekt olarak** (client tarafında) çalışır, ağ üzerinden senkronize edilmez; bu da işi çok kolaylaştırır.

## 6. BÖLÜM 1: "Sisli Kıyı ve Zehirli Orman"

### 6.1 Akış Özeti
1. **Giriş sinematiği:** Tekne ile adaya varış
2. **Kıyı:** Öğretici bölüm, ilk fener ve çanta kullanımı
3. **Orman:** Hastalık çiçekleri, totemler, keşif
4. **Ada öfkelenir:** Yağmur, hayvan sesleri, tehditler artar
5. **Mağara girişi:** Olay ve çöküş
6. **Kayarak düşüş:** 2. Bölüm'e geçiş

### 6.2 Giriş Sinematiği (Tekne Animasyonu)
- Siyah ekrandan açılış, dalga ve gıcırdayan tekne sesi.
- Küçük, yamalı bir tekne yoğun sisin içinden ilerler. 4 goblin teknede (oyuncuların seçtiği görünümlerle).
- Fener ışığıyla sisi tarayan goblin, uzakta karanlık ada silüetini görür.
- Tekne kıyıya sert çarpar, goblinler fırlar. Başlık: oyunun logosu.
- Süre: 30–45 sn. **Atlanabilir** (hepsi hazırsa).
- Teknik: Unity Timeline + Cinemachine. Oyuncu karakterleri gerçek zamanlı render edilir, böylece özelleştirmeler görünür.

### 6.3 Kıyı (Öğretici)
- Fener, çanta, etkileşim, koşma, yardımlaşma öğretilir.
- Kıyıda ilk eşyalar: küçük iksir, tekne parçaları.
- İlk "ipucu": kıyıda kırık bir tabela / eski bir goblin kampı. Hikayeyi ima eder (ör. "Totemlere dokunma.").
- Teknenin kısa süre sonra dalgalara kapıldığı görülür (geri dönüş yok).

### 6.4 Orman ve Çiçek Hastalığı
**Hastalık Çiçekleri**
- Ormanın belli bölgelerinde yoğun, rastgele yerleşimli parlayan çiçekler.
- Yakınlaşınca spor bulutu çıkarır. Temas = zehirlenme.
- **Hastalık zamanla yayılır** (özellikle totemler alındıkça).
- Fenerle bakılırsa çiçeklerin zemindeki sporları görünür hale gelir; sis içinde fark etmek zordur.

**Zehirlenme Evreleri**
| Evre | Etki |
|---|---|
| 1 | Hafif öksürük, ekran kenarları yeşil |
| 2 | Hareket %20 yavaş, görüş bulanık |
| 3 | Sürekli hasar, ekran titremesi, hayal sesleri |
| 4 | Bayılma |

**Çözümler (Tedavi)**
- Totem güvenli alanı
- Bitki özü / iksir (envanterde yer kaplar)
- Takım arkadaşı yardımı (iksir paylaşma)

### 6.5 Totemler (Merkezi Mekanik)
**Görünürde:**
- Ormanda dağınık duran, yüzü oyulmuş, altın-turuncu parlayan taş/tahta totemler.
- Bir totemin etrafında yaklaşık 8–10 metrelik **güvenli bölge** vardır. Bu bölgede zehir birikmez ve zehir yavaşça iyileşir.
- Totemler dinlenme noktası ve checkpoint görevi görür.

**Gerçekte:**
- Totemler adayı **uykuda tutan mühürlerdir.**
- Totemi yerinden almak oyuncuya **taşınabilir güvenli alan** (daha küçük yarıçap) verir. Bu çok cezbedicidir.
- Ancak her totem alındığında **Ada Öfkesi** yükselir.

**Ada Öfkesi (Wrath) Sistemi**
Gizli veya yarı gizli bir değer (0–100). Her alınan totem +20. Haritada **5 totem** vardır ve Öfke yalnızca totem alınca artar (zamanla artmaz); değer her zaman 0, 20, 40, 60, 80 veya 100'dür. **Öfke 100 = mağara tetikleyicisi aktif.**

| Alınan Totem | Öfke | Etkiler |
|---|---|---|
| 0 | 0 | Normal atmosfer, hafif rüzgar |
| 1 | 20 | Hafif yağmur, ilk hayvan sesleri (baykuş, uzaktan uluma), akan su izleri |
| 2 | 40 | Güçlü yağmur, şimşek, ağaçlar sallanır, hastalık yayılmaya başlar, düşen ağaçlar geri yolları kapatır |
| 3 | 60 | Hayvan sesleri yükselir (çok yakın, çevreleyen), sis kalınlaşır, hastalık duvarı genişler, mağaradan kırmızı ışık ve uğultu, gölge kademesi |
| 4 | 80 | Fırtına, dev gölgeler, çiçekler hareket eder, görüş çok azalır, geride kalan totem ışıkları söner, fiziksel yaratıklar başlar |
| 5 | 100 | Mağara tetikleyicisi aktif (bkz. 6.7) |

**Taşınan Totem Dinamiği**
- Totem **ağır eşyadır** (bkz. 5.2): 1 slot kaplar, taşıyanı yavaşlatır, koşturmaz.
- Totem taşıyan oyuncunun çevresi korunur ama **ada ona özellikle tepki verir** (hayvan sesleri taşıyıcıya doğru yoğunlaşır).
- Totem yere bırakılırsa belli bir sürede mührü yenilemez (geri koymak bir bulmaca gerektirir, ancak öfke düşmez; bu bölümde geri dönüş yoktur).

**Neden oyuncu totemleri alsın?**
- Orman derinleştikçe hastalık yoğunluğu o kadar artar ki sabit totemler arası mesafe zehir sınırını aşar.
- Görev eşyalarına ulaşmak için taşınabilir koruma gerekir.
- Oyuncu "doğru olanı yapıyorum" sanır, kötülük farkında olmadan tetiklenir.

### 6.6 Yönlendirme: Mağaraya Doğru
Oyuncuları mağaraya itmek için **doğal yönlendirme** kullanılır, görünmez duvar yerine:
- Öfke arttıkça orman yolları **kapanır** (düşen ağaçlar, yayılan hastalık duvarları).
- Totemler alındıkça kalan güvenli yol **mağaraya doğru** daralır.
- Mağaradan gelen soluk kırmızı ışık ve ses; uzaktan bile fark edilir.
- Yağmur sırasında akan su izleri mağaraya doğru akar (çevresel ipucu).
- Haritadaki son totem mağara girişindedir.

**Yönlendirme Katmanları (Görünmez Duvar Yok)**
Oyuncuyu zorlamadan, çevreyle yönlendirme yapılır. Öfke seviyesine göre sırayla devreye girer:

| Araç | Nasıl çalışır | Devreye girme |
|---|---|---|
| **Düşen ağaçlar** | Geri dönüş yollarına şimşek/fırtınayla ağaç devrilir; yol kapanır | Öfke 40 (2. totem) |
| **Yayılan hastalık** | Çiçek bölgeleri mağaranın zıt yönünden başlayarak genişler; zehirli duvar oluşur | Öfke 60 (3. totem) |
| **Kırmızı ışık** | Mağara ağzından soluk kırmızı parıltı ve düşük frekanslı uğultu; sis içinde uzaktan görünür | Öfke 60 (3. totem), 80'de yoğunlaşır |
| **Akan yağmur suyu** | Zemin eğimiyle su izleri/dereler mağaraya doğru akar; yağmur arttıkça belirginleşir | Öfke 20 (1. totem) |
| **Totem ışığı** | Kalan totemler mağara yönünde sıralıdır; sönenler geride kalır | Sürekli |

- **Kural:** Hiçbir yol "görünmez duvarla" kapatılmaz; kapanan yol her zaman bir olayla (ağaç, hastalık, çamur kayması) açıklanır.
- Teknik: Düşen ağaçlar önceden yerleştirilmiş, Öfke eşiğinde animasyonla devrilen prefab'lardır (NavMesh/collider anında güncellenir). Hastalık duvarı, yarıçapı Öfke'ye göre büyüyen `PoisonZone` nesneleridir.

### 6.7 Mağara Girişi ve Çöküş (Bölüm Sonu Olayı)
1. Oyuncular mağara ağzına varır; son totem burada bulunur. Yağmur durur, ani sessizlik.
2. Öfke 100'e ulaşmış olmalıdır (5 totemin hepsi alınmış; 5. totem mağara girişindedir). `CaveTrigger` Öfke 100'de aktifleşir ve tüm goblinler eşik çizgisini geçtiğinde **olay tetiklenir**.
3. Zemin titrer. Mağara tavanından taşlar düşer. Tüm totemlerin ışığı aynı anda söner.
4. Zemin çöker ve eğimli, kaygan bir tünele dönüşür. **Kontrol sınırlı kayma sekansı** başlar:
   - Oyuncular kayarken sağa-sola hareket ederek engellerden kaçınır.
   - Diğer oyuncuları yakalayıp tutunmak mümkündür (ekip çalışması anı).
   - Fener sallanır, karanlık ve çığlıklar.
5. Ekran kararır; başlık: **"BÖLÜM 2: Derinlikler"**.

### 6.8 Bölüm 1 Hedefleri (Örnek)
- Kıyıdan ormana ulaş
- 5 totemin hepsini bul ve al (5. totem mağara girişindedir)
- Haritadaki iksir / anahtar eşyasını topla
- Mağara girişine ulaş

> **Not (v0.3):** Çöküş yalnızca Öfke 100'de gerçekleşir; yani totemsiz geçiş yoktur. Totem almak zorunludur, gerilim "hangi sırayla, kim taşır, ne kadar hızlı" kararlarındadır.

### 6.9 Bölüm 1 Görev / Bulmaca Taslağı *(taslak, değiştirilebilir)*
Mevcut mekaniklerle yapılabilen basit örnekler. `PROGRESS.md` içinde "taslak" olarak işaretlidir.
1. **İki kollu kapı:** İki oyuncu aynı anda iki ayrı kolu tutar, kapı/köprü açılır (ekip çalışması, basit netcode olayı).
2. **Mızrakla düğme:** Zehirli çiçek alanının ardındaki düğmeye mızrakla uzaktan basılır.
3. **Fener izleri:** Karanlıkta görünmeyen semboller sadece fener ışığında görünür; doğru sıra totem kapısını açar. Fener ısınması zamana karşı baskı yaratır.
4. **Taşıma görevi:** İksir/anahtar zehirli bölgeden geçirilir; ağır totemle ya da takım arkadaşının iksiriyle korunarak.

## 7. Bölüm 2 ve Sonrası (Taslak Fikirler)

| Bölüm | Tema | Ana Mekanik |
|---|---|---|
| 2 | Karanlık Mağara / Yeraltı | Ses ile dikkat çeken yaratıklar, ışıksız bölgeler |
| 3 | Terk Edilmiş Maden / Kasaba | Raylı vagon, bulmaca, yük taşıma |
| 4 | Sular Altı Tapınak | Oksijen/su seviyesi, kapı bulmacaları |
| 5 | Kayıp Goblin Kralı Mezarı | Takım boss mücadelesi |
| 6 | Zirve / Ada Çekirdeği | Final, totemlerin mührü ve kaçış |

Her bölüm: yeni ortam, yeni tehdit, yeni araç, kısa bir cinematic ve checkpoint içerir.

### 7.1 Bölüm 2: Derinlikler — mini bölüm (KULLANICI onayı, 2026-11-08)
Tema GDD 7 ile aynı: karanlık mağara, sesle dikkat çeken yaratık, ışıksız bölgeler. Yeni sistem (pil, silah vb.) yok; fener, mızrak, envanter, ses ve mevcut bulmaca yapıları kullanılır. Ekranda ipucu yazısı yok.

1. **Giriş mağarası:** Düşüşten sonra varılan oda. Kristaller fenerle birkaç saniye aydınlatılınca **şarj olur** ve bir süre kendi kendine parlar (oyuncu keşfeder). Çıkış geçidi zifiri karanlık.
2. **Karanlık geçit — "Dinleyen":** Kör, sese tepki veren yaratık. Koşmak, zıplamak ve sert iniş **gürültü** yapar; yürümek sessizdir. Gürültüyü duyan Dinleyen o noktaya yönelir, yakalarsa zehir ekler (Bölüm 1 yaratıklarıyla aynı hasar dili). Mağarada bulunan **kristal parçası** envantere alınıp elde taşınabilir: sürekli hafif parlar, çevreyi her yöne aydınlatır (fener gibi ısınmaz, yönsüz; ağır eşya değil). Takım: biri yolu aydınlatır, diğerleri sessizce geçer.
3. **Işık yansıtma odası:** Bir oyuncu fenerini **kaynak kristale** tutar; ışın kristalden çıkar. Odadaki **döndürülebilir kristaller** (E ile çevrilir) ışını yansıtır. Işın kapıdaki rüne ulaşınca kapı açılır. Tek kişi aynı anda hem fener tutup hem çeviremez → takım işi.
4. **Bölüm sonu:** Yukarı çıkan geçit, ışığa çıkış, kısa son ekranı (karartma + başlık).

**Genişletme (KULLANICI, 2026-11-09):** Bölüm 2 süre olarak Bölüm 1 kadar uzun olacak; yeni odalar mevcut sistemlerle yapılır.
- **Çöken köprü** (karanlık geçitten sonra, ışın odasından önce): uçurum üstünde sallanan tahta köprü. Yürümek güvenli (gıcırdar); çürük tahtalarda koşmak/zıplayıp inmek tahtayı kırar (gürültü → Dinleyen). Koşmak/zıplayıp inmek köprünün tamamını yıkar (bir kez; geri gelmez). Uçuruma düşen oyuncu ölür (izleyici olur). Yedek yol: yan duvarda dar, yavaş kaya patikası.
- **Karanlık labirent** (köprüden sonra): ışıksız dar geçitler, çıkmaz yollar. Üç çıkmaz sokakta taş **kristal yuvası** var; önlerinde yalnızca fener ışığında görünen 1/2/3 çizgi. Giriş mağarasındaki kristal parçaları yuvalara çizgi sırasıyla takılır (yanlış sırada takılanlar geri düşer, baştan). Üçü doğru takılınca labirent çıkışındaki taş kapı aşağıdan yukarı açılır → ışın odası. Yuvaların yanındaki şarj kristalleri yuvaya yönlendirir. (KULLANICI, 2026-11-09)
- **Okunabilir notlar (KULLANICI, 2026-11-09):** Bölüm başlarında (Bölüm 1 iskele / doğma yeri, Bölüm 2 mağara başı) parlayan kağıt not; E ile ekranda kağıt sayfası açılır. Bölüm 1: ada, totemler (etkisini kimse bilmiyor). Bölüm 2: kristaller ve kör yaratık hakkında ipuçları. Oyun içi belge; HUD ipucu yazısı değil.
- Sıra (KULLANICI): giriş → karanlık geçit → çöken köprü → karanlık labirent → ışın odası (son) → çıkış. Başka oda eklenmeyecek.

## 8. Düşmanlar ve Tehditler (Bölüm 1)
- **Hastalık çiçekleri:** Statik tehlike, yayılır.
- **Karanlıkta hayvanlar (ses ağırlıklı):** Önce sadece ses, öfke yükselince gölge, sonra fiziksel tehdit (kurt/gece kuşu benzeri yaratıklar, stilize).
- **Doğa tehlikeleri:** Çamur, kayan zemin, yıldırım düşmesi (öfke yüksekken).
- **Bölüm sonu:** Mağara çöküşü (kaçınılmaz olay).

### 8.1 Tehdit Kademesi: Ses → Gölge → Fiziksel
Korku ve performans için tehdit **üç kademede** büyür. Ucuz olan kademeler önce, pahalı olanlar en son kullanılır.

| Kademe | Öfke | Ne olur | Teknik maliyet |
|---|---|---|---|
| **1. Ses** | 20–40 | Uzaktan baykuş, uluma, dal kırılması, yaprak hışırtısı. Sadece 3D ses; ekranda hiçbir şey yok | Çok düşük (AudioSource) |
| **2. Gölge** | 60 | Sis içinde gölge siluetler, fener ışığının kenarında hareket eden şekiller, parlayan gözler. Yaklaşınca kaybolurlar | Düşük (sprite/billboard, basit mesh, ışık dışında gölge) |
| **3. Fiziksel** | 80–100 | Gerçek yaratıklar (kurt/gece kuşu benzeri stilize), mızrakla itilebilir, kovalamaca. Sayıları sınırlı (aynı anda max 2–3) | Orta (AI, animasyon, ağ senkronu) |

- Her kademede ses bir önceki kademeden **taşınır** ve yoğunlaşır (ses hiç kesilmez).
- Sahte alarm: Bazen gölge kademesinde hiçbir şey olmaz; oyuncu sürekli gerilimde kalır.
- Fiziksel yaratıklar saldırınca hasar yerine **zehir** ekler (bkz. 5.4).
- Fiziksel yaratıklar fener ısısını yükseltmez ama ışıktan kaçabilir/çekilebilir *(denge testiyle karar verilecek)*.
- Performans: Fiziksel yaratık sayısını sınırlı tutmak, ağ trafiğini de azaltır.

## 9. Ses ve Müzik
- Dinamik ses: Öfke arttıkça katmanlar eklenir (yağmur, rüzgar, hayvan sesleri, kalp atışı benzeri bas).
- 3D uzamsal ses (hayvan sesleri yönlü).
- **Proximity voice chat** (mesafeye göre ses): korku ve komedi için çok güçlü. Vize kapsamında **bonus özellik**; ilk aşamada Discord kullanılır, zaman kalırsa Vivox veya Dissonance ile eklenir.
- Müzik: Minimal ambient + gerilim anlarında davul/çello vurguları, bir miktar komik goblin temaları (kontrast).
- Goblin sesleri: Anlamsız komik "gibberish" konuşma + emoji/ping sistemi.

## 10. Multiplayer Tasarımı
- **Host-client** (host oyuncusu oturumu yönetir) veya dedicated değil, basit co-op.
- 1–4 oyuncu; solo oynanabilir (zorluk ölçekleme).
- Davet: lobi kodu veya LAN/IP (Steam yok).
- Senkronize edilecek durumlar: konum, envanter, totem durumu, Öfke değeri, hava/olay durumu, zehir değerleri, görev ilerlemesi.
- **Late join** (oyun ortasında katılma): ilk sürümde kapalı, sonra checkpoint'lerde açılabilir.

## 11. Progression ve Meta
- Bölüm bazlı ilerleme, toplanan "Altın / Hazine" ile kozmetik açılımları.
- Başarımlar (opsiyonel, ekstra puan için): "Hiç zehirlenmeden bitir", "4 kişi birlikte kay" vb.
- Opsiyonel: Zorluk seviyeleri (Kolay/Normal/Kâbus).

## 12. UI / UX
- **HUD:** Zehir/sağlık barı, 4 slot (altta), fener ısı göstergesi, görev özeti (köşede), oyuncu isimleri (takım arkadaşlarının üzerinde, mesafeyle sönükleşir).
- **Ping sistemi:** Takım arkadaşına yön/tehlike işaretleme.
- Minimal ekran müdahalesi: Atmosferi bozmayacak, diegetic (oyun-içi) göstergeler tercih edilir.
- Menü: Ana menü, lobi, karakter özelleştirme, ayarlar.

## 13. Teknik Mimari (Unity)

### 13.1 Önerilen Teknoloji
| Alan | Öneri |
|---|---|
| Unity sürümü | **Unity 6 LTS** |
| Render pipeline | **URP** (performans + görsel denge) |
| Multiplayer | **Netcode for GameObjects** + Unity Relay/Lobby *(alternatif: Mirror, Photon Fusion 2)* |
| Steam entegrasyonu | **Vize için gerekmiyor.** İleride yayın düşünülürse Steamworks.NET + Steam Transport eklenebilir. Şimdilik Unity Relay/Lobby veya LAN/IP ile host-client |
| Girdi | Input System (yeni) |
| Kamera | Cinemachine |
| Sinematik | Timeline |
| Efekt | VFX Graph / Particle System (sis, yağmur, spor) |
| Ses | Unity Audio + Audio Mixer (veya FMOD) |
| Voice chat | Discord (dışarıdan); zaman kalırsa Vivox |
| Karakter | Humanoid/Generic rig, Animator + IK (fener tutma için) |
| Sürüm kontrol | Git + Git LFS (veya Plastic SCM) |

### 13.2 Önerilen Sistemler (Script Mimarisi)
- `GameManager` : bölüm durumu, checkpoint, fail/win
- `WrathSystem` : Ada Öfkesi değeri ve eşik olayları (event tabanlı)
- `WeatherController` : yağmur, sis yoğunluğu, rüzgar, şimşek (Öfke'ye bağlı)
- `AmbientAudioController` : öfke katmanlarına göre ses karışımı
- `PlayerController` / `NetworkPlayer`
- `InventorySystem` (4 slot, ScriptableObject tabanlı `ItemData`)
- `FlashlightController` (spot ışık, ısı + cooldown)
- `PoisonSystem` + `PoisonZone` (çiçek tetikleyicileri)
- `Totem` (SafeZone tetikleyicisi, alınabilir, öfke event'i yollar)
- `CaveTrigger` + `CollapseSequence` (kayma sekansı, sahne geçişi)
- `ObjectiveSystem`
- `ChapterLoader` (additive scene yükleme)
- `ThreatDirector` (ses → gölge → fiziksel kademe)
- `GuidanceEvents` (düşen ağaç, hastalık duvarı vb.)
- `Spear` (mızrak aleti)
- Ağır eşya desteği (`ItemData.isHeavy`, hız/koşu/mızrak kısıtı)

### 13.3 Sahne Yapısı
- `MainMenu`
- `Lobby`
- `Chapter1_Intro` (tekne sinematiği)
- `Chapter1_Island` (ana oynanış)
- `Chapter1_Slide` (kayma sekansı, tünel)
- `Chapter2_...`

### 13.4 Işık, Sis ve Karanlık Tekniği
Ağır volumetrik sistem **kullanılmaz**; ucuz ve etkili bir kombinasyon tercih edilir:
- **Baked lighting:** Ada sahnesinde ortam ışığı ve gölgeler önceden pişirilir (Lightmap / Light Probe). Gece mavi-mor tonda düşük yoğunluk.
- **Sınırlı dinamik ışık:** Sadece oyuncu fenerleri (max 4 spot) + yakındaki totem ışıkları + şimşek. Diğer tüm ışıklar baked.
- **Unity Fog:** Exponential Squared sis (renk: soluk mavi-gri). Öfke arttıkça `RenderSettings.fogDensity` yükselir.
- **Sis partikülleri:** Zeminde alçak, yavaş sis kümeleri (Particle System, düşük sayıda, büyük yarı saydam sprite'lar). Kamera yakınında yumuşak solma (soft particles).
- **Yağmur:** Kamerayı takip eden tek partikül sistemi + ıslak zemin shader/efekti (opsiyonel).
- **Post-processing (URP Volume):** Vignette, hafif film grain, renk düzeltme; zehirlenirken yeşil renk kayması ve bulanıklık.
- Hastalık çiçekleri ve totemler **emissive materyal** ile parlar (ekstra ışık gerektirmez).

### 13.5 Performans Notları
- Sis ve karanlık ucuz yoldan: Fog + sınırlı ışık sayısı + baked lighting.
- Dinamik ışık sayısını sınırla (4 fener + birkaç totem ışığı).
- Çiçek/ağaç için GPU instancing, LOD, occlusion culling.
- Yağmur: tek partikül sistemi kamera etrafında takip eder (dünya çapında değil).
- Hedef: 60 FPS, okul bilgisayarı / orta seviye laptop.

## 14. Üretim Planı (Vize Projesi Kapsamı)

**Kapsam kararı:** Sadece **Bölüm 1 tam + Bölüm 2'ye geçiş sahnesi** yapılacak. Bölüm 2 ve sonrası "gelecek planı" olarak sunumda anlatılır.
**Güncelleme (KULLANICI, 2026-11-08):** Bölüm 2 kısa bir **mini bölüm** olarak yapılacak (GDD 7.1). Bölüm 3 ve sonrası hâlâ gelecek planı.

**Öncelik sırası (MoSCoW)**
| Öncelik | İçerik |
|---|---|
| **Olmazsa olmaz** | 1. şahıs hareket, fener (ısınma), 4 slot envanter, zehir bölgeleri, totem + Ada Öfkesi, yağmur/ses kademeleri, mağara olayı + kayma sekansı |
| **Olmalı** | 2–4 oyuncu online/LAN bağlantı, tekne giriş sinematiği, mızrak, 3–5 görev/bulmaca |
| **Olursa güzel** | Ragdoll bayılma, karakter özelleştirme, ping sistemi, Bölüm 2 mini sahnesi |
| **Yapılmayacak** | Steam, başarımlar, çoklu bölüm, dedicated server, late join |

**Takvim önerisi (toplam ~8–10 hafta; kendi vize tarihine göre ölç)**
| Hafta | Hedef |
|---|---|
| 1 | Proje kurulumu, Git, gri-kutu harita, 1. şahıs hareket |
| 2 | Fener + ısınma, 4 slot envanter |
| 3 | Netcode: 2–4 oyuncu bağlanma, konum ve envanter senkronu |
| 4 | Zehir sistemi, hastalık çiçekleri |
| 5 | Totem + Ada Öfkesi + hava/ses kademeleri |
| 6 | Mağara olayı, kayma sekansı, sahne geçişi |
| 7 | Tekne sinematiği, görevler, mızrak |
| 8 | Görsel cila (asset, ışık, sis), ses |
| 9–10 | Test, hata düzeltme, sunum hazırlığı |

> **İş bölümü önerisi (4 kişilikse):** 1) Oyuncu + fener + envanter, 2) Multiplayer/Netcode, 3) Dünya: hava, totem, zehir, mağara olayı, 4) Sanat/ses/UI/sinematik.

## 15. Riskler
- Multiplayer senkronizasyon karmaşıklığı (envanter, hava durumu, olaylar)
- Karanlık atmosferin oynanabilirlik ile dengesi (fazla karanlık = sıkıcı/yorucu)
- Kapsam büyümesi (çok bölüm). İlk hedef sadece Bölüm 1 ve 2
- Totem mekaniğinin oyuncuya açık anlatılmaması (fazla gizemli veya fazla belirgin olabilir)
- Düşük bütçeli ekip için 3D asset ihtiyacı (asset store + stilize kit kullanımı)

## 16. Verilen Kararlar ve Kalan Sorular

**Karar verildi**
- Okul vize projesi, yayın planı yok (Steam yok)
- Kamera: **1. şahıs**
- Silah: yok; **mızrak** çok amaçlı alet
- Tür: **kaçış + bulmaca**
- Fener: **pil yok, ısınma + cooldown**
- Ragdoll: tam fizik hareket yok, **sadece bayılma/kayma anlarında**, ağda senkronize edilmez
- Totem: **ağır eşya** (1 slot + ağırlık etkisi), haritada 5 tane, her biri +20 Öfke
- Mağara çöküşü: **Öfke 100'de** (5 totemin hepsi alınınca)
- Zehir: tek değer; dolunca bayılma, **45 sn** içinde kaldırılmazsa ölüm; fiziksel yaratıklar da zehir verir

**Varsayımlar (denge testinde değişebilir)**
- Bayılma süresi 45 sn, kaldırılınca zehir %50
- Ölen oyuncu izleyici olur; yaşayan totem güvenli alanında ~5 sn diriltmeyle döner
- Ağır eşya: hız %40 düşer, koşu ve mızrak yok
- Bölüm 1 görev taslakları (6.9)

**Kalan sorular**
1. Oyun adı ve kısa hikaye (goblinler neden adada?)
2. Hangi asset kaynakları kullanılacak (Asset Store ücretsiz paketler, Kenney vb.)?

---
*Bu doküman yaşayan bir belgedir; her toplantıdan sonra güncellenmelidir.*
