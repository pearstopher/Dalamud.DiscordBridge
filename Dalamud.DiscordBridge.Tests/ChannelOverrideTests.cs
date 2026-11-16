using Dalamud.Game.Text;
using static Dalamud.DiscordBridge.ChannelOverride;

namespace Dalamud.DiscordBridge.Tests;

public class ChannelOverrideTests
{
    [Theory]
    [InlineData("!e note to self", XivChatType.Echo, "note to self")]
    [InlineData("!echo hi", XivChatType.Echo, "hi")]
    [InlineData("!s hello", XivChatType.Say, "hello")]
    [InlineData("!SAY hello", XivChatType.Say, "hello")]
    [InlineData("!sh hey", XivChatType.Shout, "hey")]
    [InlineData("!y hey", XivChatType.Yell, "hey")]
    [InlineData("!p pull in 5", XivChatType.Party, "pull in 5")]
    [InlineData("!a hi", XivChatType.Alliance, "hi")]
    [InlineData("!fc omw", XivChatType.FreeCompany, "omw")]
    [InlineData("!l1 hi", XivChatType.Ls1, "hi")]
    [InlineData("!ls8 hi", XivChatType.Ls8, "hi")]
    [InlineData("!cwl3 hi", XivChatType.CrossLinkShell3, "hi")]
    [InlineData("!cwls8 hi", XivChatType.CrossLinkShell8, "hi")]
    [InlineData("!n question", XivChatType.NoviceNetwork, "question")]
    [InlineData("!em waves.", XivChatType.CustomEmote, "waves.")]
    [InlineData("  !fc   spaced  ", XivChatType.FreeCompany, "spaced")]
    [InlineData("!fc line one\nline two", XivChatType.FreeCompany, "line one\nline two")]
    [InlineData("!fc", XivChatType.FreeCompany, "")]  // e.g. an image-only message
    public void ValidChannel_Overrides(string content, XivChatType type, string text)
    {
        var result = Parse(content);

        Assert.Equal(Kind.Override, result.Kind);
        Assert.Equal(new ReplyTarget(type, null), result.Target);
        Assert.Equal(text, result.Text);
    }

    [Theory]
    [InlineData("!t Bob Smith@Gilgamesh omw", "Bob Smith＠Gilgamesh", "omw")]
    [InlineData("!tell Bob Smith＠Gilgamesh omw", "Bob Smith＠Gilgamesh", "omw")]
    [InlineData("!t Bob Smith on my way", "Bob Smith", "on my way")]
    [InlineData("!t Y'shtola Rhul@Famfrit hi", "Y'shtola Rhul＠Famfrit", "hi")]
    public void Tell_ParsesPartnerAndText(string content, string partner, string text)
    {
        var result = Parse(content);

        Assert.Equal(Kind.Override, result.Kind);
        Assert.Equal(new ReplyTarget(XivChatType.TellOutgoing, partner), result.Target);
        Assert.Equal(text, result.Text);
    }

    [Theory]
    [InlineData("!t")]
    [InlineData("!t Bob")]
    [InlineData("!tell /logout")]
    public void Tell_WithoutValidName_IsInvalid(string content)
    {
        var result = Parse(content);

        Assert.Equal(Kind.Invalid, result.Kind);
        Assert.Contains("no one to send the tell to", result.Error);
    }

    [Theory]
    [InlineData("!fcc oops")]
    [InlineData("!ls9 hi")]
    [InlineData("!wow")]
    public void UnknownChannel_IsInvalid_SoTyposGoNowhere(string content)
    {
        var result = Parse(content);

        Assert.Equal(Kind.Invalid, result.Kind);
        Assert.Contains("unknown channel", result.Error);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("!!! wow")]
    [InlineData("! hi")]
    [InlineData("!")]
    [InlineData("!123 numbers")]
    [InlineData("hey !fc not at start")]
    [InlineData("xl!help")]
    [InlineData("")]
    [InlineData(null)]
    public void NotAnOverride_IsRoutedNormally(string? content)
    {
        Assert.Equal(Kind.None, Parse(content).Kind);
    }

    [Fact]
    public void EveryAlias_IsAnInboundTarget()
    {
        foreach (var (alias, type) in AllAliases())
            Assert.True(InboundRules.AllowedTargets.Contains(type), $"!{alias} → {type} isn't an inbound target");
    }
}
