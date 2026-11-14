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
| + | 74 birim testi (34 + 29 söz: LRC ayrıştırma, satır bulma, önbellek, LRCLIB eşleştirme / hız sınırı / ağ hatası + 6 satır bölme + 4 gösterge durumu + 1 yazı tipi geçişi) | ✅ |

Performans (Release): duraklatılmışken CPU ~%0,03; çalarken (çubuk animasyonu) tüm çekirdeklerin ~%0,4–0,9'u; özel bellek ~100–160 MB (çoğu WPF'nin yerel grafik katmanı; yönetilen yığın ~3 MB). Donanım render, yazılım render'dan daha verimli ölçüldü. (Sözler eklendikten sonra CPU yeniden ölçülmedi.)

## 13.11.2026 — tasarım, sözler, hızlı daralma
- **Hızlı daralma:** fare çekilince bekleme 380 → 150 ms, daralma animasyonu 400 → 300 ms. Genişken imleç ayrıca 90 ms aralıkla `GetCursorPos` ile denetlenir (katmanlı pencerede `MouseLeave` kaçarsa da daralır). Gerçek Spotify ile ölçüldü: yavaş çıkışta da imlecin bir anda sıçradığı çıkışta da "hover leave" → daralma başlangıcı ~150 ms; 200 ms sonraki ekran görüntüsünde ada küçük hâldeydi.
- **Sözler:** `Core/Lyrics` — LRC ayrıştırıcı, `TrackLyrics`, `LyricsService` (LRU 48, ağ hatası 60 sn hatırlanır), `LrclibLyricsProvider` (`/api/get` → sadeleştirilmiş başlıkla `/api/search`; süresi 8 sn'den fazla farklı kayıt reddedilir; 429'da `Retry-After`). Parça değişiminden 450 ms sonra istenir, hızlı geçişte iptal edilir. Satır zamanlayıcısı bir sonraki satırın zamanına kurulur, satırı 250 ms erken gösterir. Küçük ada yalnızca çalarken satırı gösterir (344×34); geniş görünümde çalan satır (en çok 2 satır) + sonraki satır.
- **Tasarım:** kapak renginden ışıma (şarkı değişimi / geniş), ince kenar çizgisi, Display yazı tipi, sabit genişlikli süre rakamları, kapak gölgesi, yuvarlatılmış duraklat simgesi, basınca küçülen düğmeler. Pencere 432×276 (en yüksek ada 240).
- **`--render <klasör>`:** durumları ekrana çizmeden PNG'ye döker (tasarım incelemesi için; tek kopya kilidine dokunmaz).
- Gerçek Spotify ile sözler bulundu (60 satır) ve küçük / geniş adada gösterildi. Söz zamanlamasının şarkıyla uyumu kulakla doğrulanmadı (test sırasında Spotify duraklatılmıştı).

