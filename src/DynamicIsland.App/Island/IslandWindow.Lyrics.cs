using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.Core.Lyrics;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.State;

namespace DynamicIsland.App.Island;

/// <summary>
/// Sözler. Uzun satırlar "…" ile kesilmez: her görünüm için sığacak parçalara bölünür ve sırayla gösterilir
/// (<see cref="LyricSplitter"/>). Küçük ada iki yuva arasında yumuşak çapraz geçiş yapar; geniş ada tüm satırları
/// alt alta tutar ve çalan satırı yaylı bir kaydırmayla üste getirir. Söz yoksa / duraklatılınca / satır arasında
/// küçük ada şarkı adını ve sanatçıyı gösterir.
/// </summary>
public partial class IslandWindow
{
    /// <summary>Sözler bir tık erken gösterilsin (geçiş animasyonu + Spotify konumunun gecikmesi).</summary>
    private static readonly TimeSpan LyricLead = TimeSpan.FromMilliseconds(250);

    private const double CompactFontSize = 13;
    private const double ExpandedFontSize = 16;
    private const double ExpandedLineGap = 7;
    private const double LyricTopPad = 6;
    private const double DimOpacity = 0.34;
    private const double PassedOpacity = 0.16;

    // Bölme kapasiteleri (DIP): küçük ada metin alanı tek satır; geniş ada iki satır (kaydırma payı ile).
    private const double CompactCapacity = 250 - 38 - 40 - 6;
    private const double ExpandedCapacity = (340 - 36) * 2 * 0.86;

    private readonly DispatcherTimer _lyricTimer;
    private readonly List<TextBlock> _expandedLines = [];
    private readonly List<double> _lineTops = [];
    private double[] _lineOpacity = [];
    private TrackLyrics? _rawLyrics;
    private TrackLyrics? _compactLyrics;
    private TrackLyrics? _expandedLyrics;
    private int _compactIndex = int.MinValue;
    private int _expandedIndex = int.MinValue;
    private string? _compactContentKey;
    private bool _compactFrontIsA = true;
    /// <summary>Uygulamaya gömülü Inter Display (Bold/SemiBold); bulunamazsa Segoe UI Variable.</summary>
    internal static readonly FontFamily InterDisplay = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Inter Display, Segoe UI Variable Display");

    private FontFamily _lyricFamily = InterDisplay;
    private FontWeight _lyricWeight = FontWeights.Bold;

    private bool HasLyrics => _expandedLyrics is not null && _snapshot.Timeline is not null;

    /// <summary>Geçerli parçanın zamanlanmış sözleri (yoksa / kapalıysa null).</summary>
    public void SetLyrics(TrackLyrics? lyrics)
    {
        if (ReferenceEquals(lyrics, _rawLyrics))
        {
            return;
        }

        _rawLyrics = lyrics;
        RebuildLyricLayouts();
        OnContentLayoutChanged();
    }

    public void SetLyricFont(LyricsFontChoice choice)
    {
        (_lyricFamily, _lyricWeight) = choice switch
        {
            LyricsFontChoice.SegoeBlack => (new FontFamily("Segoe UI"), FontWeights.Black),
            LyricsFontChoice.Bahnschrift => (new FontFamily("Bahnschrift, Segoe UI"), FontWeights.SemiBold),
            LyricsFontChoice.FranklinGothic => (new FontFamily("Franklin Gothic, Segoe UI"), FontWeights.Medium),
            LyricsFontChoice.SegoeVariableBold => (new FontFamily("Segoe UI Variable Display, Segoe UI"), FontWeights.Bold),
            _ => (InterDisplay, FontWeights.Bold),
        };

        if (Resources["LyricFont"] is FontFamily current && current.Source == _lyricFamily.Source
            && Resources["LyricWeight"] is FontWeight weight && weight == _lyricWeight)
        {
            return;
        }

        Resources["LyricFont"] = _lyricFamily;
        Resources["LyricWeight"] = _lyricWeight;
        RebuildLyricLayouts();
        OnContentLayoutChanged();
    }

    /// <summary>Teşhis: söz satırını hemen yeniden hesapla (animasyonsuz).</summary>
    internal void RefreshLyricsNow()
    {
        _compactIndex = _expandedIndex = int.MinValue;
        _compactContentKey = null;
        UpdateLyrics(animate: false);
    }

