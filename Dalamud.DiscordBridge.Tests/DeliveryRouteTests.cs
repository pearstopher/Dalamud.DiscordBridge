using static Dalamud.DiscordBridge.DiscordHandler;

namespace Dalamud.DiscordBridge.Tests;

public class DeliveryRouteTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void ChooseRoute_DirectMessage_AlwaysDirectMessage(bool forceEmbed, bool hasPerm)
    {
        Assert.Equal(DeliveryRoute.DirectMessage, ChooseRoute(isDirectMessage: true, isGuildTextChannel: false, forceEmbed, hasPerm));
    }

    [Fact]
    public void ChooseRoute_ForceEmbed_UsesEmbedEvenWithPermission()
    {
        Assert.Equal(DeliveryRoute.Embed, ChooseRoute(false, true, forceEmbed: true, hasManageWebhooks: true));
    }

    [Fact]
    public void ChooseRoute_NonTextGuildChannel_UsesEmbed()
    {
        Assert.Equal(DeliveryRoute.Embed, ChooseRoute(false, isGuildTextChannel: false, false, false));
    }

    [Fact]
    public void ChooseRoute_GuildTextWithoutPermission_ReportsMissingPermission()
    {
        Assert.Equal(DeliveryRoute.MissingWebhookPermission, ChooseRoute(false, true, false, hasManageWebhooks: false));
    }

    [Fact]
    public void ChooseRoute_GuildTextWithPermission_UsesWebhook()
    {
        Assert.Equal(DeliveryRoute.Webhook, ChooseRoute(false, true, false, hasManageWebhooks: true));
    }
}
