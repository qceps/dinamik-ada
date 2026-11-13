# Dinamik Ada — Spotify

Ekranın üst ortasında duran, Spotify masaüstü uygulamasında çalan şarkıyı ve zamanlanmış sözlerini gösteren Dynamic Island tarzı kapsül. Hesap girişi, API anahtarı veya Premium gerekmez: parça bilgisi Windows medya oturumundan (GSMTC), sözler LRCLIB'den alınır.

## Kurulum
1. `publish\DinamikAda-win-x64\DinamikAda.exe` dosyasını istediğiniz klasöre kopyalayın (tek dosya, .NET kurulumu gerekmez, yönetici yetkisi istemez).
2. Çalıştırın. Görev çubuğunda pencere açılmaz; simge **sistem tepsisinde** görünür.
3. Windows ile başlatmak isterseniz Ayarlar → "Windows ile başlat" (varsayılan kapalı).

Kaynaktan derlemek için (.NET 10 SDK):
```bash
dotnet publish src/DynamicIsland.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true -o publish/DinamikAda-win-x64
```

## Kullanım
| Durum | Ne zaman |
|---|---|
| Küçük | Varsayılan: kapak + ortada çalan söz satırı; söz yoksa, duraklatılınca veya söz arasında şarkı adı · sanatçı (ayardan kapatılırsa yalnızca kapak + çalma göstergesi) |
| Şarkı değişimi | Yeni parça başlayınca birkaç saniye başlık ve sanatçı |
| Geniş | Fareyle üzerine gelince veya **Ctrl+Shift+F9**: kapak, kayan sözler (sağ üstteki 💬 düğmesiyle açılıp kapanır), süre, ilerleme, önceki / oynat-duraklat / sonraki, **Spotify ses seviyesi** (kaydırıcı, sessize alma, fare tekerleği). Fare çekilince ~150 ms sonra daralır |
| Bekleme | Spotify kapalı / oturum yok / müzik yok (ayardan gizlenebilir) |
| Gösterge | Sistem sesi değişince (klavyenin ses tuşları vb.) ses çubuğu; Spotify duraklatılınca / devam edince kısa "Duraklatıldı / Çalıyor". Geniş görünümü bölmez, ~1,6 sn sonra kapanır |

- İlerleme çubuğuna tıklayarak sarma yalnızca Spotify destekliyorsa açıktır.
- Çubuk animasyonu dekoratiftir; ses verisini analiz etmez.
- **Ctrl+Shift+F10**: adayı göster/gizle. **Ctrl+Shift+F11**: etkileşim modu.
- Tepsi simgesine sol tık: Ayarlar. Sağ tık: menü (Çıkış dahil).

### Şarkı sözleri
Sözler [LRCLIB](https://lrclib.net)'ten alınır (ücretsiz, anahtar gerektirmez). Bunun için çalan parçanın **adı, sanatçısı, albümü ve süresi** bu hizmete gönderilir. Yalnızca zamanlanmış (senkron) sözler gösterilir; küçük bir bellek önbelleğinde tutulur, diske ve günlüğe yazılmaz. Uzun satırlar "…" ile kesilmez: adaya sığan parçalara bölünüp sırayla gösterilir (mümkünse virgülden; satırın süresi parçalara paylaştırılır). Ayarlar → "Şarkı sözleri": aç/kapat, küçük adada göster, yazı tipi (varsayılan **Inter Display** kalın — iPhone'un kalın yazısına en yakın açık lisanslı yazı tipi, uygulamaya gömülü; ya da Segoe UI Variable, Segoe UI Black, Bahnschrift, Franklin Gothic). Her parçanın sözü bulunmayabilir; zamanlama Spotify'ın Windows'a bildirdiği konuma dayanır.

### Ses ve medya tuşları
- Geniş adadaki ses kaydırıcısı **yalnızca Spotify'ın** Windows ses karıştırıcısındaki seviyesini değiştirir; sistem sesi değişmez. Spotify o oturumda hiç ses çalmadıysa kaydırıcı soluk görünür.
- Klavye **dinlenmez**: ses tuşlarının sonucu Windows ses API'sinden (sistem sesi değişti), oynat/duraklat ise Spotify'ın medya oturumundan anlaşılır. Tuşlar Spotify'a ve Windows'a aynen ulaşmaya devam eder; Windows'un kendi ses penceresi de görünür. Ayarlar → Genel'den kapatılabilir.

### Oyun modu
Varsayılan "Tam ekran algılanınca": ön plandaki uygulama ekranı kaplıyorsa ada tıklamaları alttaki oyuna geçirir ve fareyle genişlemez. Kontrollere tıklamak için **Ctrl+Shift+F11** ile etkileşim modunu açın (ince çerçeve görünür; 8 sn hareketsizlikte kendiliğinden kapanır). Masaüstü ve kenarlıksız tam ekranda test edildi; **özel (exclusive) tam ekran oyunlarda normal pencereler görünmeyebilir** — bu senaryo doğrulanmadı.

### Ayarlar
Tema (Siyah + kapak rengi, Tam siyah, Şarkıya göre renkli), şarkı sözleri (aç/kapat, küçük adada göster, yazı tipi), medya göstergesi, monitör, yatay konum, üst mesafe, boyut, arka plan opaklığı, animasyonlar, şarkı değişiminde açık kalma süresi, Spotify kapalıyken davranış, oyun modu, kısayollar, konumu varsayılana döndürme, çıkış. Ayarlar `%APPDATA%\DinamikAda\settings.json` içinde saklanır.

### Diğer
- `DinamikAda.exe --demo`: sahte verilerle görsel demo (Spotify'a bağlanmaz, "DEMO" rozeti).
- `DinamikAda.exe --render <klasör>`: geliştirici aracı; adanın tüm durumlarını demo verisiyle PNG olarak döker ve çıkar (ekrana çizmez, açık kopyayı etkilemez).
- Günlük: `%LOCALAPPDATA%\DinamikAda\logs\app.log` (şarkı/sanatçı bilgisi yazılmaz).
- Uygulama tek kopya çalışır; ikinci kez açmak ayarları öne getirir.

### Üçüncü taraf
- [Inter](https://github.com/rsms/inter) v4.1 (Inter Display Bold / SemiBold) — © The Inter Project Authors, SIL Open Font License 1.1. Lisans metni: `src/DynamicIsland.App/Assets/Fonts/Inter-OFL-LICENSE.txt` (uygulamaya da gömülüdür).
