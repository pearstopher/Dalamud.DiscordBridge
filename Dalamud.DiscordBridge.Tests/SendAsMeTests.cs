using System.Text;
using Dalamud.DiscordBridge.Model;
using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge.Tests;

public class GameChatCommandTests
{
    [Theory]
    [InlineData(XivChatType.Say, "/s")]
    [InlineData(XivChatType.Shout, "/sh")]
    [InlineData(XivChatType.Yell, "/y")]
    [InlineData(XivChatType.Party, "/p")]
    [InlineData(XivChatType.Alliance, "/a")]
    [InlineData(XivChatType.FreeCompany, "/fc")]
    [InlineData(XivChatType.Ls1, "/l1")]
    [InlineData(XivChatType.Ls8, "/l8")]
    [InlineData(XivChatType.CrossLinkShell1, "/cwl1")]
    [InlineData(XivChatType.CrossLinkShell8, "/cwl8")]
    [InlineData(XivChatType.Echo, "/e")]
    public void PrefixFor_SupportedChannels(XivChatType type, string prefix)
    {
        Assert.Equal(prefix, GameChatCommand.PrefixFor(type));
    }

    [Fact]
    public void EveryInboundTargetExceptNoviceNetwork_CanBeSentTo()
    {
        foreach (var type in InboundRules.AllowedTargets.Where(t => t != XivChatType.NoviceNetwork))
            Assert.NotNull(GameChatCommand.PrefixFor(type));
    }

    [Fact]
    public void TryBuild_PrefixesChannelCommand()
    {
        Assert.True(GameChatCommand.TryBuild(XivChatType.FreeCompany, "omw, 5 min", out var line, out _));
        Assert.Equal("/fc omw, 5 min", line);
    }

    [Theory]
    [InlineData("/logout")]
    [InlineData("  /gaction teleport")]
    public void TryBuild_TextStartingWithSlash_Rejected(string text)
    {
        Assert.False(GameChatCommand.TryBuild(XivChatType.FreeCompany, text, out var line, out var reason));
        Assert.Equal("", line);
        Assert.Contains("/", reason);
    }

    [Theory]
    [InlineData("/logout")]
    [InlineData(" /logout")]       // how you get past Discord's own slash-command menu
    [InlineData("**/logout**")]
    [InlineData("`/logout`")]
    [InlineData("\n/logout")]
    [InlineData("> /logout")]
    [InlineData("__/gaction teleport__")]
    public void SlashCommandsFromDiscord_AreRejectedInSendMode(string input)
    {
        var text = new DiscordToGameConverter().Convert(Msg(input), int.MaxValue);

        Assert.False(GameChatCommand.TryBuild(XivChatType.FreeCompany, text, out var line, out var reason), $"'{input}' → '{line}'");
        Assert.Equal("messages can't start with /", reason);
    }

    [Theory]
    [InlineData("||/logout||")]   // becomes "[spoiler]"
    [InlineData("hi /logout")]    // slash not at the start
    public void TextThatIsNotACommand_IsSentWithThePluginsPrefixOnly(string input)
    {
        var text = new DiscordToGameConverter().Convert(Msg(input), int.MaxValue);

        Assert.True(GameChatCommand.TryBuild(XivChatType.FreeCompany, text, out var line, out _));
        Assert.StartsWith("/fc ", line);
        Assert.False(line["/fc ".Length..].StartsWith('/'));
    }

    private static InboundDiscordMessage Msg(string input) => new()
    {
        ChannelId = 1, MessageId = 1, AuthorId = 1, AuthorName = "me", IsOwner = true,
        TargetType = XivChatType.FreeCompany, Content = input, CleanContent = input,
    };

    [Fact]
    public void TryBuild_LineBreaks_Rejected()
    {
        Assert.False(GameChatCommand.TryBuild(XivChatType.Say, "a\nb", out _, out _));
    }

    [Fact]
    public void TryBuild_Empty_Rejected()
    {
        Assert.False(GameChatCommand.TryBuild(XivChatType.Say, "  ", out _, out _));
    }

