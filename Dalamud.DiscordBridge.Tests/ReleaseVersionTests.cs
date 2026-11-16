using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Dalamud.DiscordBridge.Tests;

/// <summary>
/// The release workflow checks tag vX.Y.Z against the csproj &lt;Version&gt; X.Y.Z.0. These tests catch a bad version
/// before tagging.
/// </summary>
public class ReleaseVersionTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dalamud.DiscordBridge.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static Version CsprojVersion()
    {
        var csproj = XDocument.Load(Path.Combine(RepoRoot(), "Dalamud.DiscordBridge", "Dalamud.DiscordBridge.csproj"));
        var text = csproj.Descendants("Version").Single().Value.Trim();
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+\.0$"), text);
        return Version.Parse(text);
    }

    [Fact]
    public void CsprojVersion_IsFourPartEndingInZero()
    {
        Assert.Equal(0, CsprojVersion().Revision);
    }

    [Fact]
    public void CsprojVersion_IsNotOlderThanPublishedRepoJson()
    {
        var repoJson = Path.Combine(RepoRoot(), "repo.json");
        if (!File.Exists(repoJson))
            return; // Nothing released yet; the first release creates repo.json.

        using var doc = JsonDocument.Parse(File.ReadAllText(repoJson));
        var published = Version.Parse(doc.RootElement[0].GetProperty("AssemblyVersion").GetString()!);

        Assert.True(CsprojVersion() >= published, $"csproj version {CsprojVersion()} is older than published {published}.");
    }

    [Fact]
    public void Manifest_HasIdentityFields()
    {
        var manifestPath = Path.Combine(RepoRoot(), "Dalamud.DiscordBridge", "DiscordChatPears.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = doc.RootElement;

        Assert.Equal("Discord Chat Pears", root.GetProperty("Name").GetString());
        Assert.Equal("DiscordChatPears", root.GetProperty("InternalName").GetString());
        Assert.StartsWith("https://", root.GetProperty("IconUrl").GetString());
    }
}