## 13.11.2026 (2) — söz bölme, yazı tipi, akıcı geçiş, ses ve medya tuşları
- **Söz bölme (`LyricSplitter`):** satır sığmıyorsa önce noktalamadan, olmazsa kelimeleri en geniş parçayı küçültecek şekilde (küçük DP) böler; süre harf sayısına göre paylaştırılır, satırdan sonra uzun ara varsa en fazla `1 + harf/7` sn'ye yayılır. Küçük ada tek satır (≈256 DIP), geniş ada iki satır (≈585 DIP) kapasiteyle ayrı ayrı bölünür.
- **Küçük ada yazısı:** iki yuvalı çapraz geçiş (eski yukarı kayıp söner 260–360 ms, yeni aşağıdan 560 ms Quintic). Söz yoksa / duraklatılınca / söz arasında "şarkı adı · sanatçı". Küçük ada artık parça varken hep aynı genişlikte (duraklatınca daralmıyor). Kullanıcı "çok geniş" deyince daraltıldı: küçük ada 344 → 280 → 250, şarkı değişimi 340 → 310, ses göstergesi 240 → 216, oynat/duraklat 200 → 180, sonra kullanıcı isteğiyle geniş görünüm de 376 → 340 (ses kaydırıcısı 104 → 82, oynat düğmesi aralıkları 22 → 12).
- **Geniş ada sözleri:** tüm satırlar alt alta (Canvas içinde — StackPanel 74 px'e yerleştirilince WPF kaydırmadan önce kırpıyordu), çalan satır yaylı kaydırmayla (680 ms) üste gelir, geçmiş satırlar %16, gelecek %34. Saydamlık maskesi `MappingMode=Absolute` (göreli maske uzun listenin sınırlarına yayılıp çalan satırı soluk gösteriyordu).
- **Söz düğmesi:** geniş görünümün sağ üstünde (çalma göstergesinin altında) sözleri aç/kapat düğmesi; ayarlardaki "Zamanlanmış şarkı sözlerini göster" seçeneğini değiştirir (kalıcı, ayar penceresi de güncellenir). Kapalıyken soluk, açıkken dolu çip. Demo rozeti kapağın köşesine taşındı.
- **Yazı tipi:** kullanıcı iPhone kalın yazısını istedi. SF Pro'nun lisansı Windows uygulamasına koymaya izin vermediği için en yakın açık lisanslı **Inter Display** (v4.1, Bold + SemiBold, SIL OFL 1.1, lisans metni gömülü) kullanıcı onayıyla indirilip `Assets/Fonts`'a gömüldü; sözler, başlıklar ve gösterge yazısı Inter. Sanatçı/albüm gibi ikincil yazılar Segoe UI Variable Text (gömülü Inter'in normal ağırlığı yok). Ayarlar şema sürümü 2: eski varsayılan (Segoe) Inter'e taşınır, bilerek seçilen diğer yazı tipleri korunur. Seçenekler: Inter, Segoe UI Variable, Segoe UI Black, Bahnschrift, Franklin Gothic.
- **Spotify sesi:** WASAPI oturumları (`AppVolume`, işlem adı `Spotify`, tüm etkin çıkış aygıtları). Dikkat: `ISimpleAudioVolume` IID'si `87CE5498-68D6-44E5-9215-6DA47EF883D8` (yanlış IID ile tüm oturumlarda E_NOINTERFACE). Sürüklerken istekler tek işçide birleştirilir; genişken 1,2 sn'de bir okunur. Gerçek ortamda: okuma %100, %80'e ayarlayıp geri alma, tekerlekle 2 adım aşağı (çubuk ~%10 kısaldı) / 2 adım yukarı → %100.
- **Göstergeler:** `SystemVolumeWatcher` (IAudioEndpointVolume bildirimi, varsayılan aygıt değişince yeniden bağlanır) ve aynı parçada Çalıyor↔Duraklatıldı geçişi (300 ms doğrulama). Klavye kancası yok. Gerçek ortamda: ses tuşu aşağı → "%98", yukarı → "%100"; oynat/duraklat göstergeleri doğru yönde göründü (test başında Spotify çalıyordu).
- Gerçek Spotify'da söz bulunamayan parçada küçük adanın "şarkı adı · sanatçı" gösterdiği doğrulandı. Çalarken gerçek sözlerin parça parça akışı ve geçişlerin akıcılığı gözle değerlendirilmedi; demo görüntüleriyle (`--render`) doğrulandı.

## 14.11.2026 — tema ve performans
- **Tema (Ayarlar → Tema):** Siyah + kapak rengi (varsayılan), Tam siyah (ışıma yok, çubuklar beyaz), Şarkıya göre renkli (arka plan kapak renginden HSL ile koyu iki tona boyanır: l=0,24 → 0,085; şarkı değişince 650 ms geçiş). Opaklık ayarı her temada geçerli.
- **Performans ölçümleri** (demo modu, küçük ada + sözler çalarken, 15 sn, işlemin kendi CPU süresi / 12 çekirdek). Makinedeki arka plan yükü yüzünden aynı yapılandırma turdan tura %1,1–1,9 arasında oynadı; küçük farklar güvenilir değil.
  - Pencere yüksekliğini adaya göre küçültmek (katmanlı yüzey küçülsün diye) denendi: CPU **arttı** (~%0,8 → ~%1,2–1,5) ve bellek büyüdü; geri alındı.
  - `BitmapCache`: ölçülebilir fayda yok; geri alındı.
  - Çubuklar 24 → 15 fps: ölçülebilir fark yok; 24 kaldı.
  - `DropShadowEffect` kapalıyken iki turda da daha düşük CPU ve ~50 MB daha az özel bellek → ada ve kapak gölgesi, üst üste binen yarı saydam katmanlarla (efektsiz) yeniden yapıldı. Son ölçüm: CPU ~%1,0, özel bellek 141–156 MB (gürültü yüzünden kesin kazanç iddia edilemez).
  - Başlangıç (demo): işlem başlangıcından ada görünene ~1,8 sn; bunun ~1,15 sn'si uygulama kodundan önce (.NET + WPF + tek dosya paketi). Spotify bağlantısı artık tepsi/kısayol hazırlığıyla paralel başlıyor. Günlüğe "Ada ilk kez gösterildi (… ms)" yazılıyor.
- **Daha çabuk tepki:** fareyle genişleme gecikmesi 120 → 90 ms, büyüme animasyonu 460 → 400 ms, daralma 300 → 280 ms, katman geçişleri 200/90 → 180/80 ms.
- Birim testleri artık gerçek uygulama günlüğüne değil `%TEMP%\DinamikAda-test-logs`'a yazıyor (bilerek hata üreten testler kullanıcı günlüğünde "Şarkı sözü alınamadı" uyarısı bırakıyordu).

## 14.11.2026 (2) — küçük paket, sürükleme, GitHub Actions
- **Paket ölçümleri** (exe boyutu / demo modunda ada görünene kadar, iki tur):
  | Biçim | Boyut | Açılış |
  |---|---|---|
  | Eski: tek dosya, .NET dahil, ReadyToRun | 251 MB | ~1,8–1,9 sn |
  | Tek dosya, .NET dahil, sıkıştırılmış, ReadyToRun | 96 MB | 1,56–1,75 sn |
  | **Tek dosya, .NET dahil, sıkıştırılmış** (seçilen) | **79 MB** | 1,40–1,74 sn |
  | .NET gerektiren, ReadyToRun | 27 MB | **açılmıyor**: coreclr.dll içinde 0xC0000602 (bu bilgisayarda) |
  | **.NET gerektiren** (seçilen "hafif") | **27 MB** | 0,97–1,26 sn |

  Hafif sürümün 27 MB'ının çoğu Windows SDK (WinRT) projeksiyonu; WPF kırpmayı (trimming) desteklemediği için küçültülmedi. Kullanıcının kopyası sıkıştırılmış sürüme geçti: gerçek modda ada 1,32 sn'de göründü.
- **Sürükleyerek taşıma:** yalnızca Ayarlar penceresi açıkken (kullanıcı isteği). Adanın boş yerinden tutulur (düğmeler, ilerleme ve ses çubuğu kendi tıklamalarını alır), 4 px eşikten sonra sürükleme başlar, fark ekran koordinatlarından hesaplanır; pencere canlı taşınır, bırakınca `HorizontalOffset`/`TopMargin` kaydedilir. Ortadan 12 DIP içinde ortaya yapışır, üst mesafe 0–80. Ayarlar açıkken oyun modunda da tıklanabilir olur ve ince çerçeve görünür; sürüklerken fareyle genişleme bekletilir. Gerçek ekranda fareyle denenmedi.
- **GitHub Actions:** `ci.yml` (main push + PR: Release derleme + testler), `release.yml` (`v*` etiketi: testler, iki yayın, `gh release create` ile Releases'e yükleme; `github.token`, `contents: write`). Sürüm numarası etiketten (`-p:Version`). `Directory.Build.props` sürümü 1.1.0.

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
