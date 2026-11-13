using System.Globalization;
using System.Text.RegularExpressions;

namespace DynamicIsland.Core.Lyrics;

/// <summary>
/// LRC biçimini ayrıştırır: <c>[mm:ss.xx]metin</c>, bir satırda birden çok damga, <c>[offset:±ms]</c>.
/// Meta etiketleri (<c>[ar:…]</c>, <c>[ti:…]</c>) ve kelime düzeyi damgalar (<c>&lt;mm:ss.xx&gt;</c>) yok sayılır.
/// </summary>
public static partial class LrcParser
{
    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex TimeTag();

    [GeneratedRegex(@"^\s*\[offset:\s*([+-]?\d+)\s*\]\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetTag();

    [GeneratedRegex(@"<\d{1,3}:\d{1,2}(?:[.:]\d{1,3})?>")]
    private static partial Regex WordTag();

    public static TrackLyrics? Parse(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc))
        {
            return null;
        }

        var offset = TimeSpan.Zero;
        var lines = new List<LyricLine>();
        foreach (var raw in lrc.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var offsetMatch = OffsetTag().Match(line);
            if (offsetMatch.Success && int.TryParse(offsetMatch.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var ms))
            {
                // LRC standardı: pozitif offset sözleri ERKEN gösterir.
                offset = TimeSpan.FromMilliseconds(-ms);
                continue;
            }

            var stamps = new List<TimeSpan>();
            var position = 0;
            while (true)
            {
                var match = TimeTag().Match(line, position);
                if (!match.Success || match.Index != position)
                {
                    break;
                }

                stamps.Add(ToTime(match));
                position = match.Index + match.Length;
            }

            if (stamps.Count == 0)
            {
                continue;
            }

            var text = WordTag().Replace(line[position..], string.Empty).Trim();
            foreach (var stamp in stamps)
            {
                lines.Add(new LyricLine(stamp, text));
            }
        }

        if (lines.Count == 0)
        {
            return null;
        }

        if (offset != TimeSpan.Zero)
        {
            lines = lines.Select(l => l with { Time = l.Time + offset < TimeSpan.Zero ? TimeSpan.Zero : l.Time + offset }).ToList();
        }

        var lyrics = new TrackLyrics(lines);
        return lyrics.IsEmpty ? null : lyrics;
    }

    private static TimeSpan ToTime(Match match)
    {
        var minutes = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var fraction = 0.0;
        if (match.Groups[3].Success)
        {
            var digits = match.Groups[3].Value;
            fraction = int.Parse(digits, CultureInfo.InvariantCulture) / Math.Pow(10, digits.Length);
        }

        return TimeSpan.FromSeconds(minutes * 60 + seconds + fraction);
    }
}
