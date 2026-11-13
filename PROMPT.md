# Bu projeyi yeniden üretmek için istem (prompt)

Aşağıdaki istem, bu oturumda şu ana kadar yapılan her şeyi tek seferde yeniden yaptırmak için yazılmıştır.

---

Windows 11 için, Spotify masaüstü uygulamasına odaklı, iPhone "Dynamic Island" tarzında bir masaüstü uygulaması geliştir. Sadece plan verme; projeyi oluştur, derle, çalıştır ve gerçek ortamda test et.

## Ortam ve teknoloji
- Klasör boşsa sıfırdan başla. .NET SDK yoksa resmî `dotnet-install.ps1` betiğiyle **.NET 10 LTS SDK**'yı yönetici yetkisi istemeden `%USERPROFILE%\.dotnet` altına kur (sürümü `releases-index.json`'dan doğrula).
- C# / .NET 10 / **WPF**. TFM: `net10.0-windows10.0.22621.0`, `SupportedOSPlatformVersion` 10.0.19041.0.
- Çözüm yapısı:
  - `src/DynamicIsland.Core` — WPF'den bağımsız ortak katman: GSMTC Spotify medya kaynağı, modeller, ayarlar (JSON), saf durum çözücü, yerleşim hesabı, gizlilik dostu günlük.
  - `src/DynamicIsland.Interop` — tüm Win32 P/Invoke'lar (LibraryImport; geri çağrılı olanlar DllImport): pencere stilleri, SetWindowPos, monitör/DPI, RegisterHotKey, SetWinEventHook, tam ekran tespiti, SPI_GETCLIENTAREAANIMATION.
  - `src/DynamicIsland.App` — WPF uygulaması (`DinamikAda.exe`), tepsi için yalnızca WinForms NotifyIcon. `app.manifest`: PerMonitorV2, asInvoker.
  - `tools/MediaProbe` — konsol teşhis/test aracı (`sessions`, `watch`, `events`, `next`, `prev`, `playpause`, `seek`); kendi günlüğünü %TEMP%'e yazar.
  - `tests/DynamicIsland.Tests` — xUnit birim testleri.
  - `tools/test/*.ps1` — fare/klavye simülasyonu, ekran görüntüsü, UI Automation ile uçtan uca test betikleri.

## Spotify bağlantısı (Core)
- `GlobalSystemMediaTransportControlsSessionManager` ile **tüm** oturumları tara; yalnızca uygulama kimliği `Spotify.exe` (Win32) veya `SpotifyAB.SpotifyMusic_…` (Store) olanı kullan. "Aktif oturum"a güvenme: tarayıcıdaki video Spotify'ın yerini almasın.
- Olay tabanlı: `SessionsChanged`, `MediaPropertiesChanged`, `PlaybackInfoChanged`, `TimelinePropertiesChanged`. Olayları 60 ms'lik pencerede birleştiren tek bir arka plan işçisi (kirli bayraklar + Interlocked). UI iş parçacığını asla bloke etme.
- Sorgulama yalnızca: oturum yokken 4 sn'de bir (kapalı/oturum yok ayrımı için), 30 sn'lik güvenlik eşitlemesi ve isteğe bağlı polling modu (AppContainer için).
- Durumlar ayrı: `SpotifyNotRunning` (işlem yok), `NoSession` (işlem var oturum yok), `Ready` + `PlaybackStatus` (Playing/Paused/Stopped/Changing). Başlıksız oturum = "müzik yok".
- Kapak: `DataReader` ile bayt olarak oku (≤8 MB), FNV-1a hash, 24'lük LRU önbellek; parça değişiminden 0,9 ve 2,5 sn sonra yeniden okuyup farklıysa güncelle (Spotify'ın geç/eski kapak sorunu). Hızlı geçişte önceki denetimi iptal et.
- Zaman çizelgesi: `EndTime-StartTime`, geçersiz `LastUpdatedTime` için okuma anını kullan, çalarken yerelde ilerlet. Yetenekler (`IsNextEnabled`, `IsPlaybackPositionEnabled` …) kontrollere aynen yansısın.
- Komutlar 3 sn zaman aşımıyla; hatalarda oturumu bırakıp yeniden ara, asla çökme. Spotify yeniden açılınca kendiliğinden toparlan.
- Günlük (`%LOCALAPPDATA%\DinamikAda\logs\app.log`, 512 KB döner) şarkı/sanatçı/albüm **yazmaz**. `DINAMIKADA_TRACE=1` ile yalnızca durum geçişi izleri.

