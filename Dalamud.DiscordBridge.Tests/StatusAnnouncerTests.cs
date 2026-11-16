using Dalamud.DiscordBridge.Model;
using Dalamud.Game.Text;
using Newtonsoft.Json;

namespace Dalamud.DiscordBridge.Tests;

public class StatusAnnouncerTests
{
    [Fact]
    public void TryBeginOnline_SecondCall_ReturnsFalse()
    {
        var gate = new StatusAnnouncer();

        Assert.True(gate.TryBeginOnline());
        Assert.False(gate.TryBeginOnline());
        Assert.True(gate.OnlineAnnounced);
    }

    [Fact]
    public void TryBeginOffline_WithoutOnline_ReturnsFalse()
    {
        var gate = new StatusAnnouncer();

        Assert.False(gate.TryBeginOffline());
        Assert.False(gate.OnlineAnnounced);
    }

    [Fact]
    public void TryBeginOffline_AfterOnline_OnlyOnce()
    {
        var gate = new StatusAnnouncer();
        gate.TryBeginOnline();

        Assert.True(gate.TryBeginOffline());
        Assert.False(gate.TryBeginOffline());
        Assert.False(gate.TryBeginOnline());
    }

    [Fact]
    public void TargetChannels_OnlyBridgedChannels()
    {
        var outbound = new DiscordChannelConfig();
        outbound.SetUnique(XivChatType.FreeCompany);
        var channels = new Dictionary<ulong, DiscordChannelConfig>
        {
            [5] = outbound,
            [3] = new() { IsContentFinder = true },
            [4] = new() { InboundChatType = XivChatType.Echo },
            [1] = new(),
        };

        Assert.Equal([3UL, 4UL, 5UL], StatusAnnouncer.TargetChannels(channels));
    }

    [Fact]
    public void TargetChannels_Empty_ReturnsEmpty()
    {
        Assert.Empty(StatusAnnouncer.TargetChannels(new Dictionary<ulong, DiscordChannelConfig>()));
    }

    [Fact]
    public void OnlineText_WithCharacter_MentionsCharacterAndVersion()
    {
        var text = StatusAnnouncer.OnlineText("2.1.0.0", "Ane Mora＠Famfrit");

        Assert.Contains("v2.1.0.0", text);
        Assert.Contains("Ane Mora＠Famfrit", text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void OnlineText_NoCharacter_SaysNotLoggedIn(string? character)
    {
        Assert.Contains("not logged in", StatusAnnouncer.OnlineText("2.1.0.0", character));
    }

    [Fact]
    public void OfflineText_MentionsVersionAndNoReaction()
    {
        var text = StatusAnnouncer.OfflineText("2.1.0.0");

        Assert.Contains("v2.1.0.0", text);
        Assert.Contains("no reaction", text);
    }

    [Fact]
    public void Configuration_WithoutPostStatusMessages_DefaultsOn()
    {
        var config = JsonConvert.DeserializeObject<Configuration>("{\"DiscordBotPrefix\":\"xl!\"}")!;

        Assert.True(config.PostStatusMessages);
    }
}
