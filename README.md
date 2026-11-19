# Son Hasat

![Son Hasat](docs/kapak.png)

**1–4 oyunculu, LAN üzerinden oynanan, birinci şahıs gerilim ve bulmaca oyunu.**
Unity 6 (URP) ve Netcode for GameObjects ile geliştirildi.

Tekneniz sisli bir adanın kıyısına çarptı. Dört korkuluk, zehirli çiçeklerin, öfkeli totemlerin ve karanlığın arasında
yalnızca ellerindeki fenerle hayatta kalmaya çalışıyor. Ada sizi yer altına, oradan da kimsenin uyanamadığı bir rüyaya sürüklüyor.

<p>
<img src="docs/ekran_1_dusus.jpg" width="49%"> <img src="docs/ekran_3_ruya_koyu.jpg" width="49%">
<img src="docs/ekran_4_tekne.jpg" width="49%"> <img src="docs/ekran_5_magara.jpg" width="49%">
</p>

**itch.io:** https://d3xs.itch.io/son-hasat

## İndir ve oyna

1. [Releases](../../releases/latest) sayfasından `SonHasat-Windows.zip` dosyasını indir.
2. Zip'i bir klasöre çıkar ve `Son Hasat.exe`'yi çalıştır.
3. **Oyna** → bir oyuncu **Host Ol**'a basar; diğerleri host'un lobide yazan IP adresini girip **Katıl**'a basar
   (aynı ağda olmalısınız; varsayılan port 7777).
4. Herkes gelince host **Oyunu Başlat**'a basar. Host, başlat düğmesinin altından başlangıç bölümünü de seçebilir.

Sistem: Windows 10/11 (64 bit), DirectX 11/12 destekli ekran kartı. Tek başına da oynanabilir.

## Bölümler

| Bölüm | Ad | Özet |
|---|---|---|
| 1 | **Solgun Topraklar** | Sisli ada. Zehir saçan çiçekler, beş totem, ısınan bir fener ve giderek öfkelenen bir ada. Totemleri toplayın, birbirinizi ayakta tutun. |
| 2 | **Derinlikler** | Adanın altındaki mağara. Işığı kristallerle yansıtın, çöken köprüyü ve karanlık labirenti aşın; sesinize gelen "Dinleyen"e yakalanmayın. |
| 3 | **Düşler** | Gökten düşerek girdiğiniz, uykunun bastırdığı eğri bir mantar köyü. Fener burada nesneleri büyütüp küçültür. Kilitli yatakları bulmacalarla açın; herkes yatmadan kimse uyanamaz. |

## Kontroller

| Tuş | İşlev |
|---|---|
| W A S D / Fare | Hareket / bakış |
| Shift | Koş |
| Space | Zıpla |
| C | Eğil |
| E | Etkileşim (al, kullan, kaldır, oku) |
| F | Feneri aç / kapat |
| R | Fener modu: normal → büyüten → küçülten (Bölüm 3) |
| Sol tık | Mızrak |
| 1–4 | Envanter yuvası |
| Q | Eşyayı bırak |
| Esc | Menü |

## Kaynak kodu açmak

- **Unity 6000.3.26f1** (Unity 6 LTS), URP.
- Büyük dosyalar Git LFS ile tutulur: klonlamadan önce `git lfs install`.
- Sahneler: `Assets/_Project/Scenes` (MainMenu → Lobby → Ch1_Island → Ch2_Transition → Ch3_Dream). Oyunu `MainMenu`'den başlatın.
- Kodlar `Assets/_Project/Scripts` altında; Bölüm 3'ün sahne üreticileri `Tools/Son Hasat/...` menüsünde.

### Asset Store paketi

Bölüm 2'deki dev yaratık modeli Unity Asset Store'daki **Kevin Iglesias – Humanoid Giant** paketinden geliyor.
Asset Store lisansı gereği bu depoya konmadı. Paketi kendi hesabınızdan içe aktarırsanız (`Assets/Kevin Iglesias`)
proje eksiksiz açılır; aktarmazsanız yalnızca o model eksik görünür. Hazır build'de model vardır.

## Geliştirme notları

`GDD.md` oyun tasarım belgesi, `PROJECT.md` teknik kararlar, `PROGRESS.md` görev günlüğüdür.