## Ada görünümü (App)
- Sabit boyutlu (432×208 DIP × ölçek), `AllowsTransparency`, çerçevesiz, görev çubuğunda yok, `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST`, `WM_MOUSEACTIVATE → MA_NOACTIVATE`, `ShowActivated=False`. Saydam pikseller tıklamayı alttaki pencereye geçirir.
- İçteki siyah kapsülün yalnızca genişlik/yüksekliği animasyonlu; köşe yarıçapı `min(h/2, 30)`, kırpma geometrisi ve ayrı gölge plakası (DropShadow). Opaklık ayarı plaka+ada için `a = 1-√(1-opaklık)`.
- Durumlar: Bekleme 150×30 ("Spotify kapalı / Spotify hazır / Müzik yok"), Küçük 172×34 (kapak + çubuklar), Şarkı değişimi 330×54 (kapak, başlık, sanatçı), Geniş 376×176 (66 px kapak, başlık/sanatçı/albüm, süre + ilerleme çubuğu, önceki/oynat-duraklat/sonraki) — zaman çizelgesi yoksa 376×154 ve ilerleme gizli. Uzun metinler `CharacterEllipsis`.
- Katmanlar sabit boyutlu ve ortalı; geçişte yalnızca opaklık çapraz geçişi. Boyut animasyonu: özel `SpringEase` (sönüm 0,84 büyürken / 0,95 küçülürken, ~%1 aşma), 480/400 ms, `HandoffBehavior.SnapshotAndReplace` (hızlı geçişte birikme yok).
- Çubuklar dekoratif (gerçek spektrum değil): 4 çubuk, yalnızca çalarken + görünürken + katman etkinken, 24 fps. Renk kapaktan çıkarılan vurgu rengi (doygunluk ağırlıklı ortalama, HSL ile okunur parlaklığa çekilmiş).
- İleri sarma yalnızca `CanSeek` ve zaman çizelgesi varsa etkileşimli. İlerleme zamanlayıcısı (500 ms) yalnızca geniş + çalarken çalışır.
- Animasyonlar ayardan kapatılabilir; Windows "Animasyon efektleri" kapalıysa otomatik kapanır.

## Davranış (IslandController)
- Saf çözücü: Gizli > (parça yoksa: Bilinmiyor→gizli, ayara göre Bekleme/Gizli) > Geniş (hover/kısayol/etkileşim) > Şarkı değişimi > Küçük.
- Yeni parça anahtarında "şarkı değişimi" açılır, ayarlı süre (varsayılan 4 sn) sonra küçülür; hızlı geçişte zamanlayıcı yeniden başlar.
- Hover: 140 ms bekleyip genişle, 380 ms sonra daral. Kısayol ile genişlet/daralt.
- Oyun modu (Kapalı / Açık / Tam ekran algılanınca — varsayılan): ön plan değişimi WinEvent ile, 0,9 ve 3,5 sn sonra yeniden denetim; tam ekran = `SHQueryUserNotificationState` (D3D/busy) veya ön plan penceresi monitörü kaplıyor. Oyun modunda `WS_EX_TRANSPARENT` (tıklama geçer), hover kapalı. Etkileşim modu kısayolla açılır (ince halka göstergesi), tıklanabilir olur, 8 sn hareketsizlikte kendiliğinden kapanır.
- Her ön plan değişiminde `SetWindowPos(HWND_TOPMOST, NOACTIVATE)` ile en üstte kalmayı yeniden doğrula.
- Yerleşim: `PlacementCalculator` — seçili monitör (aygıt adı; çıkarılmışsa birincile düş, ayarı silme), monitör DPI'ı, ortadan kaydırma (ada ekran içinde kalacak şekilde kırpılır), üst boşluk. `WM_DPICHANGED`, `WM_DISPLAYCHANGE`, `DisplaySettingsChanged`, uykudan dönüşte yeniden konumla + medya yeniden eşitle.

