# Dinamik Ada — Spotify

Ekranın üst ortasında duran, Spotify masaüstü uygulamasında çalan şarkıyı gösteren Dynamic Island tarzı kapsül. Hesap girişi, API anahtarı veya Premium gerekmez: bilgi Windows medya oturumundan (GSMTC) okunur.

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
| Küçük | Varsayılan: kapak + çalma göstergesi |
| Şarkı değişimi | Yeni parça başlayınca birkaç saniye başlık ve sanatçı |
| Geniş | Fareyle üzerine gelince veya **Ctrl+Shift+F9**: kapak, süre, ilerleme, önceki / oynat-duraklat / sonraki |
| Bekleme | Spotify kapalı / oturum yok / müzik yok (ayardan gizlenebilir) |

- İlerleme çubuğuna tıklayarak sarma yalnızca Spotify destekliyorsa açıktır.
- Çubuk animasyonu dekoratiftir; ses verisini analiz etmez.
- **Ctrl+Shift+F10**: adayı göster/gizle. **Ctrl+Shift+F11**: etkileşim modu.
- Tepsi simgesine sol tık: Ayarlar. Sağ tık: menü (Çıkış dahil).

### Oyun modu
Varsayılan "Tam ekran algılanınca": ön plandaki uygulama ekranı kaplıyorsa ada tıklamaları alttaki oyuna geçirir ve fareyle genişlemez. Kontrollere tıklamak için **Ctrl+Shift+F11** ile etkileşim modunu açın (ince çerçeve görünür; 8 sn hareketsizlikte kendiliğinden kapanır). Masaüstü ve kenarlıksız tam ekranda test edildi; **özel (exclusive) tam ekran oyunlarda normal pencereler görünmeyebilir** — bu senaryo doğrulanmadı.

### Ayarlar
Monitör, yatay konum, üst mesafe, boyut, arka plan opaklığı, animasyonlar, şarkı değişiminde açık kalma süresi, Spotify kapalıyken davranış, oyun modu, kısayollar, konumu varsayılana döndürme, çıkış. Ayarlar `%APPDATA%\DinamikAda\settings.json` içinde saklanır.

### Diğer
- `DinamikAda.exe --demo`: sahte verilerle görsel demo (Spotify'a bağlanmaz, "DEMO" rozeti).
- Günlük: `%LOCALAPPDATA%\DinamikAda\logs\app.log` (şarkı/sanatçı bilgisi yazılmaz).
- Uygulama tek kopya çalışır; ikinci kez açmak ayarları öne getirir.
