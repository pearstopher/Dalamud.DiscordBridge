using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge.Tests;

public class SentMarkerTests
{
    [Fact]
    public void Guard_RemembersSentVersusDisplayed()
    {
        var guard = new InboundEchoGuard();
        guard.Register(XivChatType.FreeCompany, "shown", EchoKind.Displayed);
        guard.Register(XivChatType.FreeCompany, "sent", EchoKind.Sent);

        Assert.Equal(EchoKind.Sent, guard.Consume(XivChatType.FreeCompany, "sent"));
        Assert.Equal(EchoKind.Displayed, guard.Consume(XivChatType.FreeCompany, "shown"));
        Assert.Null(guard.Consume(XivChatType.FreeCompany, "sent"));
    }

    [Fact]
    public void Guard_DefaultKindIsDisplayed()
    {
        var guard = new InboundEchoGuard();
        guard.Register(XivChatType.Say, "hi");

        Assert.Equal(EchoKind.Displayed, guard.Consume(XivChatType.Say, "hi"));
    }

    [Theory]
    [InlineData("Ane Mora", true)]          // chat, emotes, tells: tag the name
    [InlineData("Bob Smith@Gilgamesh", true)]
    [InlineData("", false)]                 // Echo has no name: tag the text
    [InlineData("  ", false)]
    public void TagPlacement(string sender, bool onSender)
    {
        Assert.Equal(onSender, SentMarker.TagGoesOnSender(sender));
    }

    [Theory]
    [InlineData(null, "[S]")]
    [InlineData("", "[S]")]
    [InlineData(" [Sent] ", "[Sent]")]
    public void Tag_DefaultsToS(string? configured, string expected)
    {
        Assert.Equal(expected, SentMarker.Tag(configured));
    }
}
