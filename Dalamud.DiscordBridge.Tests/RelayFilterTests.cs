using Dalamud.DiscordBridge.Model;
using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge.Tests;

public class RelayFilterTests
{
    private static DiscordChannelConfig Channel(params XivChatType[] types)
    {
        var channel = new DiscordChannelConfig();
        channel.SetUnique(types);
        return channel;
    }

    [Fact]
    public void Subscribed_IsWanted_Unsubscribed_IsNot()
    {
        var channels = new[] { Channel(XivChatType.FreeCompany) };

        Assert.True(RelayFilter.Wants(channels, XivChatType.FreeCompany));
        Assert.False(RelayFilter.Wants(channels, XivChatType.Say));
        Assert.False(RelayFilter.Wants(channels, (XivChatType)2105)); // battle log noise
    }

    [Fact]
    public void NoChannels_WantsNothing()
    {
        Assert.False(RelayFilter.Wants([], XivChatType.Say));
    }

    [Fact]
    public void OutgoingTell_FollowsTheTellSubscription()
    {
        Assert.True(RelayFilter.Wants([Channel(XivChatType.TellIncoming, XivChatType.TellOutgoing)], XivChatType.TellOutgoing));
        Assert.True(RelayFilter.Wants([Channel(XivChatType.TellIncoming)], XivChatType.TellOutgoing));
        // Same as SendChatEvent: outgoing tells are posted as tells, so an outgoing-only list never got them.
        Assert.False(RelayFilter.Wants([Channel(XivChatType.TellOutgoing)], XivChatType.TellOutgoing));
    }

    [Theory]
    [InlineData(85, XivChatType.FreeCompany)]   // GM FC
    [InlineData(80, XivChatType.TellIncoming)]  // GM tell
    [InlineData(86, XivChatType.Ls1)]           // GM LS1
    public void GmLines_FollowTheirPlayerChannel(int gm, XivChatType player)
    {
        Assert.True(RelayFilter.Wants([Channel(player)], (XivChatType)gm));
        Assert.False(RelayFilter.Wants([Channel(XivChatType.Say)], (XivChatType)gm));
    }

    [Fact]
    public void Wants_MatchesWhatSendChatEventWouldDeliver()
    {
        // For every kind and a mix of channel setups, the filter agrees with the sender's own channel choice.
        var setups = new[]
        {
            Channel(ChatTypeGroups.PlayerChat.ToArray()),
            Channel(DiscordHandler.DefaultChatTypes),
            Channel(XivChatType.RetainerSale, XivChatType.SystemMessage),
            Channel(XivChatType.TellIncoming),
        };
        foreach (var setup in setups)
        {
            foreach (var type in Enumerable.Range(0, 128).Select(i => (XivChatType)i))
            {
                var sendType = type == XivChatType.TellOutgoing ? XivChatType.TellIncoming : type;
                Assert.Equal(ChatTypeGroups.Receives(setup, sendType), RelayFilter.Wants([setup], type));
            }
        }
    }

    [Fact]
    public void ShouldRelay_RefreshesWhenTheConfigChanges()
    {
        var config = new Configuration();
        var filter = new RelayFilter();

        Assert.False(filter.ShouldRelay(config, XivChatType.Say));

        config.ChannelConfigs[1] = Channel(XivChatType.Say);
        Assert.False(filter.ShouldRelay(config, XivChatType.Say)); // cached until the change is saved
        config.Touch();
        Assert.True(filter.ShouldRelay(config, XivChatType.Say));

        config.ChannelConfigs[1].UnsetUnique([XivChatType.Say]);
        config.Touch();
        Assert.False(filter.ShouldRelay(config, XivChatType.Say));
    }

    [Fact]
    public void Plugin_FiltersAfterTheEchoGuard()
    {
        // Loop prevention must still see every line: the echo guard runs first, then the filter.
        var source = File.ReadAllText(Path.Combine(DocumentationTests.Root, "Dalamud.DiscordBridge", "DiscordBridgePlugin.cs"));
        var guard = source.IndexOf("this.EchoGuard.Consume(", StringComparison.Ordinal);
        var filter = source.IndexOf("this.relayFilter.ShouldRelay(", StringComparison.Ordinal);
        var enqueue = source.IndexOf("MessageQueue.Enqueue(new QueuedRetainerItemSaleEvent", StringComparison.Ordinal);

        Assert.True(guard > 0 && guard < filter && filter < enqueue);
    }
}
