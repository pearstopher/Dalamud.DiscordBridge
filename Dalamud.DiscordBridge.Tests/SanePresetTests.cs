using Dalamud.DiscordBridge.Model;
using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge.Tests;

public class SanePresetTests
{
    private const ulong Channel = 42;

    [Fact]
    public void NewChannel_GetsPlayerChatBothWays()
    {
        var config = new Configuration();

        SanePreset.Apply(config, Channel);

        var channel = config.ChannelConfigs[Channel];
        Assert.Equal(ChatTypeGroups.PlayerChat, channel.ChatTypes);
        Assert.Equal(XivChatType.Echo, channel.InboundChatType);
        Assert.Equal(InboundPermission.OwnerOnly, channel.InboundPermission);
        Assert.False(channel.IsContentFinder);
    }

    [Fact]
    public void Notifications_AreTurnedOff()
    {
        var config = new Configuration();
        var existing = new DiscordChannelConfig { IsContentFinder = true };
        existing.SetUnique([XivChatType.RetainerSale, XivChatType.SystemMessage, XivChatType.Echo, XivChatType.LootNotice, XivChatType.FreeCompany]);
        config.ChannelConfigs[Channel] = existing;

        SanePreset.Apply(config, Channel);

        var types = config.ChannelConfigs[Channel].ChatTypes;
        Assert.DoesNotContain(XivChatType.RetainerSale, types);
        Assert.DoesNotContain(XivChatType.SystemMessage, types);
        Assert.DoesNotContain(XivChatType.LootNotice, types);
        Assert.DoesNotContain(XivChatType.Echo, types);
        Assert.Contains(XivChatType.FreeCompany, types);
        Assert.False(config.ChannelConfigs[Channel].IsContentFinder);
    }

    [Fact]
    public void PlayerChat_CoversEveryInboundChannelExceptEcho()
    {
        foreach (var type in InboundRules.AllowedTargets.Where(t => t != XivChatType.Echo))
            Assert.Contains(type, ChatTypeGroups.PlayerChat);
    }

    [Fact]
    public void PlayerChat_IncludesBothTellDirections_AndNoNotificationsOrGm()
    {
        Assert.Contains(XivChatType.TellIncoming, ChatTypeGroups.PlayerChat);
        Assert.Contains(XivChatType.TellOutgoing, ChatTypeGroups.PlayerChat);
        foreach (var type in ChatTypeGroups.PlayerChat)
        {
            Assert.False(ReplyRouting.IsGmChannel(type));
            Assert.NotEqual(XivChatType.RetainerSale, type);
            Assert.NotEqual(XivChatType.SystemMessage, type);
        }
    }

    [Fact]
    public void GmTellsStillArrive_AfterSetup()
    {
        var config = new Configuration();
        SanePreset.Apply(config, Channel);

        Assert.True(ChatTypeGroups.Receives(config.ChannelConfigs[Channel], (XivChatType)80));
    }

    [Fact]
    public void DisplayOptions_ResetToDefaults()
    {
        var config = new Configuration
        {
            InboundDisplayEnabled = false,
            InboundDisplayAsEcho = true,
            InboundReactions = false,
            MarkSentInGame = false,
            SenderInMessage = true,
            ForceEmbedFallbackMode = true,
            ForceDefaultNameAvatar = true,
            DuplicateCheckMS = 1,
        };

        SanePreset.Apply(config, Channel);

        Assert.True(config.InboundDisplayEnabled);
        Assert.False(config.InboundDisplayAsEcho);
        Assert.True(config.InboundReactions);
        Assert.True(config.MarkSentInGame);
        Assert.False(config.SenderInMessage);
        Assert.False(config.ForceEmbedFallbackMode);
        Assert.False(config.ForceDefaultNameAvatar);
        Assert.Equal(5000, config.DuplicateCheckMS);
    }

    [Fact]
    public void DoesNotTouch_IdentityCustomizationsOtherChannelsOrSendOptIn()
    {
        var config = new Configuration
        {
            DiscordToken = "token",
            DiscordOwnerName = "pear",
            DiscordBotPrefix = "p!",
            AllowSendAsCharacter = true,
        };
        config.PrefixConfigs[XivChatType.FreeCompany] = "🏠";
        config.CustomSlugsConfigs[XivChatType.Ls1] = "Raid";
        config.ChatTypeAvatarURL[XivChatType.Say] = "https://example.com/a.png";
        var other = new DiscordChannelConfig { InboundChatType = XivChatType.FreeCompany };
        other.SetUnique(XivChatType.RetainerSale);
        config.ChannelConfigs[7] = other;
        config.ChannelConfigs[Channel] = new DiscordChannelConfig { InboundSendAsOwner = true };

        var summary = SanePreset.Apply(config, Channel);

        Assert.Equal("token", config.DiscordToken);
        Assert.Equal("pear", config.DiscordOwnerName);
        Assert.Equal("p!", config.DiscordBotPrefix);
        Assert.True(config.AllowSendAsCharacter);
        Assert.Equal("🏠", config.PrefixConfigs[XivChatType.FreeCompany]);
        Assert.Equal("Raid", config.CustomSlugsConfigs[XivChatType.Ls1]);
        Assert.Equal("https://example.com/a.png", config.ChatTypeAvatarURL[XivChatType.Say]);
        Assert.Equal([XivChatType.RetainerSale], config.ChannelConfigs[7].ChatTypes);
        Assert.Equal(XivChatType.FreeCompany, config.ChannelConfigs[7].InboundChatType);
        Assert.True(config.ChannelConfigs[Channel].InboundSendAsOwner);
        Assert.Contains(summary, line => line.Contains("Send as me:** ON"));
    }

    [Fact]
    public void IsIdempotent()
    {
        var config = new Configuration();
        SanePreset.Apply(config, Channel);
        var first = config.ChannelConfigs[Channel].ChatTypes.ToList();

        SanePreset.Apply(config, Channel);

        Assert.Equal(first, config.ChannelConfigs[Channel].ChatTypes);
    }
}