    private void RebuildLyricLayouts()
    {
        _compactLyrics = _rawLyrics is null ? null : LyricSplitter.Split(_rawLyrics, s => MeasureText(s, CompactFontSize), CompactCapacity);
        _expandedLyrics = _rawLyrics is null ? null : LyricSplitter.Split(_rawLyrics, s => MeasureText(s, ExpandedFontSize), ExpandedCapacity);

        LyricsStack.Children.Clear();
        _expandedLines.Clear();
        _lineTops.Clear();
        _compactIndex = _expandedIndex = int.MinValue;
        _compactContentKey = null;
        if (_expandedLyrics is null)
        {
            _lineOpacity = [];
            return;
        }

        foreach (var line in _expandedLyrics.Lines)
        {
            var block = new TextBlock
            {
                Text = line.IsGap ? "♪" : line.Text,
                FontSize = ExpandedFontSize,
                FontFamily = _lyricFamily,
                FontWeight = _lyricWeight,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, ExpandedLineGap),
                Opacity = DimOpacity,
                IsHitTestVisible = false,
            };
            TextOptions.SetTextFormattingMode(block, TextFormattingMode.Ideal);
            _expandedLines.Add(block);
            LyricsStack.Children.Add(block);
        }

        _lineOpacity = Enumerable.Repeat(DimOpacity, _expandedLines.Count).ToArray();
        LyricsScroll.BeginAnimation(TranslateTransform.YProperty, null);
        LyricsScroll.Y = LyricTopPad;
    }

    private double MeasureText(string text, double size)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(_lyricFamily, FontStyles.Normal, _lyricWeight, FontStretches.Normal), size, Brushes.White, dpi);
        return formatted.WidthIncludingTrailingWhitespace;
    }

    private bool LyricsOnScreen =>
        _shown && HasLyrics && (_visual == IslandVisual.Expanded || (_visual == IslandVisual.Compact && CompactShowsText));

    private void UpdateLyrics(bool animate = true)
    {
        if (!HasLyrics)
        {
            _lyricTimer.Stop();
            UpdateCompactContent(animate);
            return;
        }

        var position = _snapshot.Timeline!.PositionAt(DateTimeOffset.UtcNow, _snapshot.IsPlaying) + LyricLead;
        _compactIndex = _compactLyrics!.IndexAt(position);
        UpdateCompactContent(animate);

        var expandedIndex = _expandedLyrics!.IndexAt(position);
        if (expandedIndex != _expandedIndex)
        {
            _expandedIndex = expandedIndex;
            ScrollExpanded(expandedIndex, animate && _animations && _shown && _visual == IslandVisual.Expanded);
        }

        // Bir sonraki satır (veya parça) zamanına kur; uzun aralarda en geç 1 sn'de bir yeniden eşitle.
        TimeSpan? next = null;
        foreach (var lyrics in new[] { _compactLyrics, _expandedLyrics })
        {
            if (lyrics.NextChangeAfter(position) is { } t && (next is null || t < next))
            {
                next = t;
            }
        }

        if (LyricsOnScreen && _snapshot.IsPlaying && next is { } n)
        {
            _lyricTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp((n - position).TotalMilliseconds + 15, 30, 1000));
            _lyricTimer.Stop();
            _lyricTimer.Start();
        }
        else
        {
            _lyricTimer.Stop();
        }
    }

    // ---- Küçük ada: çalan parça ya da şarkı adı · sanatçı ----

    private void UpdateCompactContent(bool animate)
    {
        var track = _snapshot.Track;
        if (track is null)
        {
            return;
        }

        string key;
        string? lyric = null;
        if (HasLyrics && _snapshot.IsPlaying && _compactIndex >= 0 && !_compactLyrics!.Lines[_compactIndex].IsGap)
        {
            lyric = _compactLyrics.Lines[_compactIndex].Text;
            key = "L" + _compactIndex.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            key = "I" + track.Key;
        }

        if (key == _compactContentKey)
        {
            return;
        }

        _compactContentKey = key;
        var (front, frontShift, back, backShift) = _compactFrontIsA
            ? (CompactTextA, CompactShiftA, CompactTextB, CompactShiftB)
            : (CompactTextB, CompactShiftB, CompactTextA, CompactShiftA);

        back.Inlines.Clear();
        if (lyric is not null)
        {
            back.Inlines.Add(new Run(lyric));
        }
        else
        {
            back.Inlines.Add(new Run(track.Title));
            back.Inlines.Add(new Run("  ·  ") { Foreground = (Brush)FindResource("TertiaryText") });
            back.Inlines.Add(new Run(ArtistText(track))
            {
                // Gömülü Inter yalnızca kalın; sanatçı adı sistemin normal yazısıyla.
                Foreground = (Brush)FindResource("SecondaryText"),
                FontFamily = (FontFamily)FindResource("BodyFont"),
                FontWeight = FontWeights.Normal,
            });
        }

        _compactFrontIsA = !_compactFrontIsA;
        var visible = animate && _animations && _shown && _visual == IslandVisual.Compact && CompactShowsText;
        if (!visible)
        {
            foreach (var (text, shift, opacity) in new[] { (back, backShift, 1.0), (front, frontShift, 0.0) })
            {
                text.BeginAnimation(OpacityProperty, null);
                shift.BeginAnimation(TranslateTransform.YProperty, null);
                text.Opacity = opacity;
                shift.Y = 0;
            }

            return;
        }

        // Eski yazı hafifçe yukarı kayıp söner; yenisi aşağıdan yükselerek belirir. Yavaşlayan eğriler: sarsıntısız.
        var outEase = new SineEase { EasingMode = EasingMode.EaseIn };
        var inEase = new QuinticEase { EasingMode = EasingMode.EaseOut };
        front.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)) { EasingFunction = outEase }, HandoffBehavior.SnapshotAndReplace);
        frontShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-10, TimeSpan.FromMilliseconds(360)) { EasingFunction = outEase }, HandoffBehavior.SnapshotAndReplace);
        back.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)) { BeginTime = TimeSpan.FromMilliseconds(90), EasingFunction = inEase });
        backShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(560)) { EasingFunction = inEase });
    }

    // ---- Geniş ada: kayan söz listesi ----

    private void EnsureLineTops()
    {
        if (_expandedLines.Count > 0 && _expandedLines[0].ActualHeight <= 0)
        {
            LyricsBlock.UpdateLayout();
        }

        _lineTops.Clear();
        var y = 0.0;
        foreach (var line in _expandedLines)
        {
            _lineTops.Add(y);
            y += line.ActualHeight + line.Margin.Bottom;
        }
    }

    private void ScrollExpanded(int index, bool animate)
    {
        if (_expandedLines.Count == 0)
        {
            return;
        }

        EnsureLineTops();
        var target = LyricTopPad - (index >= 0 && index < _lineTops.Count ? _lineTops[index] : 0);
        if (animate)
        {
            // Hafif yaylı kaydırma: satır yerine "oturur", sert durmaz.
            var scroll = new DoubleAnimation(target, TimeSpan.FromMilliseconds(680))
            {
                EasingFunction = new SpringEase { Damping = 0.9, Stiffness = 6.2 },
            };
            LyricsScroll.BeginAnimation(TranslateTransform.YProperty, scroll, HandoffBehavior.SnapshotAndReplace);
        }
        else
        {
            LyricsScroll.BeginAnimation(TranslateTransform.YProperty, null);
            LyricsScroll.Y = target;
        }

        for (var i = 0; i < _expandedLines.Count; i++)
        {
            var desired = i == index
                ? (_expandedLyrics!.Lines[i].IsGap ? 0.75 : 1.0)
                : i < index ? PassedOpacity : DimOpacity;
            if (Math.Abs(_lineOpacity[i] - desired) < 0.001)
            {
                continue;
            }

            _lineOpacity[i] = desired;
            if (animate)
            {
                var fade = new DoubleAnimation(desired, TimeSpan.FromMilliseconds(i == index ? 420 : 520))
                {
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                _expandedLines[i].BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
            }
            else
            {
                _expandedLines[i].BeginAnimation(OpacityProperty, null);
                _expandedLines[i].Opacity = desired;
            }
        }
    }
}
