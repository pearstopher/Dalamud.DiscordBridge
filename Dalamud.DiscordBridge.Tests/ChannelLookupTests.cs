namespace Dalamud.DiscordBridge.Tests;

public class ChannelLookupTests
{
    [Fact]
    public void Classify_Found_IsFound()
    {
        Assert.Equal(ChannelLookupResult.Found, ChannelLookup.Classify(found: true, threw: false, null));
    }

    [Fact]
    public void Classify_NullWithoutError_IsNotFound()
    {
        // Discord.Net returns null only when Discord answered 404.
        Assert.Equal(ChannelLookupResult.NotFound, ChannelLookup.Classify(found: false, threw: false, null));
    }

    [Fact]
    public void Classify_UnknownChannelError_IsNotFound()
    {
        Assert.Equal(ChannelLookupResult.NotFound, ChannelLookup.Classify(false, true, ChannelLookup.UnknownChannelCode));
    }

    [Theory]
    [InlineData(null)]      // network error, timeout
    [InlineData(50001)]     // Missing Access (bot removed from server, permissions)
    [InlineData(50013)]     // Missing Permissions
    [InlineData(0)]         // General error / outage
    public void Classify_OtherErrors_AreErrors(int? code)
    {
        Assert.Equal(ChannelLookupResult.Error, ChannelLookup.Classify(false, true, code));
    }

    [Theory]
    [InlineData("Found", false)]
    [InlineData("Error", false)]
    [InlineData("NotFound", true)]
    public void ShouldForget_OnlyWhenDefinitelyGone(string result, bool expected)
    {
        Assert.Equal(expected, ChannelLookup.ShouldForget(Enum.Parse<ChannelLookupResult>(result)));
    }

    [Fact]
    public void WarnOnce_WarnsOncePerChannelUntilReset()
    {
        var warn = new WarnOnce();

        Assert.True(warn.ShouldWarn(1));
        Assert.False(warn.ShouldWarn(1));
        Assert.True(warn.ShouldWarn(2));
        warn.Reset(1);
        Assert.True(warn.ShouldWarn(1));
    }

    [Fact]
    public void Senders_OnlyRemoveChannelsThroughTheLookupRule()
    {
        // Regression (#39): a transient lookup failure used to delete the channel's whole config.
        var source = File.ReadAllText(Path.Combine(DocumentationTests.Root, "Dalamud.DiscordBridge", "DiscordHandler.cs"));
        var removals = System.Text.RegularExpressions.Regex.Matches(source, @"ChannelConfigs\.Remove\(").Count;

        Assert.Equal(1, removals);
        Assert.Contains("if (ChannelLookup.ShouldForget(lookup))", source);
    }
}
