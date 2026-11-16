using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace Dalamud.DiscordBridge.Tests;

public class FcLoginNoticeTests
{
    [Theory]
    [InlineData("Bob Smith has logged in.", "LoggedIn")]
    [InlineData("Bob Smith has logged out.", "LoggedOut")]
    [InlineData("Bob Smithがログインしました。", "LoggedIn")]
    [InlineData("Bob Smithがログアウトしました。", "LoggedOut")]
    [InlineData("Bob Smith hat sich eingeloggt.", "LoggedIn")]
    [InlineData("Bob Smith hat sich ausgeloggt.", "LoggedOut")]
    [InlineData("Bob Smith s'est connecté(e).", "LoggedIn")]
    [InlineData("Bob Smith s'est déconnecté(e).", "LoggedOut")]
    [InlineData("Something else entirely.", "Unknown")]
    public void Classify(string text, string expected)
    {
        Assert.Equal(expected, FcLoginNotice.Classify(text).ToString());
    }

    [Theory]
    [InlineData("Bob Smith has logged in.", "Bob Smith")]
    [InlineData("Ane Mora has logged out.", "Ane Mora")]
    [InlineData("Y'shtola Rhul has logged in.", "Y'shtola Rhul")]
    [InlineData("A'zor-Lanu Ah has logged in.", "A'zor-Lanu Ah")]
    [InlineData("\uE05D Bob Smith has logged in.", "Bob Smith")]   // leading game icon
    [InlineData("Bob Smithがログインしました。", "Bob Smith")]
    [InlineData("has logged in.", null)]
    [InlineData("", null)]
    public void NameFromText(string text, string? expected)
    {
        Assert.Equal(expected, FcLoginNotice.NameFromText(text));
    }

    [Fact]
    public void Resolve_PlayerLinkWins()
    {
        var (name, world, text) = FcLoginNotice.Resolve("Bob Smith", "Gilgamesh", "Bob Smith has logged in.", "Famfrit");

        Assert.Equal("Bob Smith", name);
        Assert.Equal("Gilgamesh", world);
        Assert.Equal("🟢 Bob Smith has logged in.", text);
    }

    [Fact]
    public void Resolve_NoLink_UsesTextNameAndYourHomeWorld()
    {
        var (name, world, text) = FcLoginNotice.Resolve(null, null, "Bob Smith has logged out.", "Famfrit");

        Assert.Equal("Bob Smith", name);
        Assert.Equal("Famfrit", world);
        Assert.Equal("⚫ Bob Smith has logged out.", text);
    }

    [Fact]
    public void Resolve_NoNameAtAll_PostsAsFreeCompany()
    {
        var (name, world, text) = FcLoginNotice.Resolve(null, null, "??? has logged in.", "Famfrit");

        Assert.Equal(FcLoginNotice.UnknownSender, name);
        Assert.Equal("", world); // no world: no Lodestone search for "Free Company"
        Assert.StartsWith("🟢", text);
    }

    [Theory]
    [InlineData(70, true)]
    [InlineData(70 | 0x80, true)]
    [InlineData((int)XivChatType.FreeCompany, false)]
    public void Is(int type, bool expected)
    {
        Assert.Equal(expected, FcLoginNotice.Is((XivChatType)type));
    }

    [Fact]
    public void FcLogin_IsAKnownKindWithSlugFclogin()
    {
        Assert.Equal("fclogin", FcLoginNotice.ChatType.GetSlug());
    }

    [Fact]
    public void Queue_ReadsTheNameFromTheTextWhenThereIsNoLink()
    {
        var message = new SeString(new TextPayload("Bob Smith has logged in."));

        var (name, world, text) = DiscordMessageQueue.ResolveFcLogin(message, new SeString(), "Famfrit");

        Assert.Equal(("Bob Smith", "Famfrit", "🟢 Bob Smith has logged in."), (name, world, text));
    }

    [Fact]
    public void RepliesToFcLoginPosts_AreBlocked()
    {
        Assert.False(ReplyRouting.CanReplyTo(ReplyRouting.ForRelay(FcLoginNotice.ChatType, null)));
    }
}
