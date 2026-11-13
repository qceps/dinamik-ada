namespace DynamicIsland.Core.Lyrics;

/// <summary>
/// Uzun söz satırlarını, verilen genişliğe sığan parçalara böler ve satırın süresini parçalara harf sayısına
/// göre paylaştırır. Böylece satır "…" ile kesilmek yerine sıra sıra gösterilir.
/// Önce noktalama (virgül vb.) sınırlarından bölmeyi dener; olmazsa kelimeleri dengeli gruplara ayırır.
/// </summary>
public static class LyricSplitter
{
    /// <summary>Yavaş söylemede saniyedeki yaklaşık harf: satırdan sonra uzun bir ara varsa parçalar en fazla bu kadar yayılır.</summary>
    private const double CharsPerSecond = 7;

    /// <param name="lyrics">Kaynak sözler.</param>
    /// <param name="measure">Bir metnin genişliği (tek satır, DIP).</param>
    /// <param name="capacity">Bir parçanın sığması gereken toplam genişlik.</param>
    public static TrackLyrics Split(TrackLyrics lyrics, Func<string, double> measure, double capacity)
    {
        if (capacity <= 0)
        {
            return lyrics;
        }

        var cache = new Dictionary<string, double>(StringComparer.Ordinal);
        double Measure(string s)
        {
            if (!cache.TryGetValue(s, out var w))
            {
                w = measure(s);
                cache[s] = w;
            }

            return w;
        }

        var result = new List<LyricLine>(lyrics.Lines.Count);
        for (var i = 0; i < lyrics.Lines.Count; i++)
        {
            var line = lyrics.Lines[i];
            if (line.IsGap || Measure(line.Text) <= capacity)
            {
                result.Add(line);
                continue;
            }

            var chunks = Chunk(line.Text, Measure, capacity);
            if (chunks.Count <= 1)
            {
                result.Add(line);
                continue;
            }

            var totalChars = chunks.Sum(c => c.Length);
            var estimate = TimeSpan.FromSeconds(1 + totalChars / CharsPerSecond);
            // Sonraki satıra kadar olan süre; uzun bir ara varsa (enstrümantal) parçaları tahmini söyleme süresine yay.
            var gap = i + 1 < lyrics.Lines.Count ? lyrics.Lines[i + 1].Time - line.Time : estimate;
            var duration = gap <= TimeSpan.Zero ? TimeSpan.FromSeconds(0.1 * chunks.Count) : (gap < estimate ? gap : estimate);

            var before = 0;
            foreach (var chunk in chunks)
            {
                var offset = TimeSpan.FromTicks((long)(duration.Ticks * (before / (double)totalChars)));
                result.Add(new LyricLine(line.Time + offset, chunk));
                before += chunk.Length;
            }
        }

        return new TrackLyrics(result);
    }

    internal static List<string> Chunk(string text, Func<string, double> measure, double capacity)
    {
        // 1) Noktalama sınırları: her parça sığıyorsa, sığdığı sürece komşularla birleştir.
        var clauses = SplitClauses(text);
        if (clauses.Count > 1 && clauses.All(c => measure(c) <= capacity))
        {
            var merged = new List<string>();
            var current = clauses[0];
            for (var i = 1; i < clauses.Count; i++)
            {
                var candidate = current + " " + clauses[i];
                if (measure(candidate) <= capacity)
                {
                    current = candidate;
                }
                else
                {
                    merged.Add(current);
                    current = clauses[i];
                }
            }

            merged.Add(current);
            return merged;
        }

        // 2) Kelimeleri, en geniş parçayı en aza indirecek şekilde n gruba böl (n: sığana kadar artar).
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 1)
        {
            return [text];
        }

        var space = Math.Max(0, measure("a a") - 2 * measure("a"));
        var widths = words.Select(measure).ToArray();
        var minGroups = (int)Math.Ceiling(measure(text) / capacity);
        for (var n = Math.Max(2, minGroups); n <= words.Length; n++)
        {
            var groups = Balance(widths, space, n);
            var chunks = groups.Select(g => string.Join(' ', words[g.Start..g.End])).ToList();
            if (chunks.All(c => measure(c) <= capacity) || n == words.Length)
            {
                return chunks;
            }
        }

        return [text];
    }

    private static List<string> SplitClauses(string text)
    {
        var clauses = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if ((c is ',' or ';' or ':' or '!' or '?' or '.') && i + 1 < text.Length && text[i + 1] == ' ')
            {
                clauses.Add(text[start..(i + 1)].Trim());
                start = i + 1;
            }
        }

        var tail = text[start..].Trim();
        if (tail.Length > 0)
        {
            clauses.Add(tail);
        }

        return clauses.Where(c => c.Length > 0).ToList();
    }

    /// <summary>Kelime genişliklerini ardışık n gruba, en geniş grubu küçültecek şekilde böler (küçük DP).</summary>
    private static List<(int Start, int End)> Balance(double[] widths, double space, int n)
    {
        var count = widths.Length;
        var prefix = new double[count + 1];
        for (var i = 0; i < count; i++)
        {
            prefix[i + 1] = prefix[i] + widths[i];
        }

        double Width(int a, int b) => prefix[b] - prefix[a] + space * (b - a - 1);

        // best[k, i]: ilk i kelimeyi k gruba bölmenin en küçük "en geniş grup" değeri.
        var best = new double[n + 1, count + 1];
        var cut = new int[n + 1, count + 1];
        for (var k = 0; k <= n; k++)
        {
            for (var i = 0; i <= count; i++)
            {
                best[k, i] = double.PositiveInfinity;
            }
        }

        best[0, 0] = 0;
        for (var k = 1; k <= n; k++)
        {
            for (var i = k; i <= count; i++)
            {
                for (var j = k - 1; j < i; j++)
                {
                    var value = Math.Max(best[k - 1, j], Width(j, i));
                    if (value < best[k, i])
                    {
                        best[k, i] = value;
                        cut[k, i] = j;
                    }
                }
            }
        }

        var groups = new List<(int, int)>();
        var end = count;
        for (var k = n; k >= 1; k--)
        {
            var start = cut[k, end];
            groups.Add((start, end));
            end = start;
        }

        groups.Reverse();
        return groups;
    }
}
