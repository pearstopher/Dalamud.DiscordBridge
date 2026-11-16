using Dalamud.DiscordBridge.Model;
using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge.Tests;

public class DiscordChannelConfigTests
{
    [Fact]
    public void SetUnique_SameTypeTwice_AddsOnce()
    {
        var config = new DiscordChannelConfig();

        config.SetUnique(XivChatType.Say);
        config.SetUnique(XivChatType.Say);

        Assert.Equal([XivChatType.Say], config.ChatTypes);
    }

    [Fact]
    public void SetUnique_Many_SkipsExisting()
    {
        var config = new DiscordChannelConfig();
        config.SetUnique(XivChatType.Party);

        config.SetUnique([XivChatType.Party, XivChatType.CrossParty]);

        Assert.Equal([XivChatType.Party, XivChatType.CrossParty], config.ChatTypes);
    }

    [Fact]
    public void UnsetUnique_RemovesOnlyGivenTypes()
    {
        var config = new DiscordChannelConfig();
        config.SetUnique([XivChatType.Say, XivChatType.Shout, XivChatType.Yell]);

        config.UnsetUnique([XivChatType.Shout, XivChatType.FreeCompany]);

        Assert.Equal([XivChatType.Say, XivChatType.Yell], config.ChatTypes);
    }
}
