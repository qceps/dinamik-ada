using DynamicIsland.Core.Updates;

namespace DynamicIsland.Tests;

public class UpdateTests
{
    private const string Sample = """
        {
          "tag_name": "v1.2.0",
          "html_url": "https://github.com/qceps/dinamik-ada/releases/tag/v1.2.0",
          "draft": false,
          "prerelease": false,
          "assets": [
            { "name": "DinamikAda-1.2.0-win-x64-hafif.exe", "size": 27000000,
              "browser_download_url": "https://github.com/qceps/dinamik-ada/releases/download/v1.2.0/DinamikAda-1.2.0-win-x64-hafif.exe",
              "digest": "sha256:AAAA" },
            { "name": "DinamikAda-1.2.0-win-x64.exe", "size": 80000000,
              "browser_download_url": "https://github.com/qceps/dinamik-ada/releases/download/v1.2.0/DinamikAda-1.2.0-win-x64.exe",
              "digest": "sha256:BBBB" },
            { "name": "evil.exe", "size": 1, "browser_download_url": "http://example.com/evil.exe" }
          ]
        }
        """;

    [Fact]
    public void Parses_release_and_picks_asset_by_flavor()
    {
        var info = ReleaseParser.Parse(Sample)!;
        Assert.Equal(new Version(1, 2, 0), info.Version);
        Assert.Equal("DinamikAda-1.2.0-win-x64.exe", info.AssetFor("full")!.Name);
        Assert.Equal("bbbb", info.AssetFor("full")!.Sha256);
        Assert.Equal("DinamikAda-1.2.0-win-x64-hafif.exe", info.AssetFor("lite")!.Name);
        Assert.DoesNotContain(info.Assets, a => a.Name == "evil.exe"); // https olmayan adres atılır
    }

    [Theory]
    [InlineData("""{"tag_name":"v1.2.0","html_url":"https://x.y/z","draft":true}""")]
    [InlineData("""{"tag_name":"v1.2.0","html_url":"https://x.y/z","prerelease":true}""")]
    [InlineData("""{"tag_name":"nightly","html_url":"https://x.y/z"}""")]
    [InlineData("bozuk")]
    public void Ignores_drafts_prereleases_and_bad_input(string json) => Assert.Null(ReleaseParser.Parse(json));

    [Theory]
    [InlineData("1.2.0", "1.1.0", true)]
    [InlineData("1.1.1", "1.1.0", true)]
    [InlineData("1.1.0", "1.1.0", false)]
    [InlineData("1.0.9", "1.1.0", false)]
    [InlineData("2.0.0", "1.9.9", true)]
    public void Version_comparison(string remote, string current, bool newer)
    {
        Assert.True(ReleaseParser.TryParseVersion(remote, out var r));
        Assert.True(ReleaseParser.TryParseVersion(current, out var c));
        // Derlemenin bildirdiği sürüm dört parçalıdır (1.1.0.0); yalnızca ilk üçü karşılaştırılmalı.
        Assert.Equal(newer, ReleaseParser.IsNewer(r, new Version(c.Major, c.Minor, c.Build, 0)));
    }

    [Theory]
    [InlineData("v1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("")]
    [InlineData("vX.Y.Z")]
    public void Rejects_non_semver_tags(string tag) => Assert.False(ReleaseParser.TryParseVersion(tag, out _));
}
