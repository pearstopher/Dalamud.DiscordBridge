using Dalamud.DiscordBridge.Model;
using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge.Tests;

public class RelayedMessageIndexTests
{
    [Fact]
    public void Record_ThenFind()
    {
        var index = new RelayedMessageIndex();
        index.Record(10, new ReplyTarget(XivChatType.Ls1, null));

        Assert.Equal(new ReplyTarget(XivChatType.Ls1, null), index.Find(10));
        Assert.Null(index.Find(11));
    }

    [Fact]
    public void OverCapacity_ForgetsOldest()
    {
        var index = new RelayedMessageIndex(capacity: 2);
        index.Record(1, new ReplyTarget(XivChatType.Say, null));
        index.Record(2, new ReplyTarget(XivChatType.Say, null));
        index.Record(3, new ReplyTarget(XivChatType.Say, null));

        Assert.Null(index.Find(1));
        Assert.NotNull(index.Find(3));
        Assert.Equal(2, index.Count);
    }

    [Fact]
    public void ZeroId_Ignored()
    {
        var index = new RelayedMessageIndex();
        index.Record(0, new ReplyTarget(XivChatType.Say, null));

        Assert.Equal(0, index.Count);
    }
}

public class ReplyRoutingTests
{
    private static InboundDiscordMessage Reply(ulong? replyTo, string? content = null, string? author = null, bool isRelay = true) => new()
    {
        ChannelId = 1, MessageId = 2, AuthorId = 3, AuthorName = "Ane", IsOwner = true,
        TargetType = XivChatType.FreeCompany, // the channel's setinbound default
        ReplyToMessageId = replyTo, ReplyToContent = content, ReplyToAuthor = author, ReplyToIsRelay = isRelay,
    };

    [Fact]
    public void NotAReply_UsesDefault()
    {
        Assert.Null(ReplyRouting.Resolve(Reply(null), new RelayedMessageIndex()));
    }

    [Fact]
    public void ReplyToIndexedRelay_UsesRecordedTarget()
    {
        var index = new RelayedMessageIndex();
        index.Record(50, new ReplyTarget(XivChatType.Ls3, null));

        Assert.Equal(new ReplyTarget(XivChatType.Ls3, null), ReplyRouting.Resolve(Reply(50, "ignored"), index));
    }

    [Fact]
    public void ReplyToIndexedTell_IsTellToPartner()
    {
        var index = new RelayedMessageIndex();
        index.Record(50, ReplyRouting.ForRelay(XivChatType.TellIncoming, "Bob Smith＠Gilgamesh"));

        Assert.Equal(new ReplyTarget(XivChatType.TellOutgoing, "Bob Smith＠Gilgamesh"), ReplyRouting.Resolve(Reply(50), index));
    }

    [Theory]
    [InlineData("**[say]** hello", XivChatType.Say)]
    [InlineData("🍐**[ls1]** hello", XivChatType.Ls1)]
    [InlineData("**[cwls2]** hello", XivChatType.CrossLinkShell2)]
    [InlineData("**[p]** pull in 5", XivChatType.Party)]
    [InlineData("**[fc]** hi", XivChatType.FreeCompany)]
    [InlineData("**[customemote]**  walks over to the other bed.", XivChatType.CustomEmote)]
    public void ReplyToRelay_NotIndexed_ParsesChannelFromContent(string content, XivChatType expected)
    {
        Assert.Equal(new ReplyTarget(expected, null), ReplyRouting.Resolve(Reply(99, content, "Bob Smith＠Gilgamesh"), new RelayedMessageIndex()));
    }

    [Fact]
    public void ReplyToIncomingTellRelay_TellsTheSender()
    {
        var target = ReplyRouting.Resolve(Reply(99, "**[tell]** wanna run maps?", "Bob Smith＠Gilgamesh"), new RelayedMessageIndex());

        Assert.Equal(new ReplyTarget(XivChatType.TellOutgoing, "Bob Smith＠Gilgamesh"), target);
    }

    [Theory]
    [InlineData("**[tell]** → Bob Smith＠Gilgamesh: on my way")]
    [InlineData("**[tell]** Ane Mora＠Famfrit → Bob Smith＠Gilgamesh: on my way")] // sender-in-message mode
    public void ReplyToOutgoingTellRelay_TellsTheRecipient(string content)
    {
        var target = ReplyRouting.Resolve(Reply(99, content, "Ane Mora＠Famfrit"), new RelayedMessageIndex());

        Assert.Equal(new ReplyTarget(XivChatType.TellOutgoing, "Bob Smith＠Gilgamesh"), target);
    }