    [Theory]
    [InlineData(XivChatType.NoviceNetwork)]
    [InlineData(XivChatType.TellOutgoing)]
    [InlineData(XivChatType.SystemMessage)]
    public void TryBuild_UnsupportedChannel_Rejected(XivChatType type)
    {
        Assert.False(GameChatCommand.TryBuild(type, "hi", out _, out var reason));
        Assert.Contains("isn't supported", reason);
    }

    [Fact]
    public void TryBuild_ExactlyAtLimit_Accepted_OverLimit_Rejected()
    {
        var fits = new string('a', 500 - "/fc ".Length);
        Assert.True(GameChatCommand.TryBuild(XivChatType.FreeCompany, fits, out var line, out _));
        Assert.Equal(500, Encoding.UTF8.GetByteCount(line));

        Assert.False(GameChatCommand.TryBuild(XivChatType.FreeCompany, fits + "a", out _, out var reason));
        Assert.Contains("too long", reason);
    }

    [Fact]
    public void TryBuild_MultibyteText_CountsBytes()
    {
        // 166 × 3-byte chars = 498 bytes + "/s " = 501 > 500.
        Assert.False(GameChatCommand.TryBuild(XivChatType.Say, new string('あ', 166), out _, out _));
        Assert.True(GameChatCommand.TryBuild(XivChatType.Say, new string('あ', 165), out _, out _));
    }
}

public class SendRateLimiterTests
{
    private DateTimeOffset now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private SendRateLimiter NewLimiter(int maxQueued = 5) => new(TimeSpan.FromSeconds(2), maxQueued, () => this.now);

    [Fact]
    public void FirstMessage_SendsImmediately()
    {
        Assert.Equal(TimeSpan.Zero, NewLimiter().TryReserve());
    }

    [Fact]
    public void Burst_IsSpacedByInterval()
    {
        var limiter = NewLimiter();

        Assert.Equal(TimeSpan.Zero, limiter.TryReserve());
        Assert.Equal(TimeSpan.FromSeconds(2), limiter.TryReserve());
        Assert.Equal(TimeSpan.FromSeconds(4), limiter.TryReserve());
    }

    [Fact]
    public void Burst_BeyondQueueLimit_Rejected()
    {
        var limiter = NewLimiter(maxQueued: 5);

        Assert.Equal(TimeSpan.Zero, limiter.TryReserve());           // sent now
        for (var i = 1; i <= 5; i++)
            Assert.Equal(TimeSpan.FromSeconds(2 * i), limiter.TryReserve()); // 5 waiting
        Assert.Null(limiter.TryReserve());                             // 6th waiting: rejected
    }

    [Fact]
    public void AfterIntervalPasses_SendsImmediatelyAgain()
    {
        var limiter = NewLimiter();
        limiter.TryReserve();

        this.now = this.now.AddSeconds(3);

        Assert.Equal(TimeSpan.Zero, limiter.TryReserve());
    }

    [Fact]
    public void PartiallyElapsed_WaitsTheRemainder()
    {
        var limiter = NewLimiter();
        limiter.TryReserve();

        this.now = this.now.AddMilliseconds(500);

        Assert.Equal(TimeSpan.FromMilliseconds(1500), limiter.TryReserve());
    }

    [Fact]
    public void QueueDrains_AsTimePasses()
    {
        var limiter = NewLimiter(maxQueued: 1);
        limiter.TryReserve();                 // now
        limiter.TryReserve();                 // +2s, waiting
        Assert.Null(limiter.TryReserve());    // full

        this.now = this.now.AddSeconds(2);    // the waiting one is due

        Assert.Equal(TimeSpan.FromSeconds(2), limiter.TryReserve());
    }
}

public class SendDecisionTests
{
    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]  // not the owner: display only
    [InlineData(true, false, true, false)]  // global switch off
    [InlineData(true, true, false, false)]  // channel didn't opt in
    public void ShouldSendAsOwner_RequiresAllThree(bool owner, bool global, bool channel, bool expected)
    {
        Assert.Equal(expected, InboundRules.ShouldSendAsOwner(owner, global, channel));
    }

    [Fact]
    public void Describe_MentionsSendMode()
    {
        var config = new DiscordChannelConfig { InboundChatType = XivChatType.FreeCompany, InboundSendAsOwner = true };

        Assert.Contains("sent as your character", InboundRules.Describe(config));
    }
}
