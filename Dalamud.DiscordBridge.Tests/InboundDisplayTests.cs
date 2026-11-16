using Dalamud.DiscordBridge.Model;
using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge.Tests;

public class InboundEchoGuardTests
{
    private DateTimeOffset now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private InboundEchoGuard NewGuard() => new(() => this.now, TimeSpan.FromSeconds(10));

    [Fact]
    public void RegisteredLine_IsConsumedOnce()
    {
        var guard = NewGuard();
        guard.Register(XivChatType.FreeCompany, "hello");

        Assert.True(guard.TryConsume(XivChatType.FreeCompany, "hello"));
        Assert.False(guard.TryConsume(XivChatType.FreeCompany, "hello")); // a real player saying it later is relayed
    }

    [Fact]
    public void DifferentTypeOrText_NotConsumed()
    {
        var guard = NewGuard();
        guard.Register(XivChatType.FreeCompany, "hello");

        Assert.False(guard.TryConsume(XivChatType.Ls1, "hello"));
        Assert.False(guard.TryConsume(XivChatType.FreeCompany, "hello!"));
        Assert.Equal(1, guard.PendingCount);
    }

    [Fact]
    public void HighBitsOnType_AndSurroundingWhitespace_StillMatch()
    {
        var guard = NewGuard();
        guard.Register(XivChatType.FreeCompany, "hello");

        Assert.True(guard.TryConsume((XivChatType)((int)XivChatType.FreeCompany | 0x80), " hello "));
    }

    [Fact]
    public void ExpiredEntries_AreForgotten()
    {
        var guard = NewGuard();
        guard.Register(XivChatType.FreeCompany, "hello");

        this.now = this.now.AddSeconds(11);

        Assert.False(guard.TryConsume(XivChatType.FreeCompany, "hello"));
        Assert.Equal(0, guard.PendingCount);
    }

    [Fact]
    public void DuplicateRegistrations_EachConsumeOnce()
    {
        var guard = NewGuard();
        guard.Register(XivChatType.Say, "gg");
        guard.Register(XivChatType.Say, "gg");

        Assert.True(guard.TryConsume(XivChatType.Say, "gg"));
        Assert.True(guard.TryConsume(XivChatType.Say, "gg"));
        Assert.False(guard.TryConsume(XivChatType.Say, "gg"));
    }
}

public class InboundFormatTests
{
    private static InboundDiscordMessage Msg(string author, XivChatType target) => new()
    {
        ChannelId = 1, MessageId = 2, AuthorId = 3, AuthorName = author, IsOwner = false, TargetType = target,
    };

    [Fact]
    public void Build_Default_UsesTargetTypeAndSenderLabel()
    {
        var entry = InboundFormat.Build(Msg("Alice", XivChatType.FreeCompany), "hi", null, asEcho: false);

        Assert.Equal(new InboundEntry(XivChatType.FreeCompany, "[D] Alice", "hi"), entry);
    }

    [Fact]
    public void Build_Echo_PutsChannelAndNameInText()
    {
        var entry = InboundFormat.Build(Msg("Alice", XivChatType.Ls2), "hi", null, asEcho: true);

        Assert.Equal(new InboundEntry(XivChatType.Echo, "", "[LS2] [D] Alice: hi"), entry);
    }

    [Theory]
    [InlineData("Discord: {name}", "Discord: Alice")]
    [InlineData("{name}", "Alice")]
    [InlineData("🍐", "🍐 Alice")]   // format without {name} gets the name appended
    [InlineData("", "[D] Alice")]
    public void SenderLabel_Format(string format, string expected)
    {
        Assert.Equal(expected, InboundFormat.SenderLabel("Alice", format));
    }

    [Fact]
    public void SenderLabel_CleansName()
    {
        Assert.Equal("[D] Alice (pear)", InboundFormat.SenderLabel("Alice 🍐", null));
    }

    [Theory]
    [InlineData(InboundResult.Shown, "✅")]
    [InlineData(InboundResult.Sent, "✅")]
    [InlineData(InboundResult.Queued, "⏳")]
    [InlineData(InboundResult.NotLoggedIn, "⏸️")]
    [InlineData(InboundResult.Rejected, "❌")]
    [InlineData(InboundResult.Failed, "⚠️")]
    [InlineData(InboundResult.Disabled, null)]
    [InlineData(InboundResult.Empty, null)]
    public void ReactionFor(InboundResult result, string? emoji)
    {
        Assert.Equal(emoji, InboundRules.ReactionFor(result));
    }
}
