namespace Dalamud.DiscordBridge.Tests;

/// <summary>Regression (#45): help sent fork users to the upstream maintainers' wiki and Discord.</summary>
public class ForkLinksTests
{
    [Fact]
    public void PluginSource_NeverLinksToUpstreamOrItsDiscord()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(DocumentationTests.Root, "Dalamud.DiscordBridge"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("reiichi001", source);
            Assert.DoesNotContain("discord.gg", source);
        }
    }

    [Fact]
    public void HelpLinks_PointToThisFork()
    {
        Assert.StartsWith("https://github.com/pearstopher/", Constant.HelpLink);
        Assert.StartsWith("https://github.com/pearstopher/", Constant.KindListLink);
        Assert.StartsWith("https://github.com/pearstopher/", Constant.IssuesLink);
    }
}
