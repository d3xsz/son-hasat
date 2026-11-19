# CLAUDE.md

Proje: **Son Hasat** (eski adı: Sisli Ada: Goblin Seferi; Bölüm 1: Sisli Ada, Bölüm 2: Derinlikler) (Unity 6 LTS, URP, Netcode for GameObjects, C#). Unity MCP ile geliştiriliyor.

## Her oturumun başında (zorunlu)
1. Sırayla oku: `PROJECT.md`, `GDD.md`, `PROGRESS.md`.
2. `PROGRESS.md` içinde "Şu an yapılıyor" doluysa önce o yarım işi bitir. Boşsa "Sıradaki görev"i al.

## Çalışma kuralları
- **Tek seferde tek görev.** Görev bitince dur, sonrakine kendiliğinden geçme.
- Göreve başlamadan önce `PROGRESS.md` > "Şu an yapılıyor" kısmına ne yapacağını yaz.
- Bitince: görevi `[x]` yap, "Son yapılanlar"a 1–3 satır ekle (hangi dosya/sahne/script, nasıl test edilir, bilinen sorun), "Sıradaki görev"i güncelle, "Şu an yapılıyor"u boşalt.
- Oturum aniden kesilebilir. Uzun bir görevin ortasında "Şu an yapılıyor"a neyin bittiğini ve nerede kaldığını yaz.
- `PROJECT.md` ve `GDD.md` kararlarını **değiştirme**. Çelişki veya eksik görürsen uydurma, `PROGRESS.md` > "Sorular"a yaz ve kullanıcıya sor.
- Yeni mekanik ekleme (pil, silah, Steam, volumetrik sis vb. yok). Sadece görev kapsamındakini yap.
- Görsel için gri kutu / primitive / placeholder kullan. Hazır model ve ses kullanıcı tarafından eklenecek.
- Her görev sonunda: Console'da hata yok, Play modunda test edildi. Ağ görevlerinde 2 oyuncuyla test (Multiplayer Play Mode veya ParrelSync).
- Görev bitince kısa bir commit mesajı öner.
- Yanıtlar kısa olsun; ne yaptığını ve nasıl test edeceğini yaz.
