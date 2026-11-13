namespace DynamicIsland.Core.Lyrics;

/// <summary>Zaman damgalı tek söz satırı. Boş metin = enstrümantal ara.</summary>
public sealed record LyricLine(TimeSpan Time, string Text)
{
    public bool IsGap => string.IsNullOrWhiteSpace(Text);
}

/// <summary>Bir parçanın zamanlanmış (senkron) sözleri. Satırlar zamana göre sıralıdır.</summary>
public sealed class TrackLyrics
{
    public TrackLyrics(IEnumerable<LyricLine> lines)
    {
        // Kararlı sıralama: aynı zamanlı satırlar kaynaktaki sırasını korur.
        Lines = lines.Select((line, i) => (line, i))
            .OrderBy(x => x.line.Time)
            .ThenBy(x => x.i)
            .Select(x => x.line)
            .ToArray();
    }

    public IReadOnlyList<LyricLine> Lines { get; }

    public bool IsEmpty => Lines.Count == 0 || Lines.All(l => l.IsGap);

    /// <summary>Verilen konumda geçerli satırın sırası; ilk satırdan önce -1.</summary>
    public int IndexAt(TimeSpan position)
    {
        int lo = 0, hi = Lines.Count - 1, found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (Lines[mid].Time <= position)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return found;
    }

    /// <summary>Geçerli satırdan sonraki ilk satırın başlangıç zamanı (yoksa null).</summary>
    public TimeSpan? NextChangeAfter(TimeSpan position)
    {
        var next = IndexAt(position) + 1;
        return next < Lines.Count ? Lines[next].Time : null;
    }
}