    [Fact]
    public void CustomSlug_IsParsedBack()
    {
        var custom = new Dictionary<XivChatType, string> { [XivChatType.FreeCompany] = "🏠" };

        Assert.Equal(new ReplyTarget(XivChatType.FreeCompany, null),
                     ReplyRouting.Resolve(Reply(99, "**[🏠]** hi", "Bob"), new RelayedMessageIndex(), custom));
    }

    [Theory]
    [InlineData("**[standardemote]** Bob waves")]  // can't talk in emotes
    [InlineData("**[sysmsg]** something")]
    [InlineData("just a normal message")]           // not a relay format
    public void ReplyToSomethingYouCantTalkIn_UsesDefault(string content)
    {
        Assert.Null(ReplyRouting.Resolve(Reply(99, content, "Bob"), new RelayedMessageIndex()));
    }

    [Fact]
    public void ReplyToAHumanMessage_UsesDefault()
    {
        Assert.Null(ReplyRouting.Resolve(Reply(99, "**[say]** looks like a relay", "Carol", isRelay: false), new RelayedMessageIndex()));
    }

    [Fact]
    public void TellWithoutPartner_UsesDefault()
    {
        var index = new RelayedMessageIndex();
        index.Record(50, ReplyRouting.ForRelay(XivChatType.TellIncoming, null));

        Assert.Null(ReplyRouting.Resolve(Reply(50), index));
    }
}

public class TellCommandTests
{
    [Theory]
    [InlineData("Bob Smith＠Gilgamesh", "Bob Smith@Gilgamesh")]
    [InlineData("Bob Smith@Gilgamesh", "Bob Smith@Gilgamesh")]
    [InlineData("  Bob Smith  ", "Bob Smith")]
    [InlineData("Y'shtola Rhul@Famfrit", "Y'shtola Rhul@Famfrit")]
    [InlineData("Ane-Mora Pear@Famfrit", "Ane-Mora Pear@Famfrit")]
    public void NormalizeTellTarget_Valid(string input, string expected)
    {
        Assert.Equal(expected, GameChatCommand.NormalizeTellTarget(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bob")]                        // needs first and last name
    [InlineData("Bob Smith@Gilgamesh /logout")]
    [InlineData("Bob Smith; /logout")]
    [InlineData("/logout Bob")]
    public void NormalizeTellTarget_Invalid(string? input)
    {
        Assert.Null(GameChatCommand.NormalizeTellTarget(input));
    }

    [Fact]
    public void TryBuild_Tell()
    {
        Assert.True(GameChatCommand.TryBuild(XivChatType.TellOutgoing, "omw", "Bob Smith＠Gilgamesh", out var line, out _));
        Assert.Equal("/tell Bob Smith@Gilgamesh omw", line);
    }

    [Fact]
    public void TryBuild_TellWithoutTarget_Rejected()
    {
        Assert.False(GameChatCommand.TryBuild(XivChatType.TellOutgoing, "omw", null, out _, out var reason));
        Assert.Equal("no one to send the tell to", reason);
    }

    [Fact]
    public void TryBuild_TellText_StillCantStartWithSlash()
    {
        Assert.False(GameChatCommand.TryBuild(XivChatType.TellOutgoing, "/logout", "Bob Smith@Gilgamesh", out _, out var reason));
        Assert.Equal("messages can't start with /", reason);
    }

    [Fact]
    public void TryBuild_Tell_CountsWholeLineAgainstLimit()
    {
        var prefix = "/tell Bob Smith@Gilgamesh ";
        Assert.True(GameChatCommand.TryBuild(XivChatType.TellOutgoing, new string('a', 500 - prefix.Length), "Bob Smith@Gilgamesh", out _, out _));
        Assert.False(GameChatCommand.TryBuild(XivChatType.TellOutgoing, new string('a', 501 - prefix.Length), "Bob Smith@Gilgamesh", out _, out _));
    }

    [Fact]
    public void Display_TellShowsRecipientInTellColour()
    {
        var message = new InboundDiscordMessage
        {
            ChannelId = 1, MessageId = 2, AuthorId = 3, AuthorName = "Alice", IsOwner = false,
            TargetType = XivChatType.TellOutgoing, TellTarget = "Bob Smith＠Gilgamesh",
        };

        Assert.Equal(new InboundEntry(XivChatType.TellIncoming, "[D] Alice", "→ Bob Smith＠Gilgamesh: hi"),
                     InboundFormat.Build(message, "hi", null, asEcho: false));
    }
}
