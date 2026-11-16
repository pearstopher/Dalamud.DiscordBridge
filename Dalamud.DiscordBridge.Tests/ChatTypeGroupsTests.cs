using Dalamud.DiscordBridge.Model;
using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge.Tests;

public class ChatTypeGroupsTests
{
    private static DiscordChannelConfig Subscribed(params XivChatType[] types)
    {
        var config = new DiscordChannelConfig();
        config.SetUnique(types);
        return config;
    }

    [Theory]
    [InlineData(80, XivChatType.TellIncoming)]
    [InlineData(81, XivChatType.Say)]
    [InlineData(82, XivChatType.Shout)]
    [InlineData(83, XivChatType.Yell)]
    [InlineData(84, XivChatType.Party)]
    [InlineData(85, XivChatType.FreeCompany)]
    [InlineData(86, XivChatType.Ls1)]
    [InlineData(93, XivChatType.Ls8)]
    [InlineData(94, XivChatType.NoviceNetwork)]
    public void GmChannel_MapsToPlayerChannel(int gm, XivChatType player)
    {
        Assert.Equal(player, ChatTypeGroups.PlayerEquivalentOfGm((XivChatType)gm));
    }

    [Theory]
    [InlineData(XivChatType.Say)]
    [InlineData(XivChatType.RetainerSale)]
    public void NonGm_HasNoPlayerEquivalent(XivChatType type)
    {
        Assert.Null(ChatTypeGroups.PlayerEquivalentOfGm(type));
    }

    [Fact]
    public void GmTell_ReachesChannelWithTells()
    {
        Assert.True(ChatTypeGroups.Receives(Subscribed(XivChatType.TellIncoming), (XivChatType)80));
        Assert.True(ChatTypeGroups.Receives(Subscribed(XivChatType.TellOutgoing), (XivChatType)80));
    }

    [Fact]
    public void GmFc_ReachesFcChannel_NotLinkshellChannel()
    {
        Assert.True(ChatTypeGroups.Receives(Subscribed(XivChatType.FreeCompany), (XivChatType)85));
        Assert.False(ChatTypeGroups.Receives(Subscribed(XivChatType.Ls1), (XivChatType)85));
    }

    [Fact]
    public void GmParty_ReachesCrossPartyChannel()
    {
        Assert.True(ChatTypeGroups.Receives(Subscribed(XivChatType.CrossParty), (XivChatType)84));
    }

    [Fact]
    public void GmLinkshell_ReachesOnlyTheMatchingLinkshell()
    {
        Assert.True(ChatTypeGroups.Receives(Subscribed(XivChatType.Ls3), (XivChatType)88));
        Assert.False(ChatTypeGroups.Receives(Subscribed(XivChatType.Ls2), (XivChatType)88));
    }

    [Fact]
    public void NormalTypes_StillNeedTheirOwnSubscription()
    {
        Assert.True(ChatTypeGroups.Receives(Subscribed(XivChatType.Say), XivChatType.Say));
        Assert.False(ChatTypeGroups.Receives(Subscribed(XivChatType.Say), XivChatType.Shout));
        Assert.False(ChatTypeGroups.Receives(Subscribed(XivChatType.FreeCompany), XivChatType.RetainerSale));
    }

    [Fact]
    public void GmType_SubscribedDirectly_StillReceived()
    {
        Assert.True(ChatTypeGroups.Receives(Subscribed((XivChatType)80), (XivChatType)80));
    }

    [Fact]
    public void EveryGmChannel_HasAPlayerEquivalent()
    {
        for (var t = 80; t <= 94; t++)
            Assert.NotNull(ChatTypeGroups.PlayerEquivalentOfGm((XivChatType)t));
    }
}
