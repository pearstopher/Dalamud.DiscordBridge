namespace Dalamud.DiscordBridge.Tests;

public class ReactionPathTests
{
    [Fact]
    public void ReactAsync_UsesTheGatewayCacheBeforeFetching()
    {
        // Regression (#50): every reaction re-fetched the message over REST first (two calls instead of one).
        var source = File.ReadAllText(Path.Combine(DocumentationTests.Root, "Dalamud.DiscordBridge", "DiscordHandler.cs"));
        var react = source[source.IndexOf("internal async Task ReactAsync(", StringComparison.Ordinal)..];
        react = react[..react.IndexOf("internal async Task ReplyAsync(", StringComparison.Ordinal)];

        var cached = react.IndexOf("GetCachedMessage(messageId)", StringComparison.Ordinal);
        var fetched = react.IndexOf("GetMessageAsync(messageId)", StringComparison.Ordinal);
        Assert.True(cached > 0 && cached < fetched, "ReactAsync must try the gateway cache first");
    }

    [Fact]
    public void GatewayCache_IsBigEnoughForTheInboundQueue()
    {
        var source = File.ReadAllText(Path.Combine(DocumentationTests.Root, "Dalamud.DiscordBridge", "DiscordHandler.cs"));
        var match = System.Text.RegularExpressions.Regex.Match(source, @"MessageCacheSize = (\d+)");

        Assert.True(match.Success);
        Assert.True(int.Parse(match.Groups[1].Value) >= 20);
    }
}
