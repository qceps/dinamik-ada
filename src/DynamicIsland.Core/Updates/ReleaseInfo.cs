using System.Text.Json;

namespace DynamicIsland.Core.Updates;

/// <summary>İndirilebilir sürüm dosyası. <see cref="Sha256"/> GitHub'ın verdiği özet (küçük harf hex); yoksa null.</summary>
public sealed record ReleaseAsset(string Name, Uri DownloadUrl, long Size, string? Sha256);

public sealed record ReleaseInfo(Version Version, string Tag, Uri PageUrl, IReadOnlyList<ReleaseAsset> Assets)
{
    /// <summary>
    /// Bu kopyanın biçimine uyan dosya: "full" → <c>DinamikAda-X.Y.Z-win-x64.exe</c>,
    /// "lite" → <c>DinamikAda-X.Y.Z-win-x64-hafif.exe</c>.
    /// </summary>
    public ReleaseAsset? AssetFor(string flavor)
    {
        var lite = string.Equals(flavor, "lite", StringComparison.OrdinalIgnoreCase);
        return Assets.FirstOrDefault(a =>
            a.Name.StartsWith("DinamikAda-", StringComparison.OrdinalIgnoreCase)
            && a.Name.EndsWith(lite ? "-win-x64-hafif.exe" : "-win-x64.exe", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>GitHub "releases/latest" yanıtını çözer ve sürüm karşılaştırır (ağdan bağımsız; birim test edilir).</summary>
public static class ReleaseParser
{
    public static ReleaseInfo? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True
                || root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True)
            {
                return null;
            }

            var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
            if (!TryParseVersion(tag, out var version)
                || !Uri.TryCreate(root.GetProperty("html_url").GetString(), UriKind.Absolute, out var page))
            {
                return null;
            }

            var assets = new List<ReleaseAsset>();
            if (root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in list.EnumerateArray())
                {
                    var name = a.GetProperty("name").GetString();
                    if (name is null || !Uri.TryCreate(a.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var url)
                        || url.Scheme != Uri.UriSchemeHttps)
                    {
                        continue;
                    }

                    string? sha = null;
                    if (a.TryGetProperty("digest", out var digest) && digest.GetString() is { } d
                        && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    {
                        sha = d["sha256:".Length..].ToLowerInvariant();
                    }

                    var size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;
                    assets.Add(new ReleaseAsset(name, url, size, sha));
                }
            }

            return new ReleaseInfo(version, tag, page, assets);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>"v1.2.3" / "1.2.3" → 1.2.3 (yalnızca üç parçalı sayısal sürümler).</summary>
    public static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        var t = text?.Trim().TrimStart('v', 'V');
        if (string.IsNullOrEmpty(t) || t.Count(c => c == '.') != 2 || !Version.TryParse(t, out var v))
        {
            return false;
        }

        version = new Version(v.Major, v.Minor, v.Build);
        return true;
    }

    /// <summary>Uzaktaki sürüm yerelden yeni mi (yalnızca Major.Minor.Build karşılaştırılır).</summary>
    public static bool IsNewer(Version remote, Version current) =>
        new Version(remote.Major, remote.Minor, Math.Max(0, remote.Build))
            .CompareTo(new Version(current.Major, current.Minor, Math.Max(0, current.Build))) > 0;
}
