using System.Text.Json;
using ItTalksTTS.Core.Services;

namespace ItTalksTTS.Tests;

public class UpdateCheckServiceTests
{
    [Fact]
    public void FindSetupAssetUrl_reads_installer_asset()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "tag_name": "v0.1.2",
              "assets": [
                { "name": "README.txt", "browser_download_url": "https://example.com/readme" },
                { "name": "ItTalksTTS-Setup.exe", "browser_download_url": "https://example.com/setup.exe" }
              ]
            }
            """);

        Assert.Equal("https://example.com/setup.exe", UpdateCheckService.FindSetupAssetUrl(doc.RootElement));
    }
}
