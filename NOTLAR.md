# Proje notu (13.11.2026)

## Son çalışan durum
- Ana uygulama (C#/.NET 10 LTS + WPF) gerçek Spotify ile çalışıyor; self-contained tek dosya yayın `publish\DinamikAda-win-x64\DinamikAda.exe` (~250 MB, ReadyToRun) .NET kurulu olmayan ortamda başlatılıp doğrulandı.
- Katmanlar: `Core` (GSMTC/Spotify, ayarlar, durum, yerleşim), `Interop` (Win32), `App` (WPF ada, tepsi, ayarlar). Teşhis: `tools/MediaProbe`; uçtan uca test betikleri: `tools/test`.

## Gerçekten yapılan testler
| # | Test | Sonuç |
|---|---|---|
| 1 | Spotify açıkken şarkı + kapak | ✅ gerçek parça ve kapak gösterildi |
| 2 | Şarkı değişimi ve 3× hızlı geçiş | ✅ son parçada doğru durdu, ara karelerde bozulma yok |
| 3 | Duraklat/devam/önceki + ileri sarma (adanın düğmeleriyle) | ✅ Spotify durumu değişti; çubuğa tıklama %50'ye sardı |
| 4 | Spotify'ı kapatıp yeniden açma | ✅ "Spotify kapalı" → otomatik yeniden bağlanma |
| 5 | Başka uygulamada medya (Brave canlı yayın aktif oturum) | ✅ Spotify gösterilmeye devam etti |
| 6 | Uzun başlık ve eksik kapak (demo) | ✅ "…" ile kesme, nota yer tutucusu |
| 7 | Küçük ↔ geniş geçişler | ✅ ekran görüntüleriyle; akıcılık gözle (kare kare değil) değerlendirildi |
| 8 | Odak çalmama | ✅ Not Defteri/sahte oyun ön planda kaldı (hover, kısayol, şarkı değişimi, tıklama) |
| 9 | Oyun modunda tıklama geçirme + kısayollar | ✅ sahte kenarlıksız tam ekran ve TopMost tam ekran pencereyle |
| 10 | Exclusive fullscreen | ❌ test edilmedi (gerçek oyun/test uygulaması çalıştırılmadı) |
| 11 | Çoklu monitör / DPI | ✅ ikinci monitöre (negatif koordinat) taşıma; ❗ farklı DPI yalnızca birim testinde (iki monitör de %100) |
| 12 | Yeniden açılışta ayar korunması | ✅ UI Automation ile |
| + | Kısayol çakışması, Windows ile başlat, tek kopya, saydam alanların tıklama geçirmesi | ✅ |
| + | 34 birim testi | ✅ |

Performans (Release): duraklatılmışken CPU ~%0,03; çalarken (çubuk animasyonu) tüm çekirdeklerin ~%0,4–0,9'u; özel bellek ~100–160 MB (çoğu WPF'nin yerel grafik katmanı; yönetilen yığın ~3 MB). Donanım render, yazılım render'dan daha verimli ölçüldü.

## Bilinen kısıtlar
- Exclusive fullscreen oyunlarda normal pencere görünmeyebilir; doğrulanmadı.
- Windows "Animasyon efektleri" sistem ayarı değiştirilerek test edilmedi (yalnızca uygulama içi anahtar).
- Yalnızca bu bilgisayardaki Spotify masaüstü uygulaması; başka cihazdaki çalma okunmaz.
- Tek dosya boyutu büyük (~250 MB); WinForms yalnızca tepsi için.
- Bir kez, imleç kullanıcı tarafından oynatıldığı tahmin edilen anda beklenmedik daralma gözlendi; yeniden üretilemedi (`DINAMIKADA_TRACE=1` ile izlenebilir).

## Kalan işler
- Xbox Game Bar pencere öğesi (kullanıcı isteğiyle ertelendi). Araştırma: SDK `Microsoft.Gaming.XboxGameBar` 7.3.2607010; UWP XAML uygulaması, `microsoft.gameBarUIExtension` AppExtension, proxy-stub kaydı, `globalMediaControl` kısıtlı yeteneği; AppContainer'da GSMTC olayları çalışmadığı için `SpotifyMediaSource(usePolling: true)` hazır. Kurulum Geliştirici Modu veya güvenilen imza sertifikası gerektirir.
- Exclusive fullscreen ve farklı DPI'lı ikinci monitör testi.
- İsteğe bağlı: daha küçük paket (çerçeveye bağımlı yayın), MSIX.