## Kısayollar, tepsi, ayarlar
- Gizli HWND_MESSAGE penceresine `RegisterHotKey` (MOD_NOREPEAT). Varsayılanlar: Ctrl+Shift+F9 genişlet, Ctrl+Shift+F10 göster/gizle, Ctrl+Shift+F11 etkileşim (Ctrl+Alt, Türkçe klavyede AltGr ile çakıştığı için kullanılmadı). Durumlar: Etkin / Kapalı / başka uygulamayla çakışma / kendi içinde çakışma / geçersiz; çakışmada tepsi bildirimi + ayarlarda kırmızı uyarı. Kısayol yakalanırken genel kısayollar askıya alınır.
- Tepsi menüsü (Türkçe): Ayarlar, Adayı göster, Etkileşim modu, Oyun modu durumu, Çıkış. Sol tık ayarları açar.
- Ayar penceresi: WPF Fluent teması (`ThemeMode="System"`), Türkçe: Windows ile başlat (varsayılan kapalı; yalnızca kullanıcı değiştirince HKCU\Run'a yaz/sil), fareyle genişleme, Spotify kapalıyken davranışı, monitör, yatay konum, üst mesafe, boyut %80–140, arka plan opaklığı %40–100, animasyonlar, şarkı değişiminde açık kalma süresi, oyun modu + durum, üç kısayol kutusu, konumu varsayılana döndür, günlük klasörü, uygulamadan çık. Onay kutuları Checked/Unchecked ile (UI Automation uyumlu). Değişiklikler canlı uygulanır, 400 ms gecikmeyle atomik JSON'a (`%APPDATA%\DinamikAda\settings.json`) kaydedilir; bozuk dosya `.bozuk` olarak yedeklenip varsayılana dönülür.
- Tek kopya: `Local\` mutex; ikinci açılış mevcut kopyada ayarları öne getirir.
- `--demo`: ayrı sahte kaynak (uzun başlıklı ve kapaksız parçalar dahil), "DEMO" rozeti; gerçek Spotify bağlantısı bu modda hiç oluşturulmaz. `--settings`: açılışta ayarlar.
- İşlenmeyen UI hataları günlüğe yazılır, uygulama kapanmaz.

## Doğrulama (gerçekten yapılanlar)
- 34 birim testi (kimlik eşleşmesi, durum çözücü, çoklu monitör/150% DPI/negatif koordinat/kırpma yerleşimi, zaman çizelgesi, LRU, ayar gidiş-dönüş ve bozuk dosya).
- Gerçek Spotify ile: parça/kapak gösterimi; tek ve 3× hızlı geçiş (son parçada doğru durdu); adanın düğmeleriyle duraklat/devam/önceki; ilerleme çubuğuna tıklayarak %50'ye sarma; Spotify'ı öldürüp yeniden açma (otomatik toparlanma); Brave'de canlı yayın aktif oturumken Spotify'ın gösterilmeye devam etmesi.
- Not Defteri ön plandayken genişleme/şarkı değişimi/tıklamalarda odak hiç değişmedi.
- Sahte kenarlıksız tam ekran (ve TopMost tam ekran) pencereyle: oyun modu otomatik açıldı, tıklamalar oyuna geçti, hover genişletmedi, etkileşim modunda kontrol çalıştı ve tıklama oyuna gitmedi, 8 sn zaman aşımı, gizle/göster; ada TopMost pencerenin de üstünde kaldı.
- Normal modda adanın çevresindeki saydam alanlar tıklamayı alttaki uygulamaya geçirdi, ada geçirmedi.
- UI Automation ile: ikinci monitöre taşıma, çıkış, yeniden açılışta korunma, varsayılana döndürme, Windows ile başlat (kayıt defteri yaz/sil), kısayol çakışması raporu.
- Yayın: `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true` → .NET kurulu olmadan çalıştı; duraklatılmışken CPU ~%0,03.

## Kapsam dışı / bekleyen
- Xbox Game Bar pencere öğesi araştırıldı (SDK 7.3.2607010: UWP XAML uygulaması, `microsoft.gameBarUIExtension` AppExtension, proxy-stub kaydı, `globalMediaControl` kısıtlı yeteneği, AppContainer'da GSMTC olayları çalışmadığından polling) ama kullanıcı isteğiyle şimdilik bırakıldı. Gerçek "exclusive fullscreen" testi yapılmadı.
