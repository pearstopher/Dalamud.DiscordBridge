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

    private static ReplyDecision Decide(InboundDiscordMessage m, RelayedMessageIndex? index = null, Dictionary<XivChatType, string>? custom = null)
        => ReplyRouting.Decide(m, index ?? new RelayedMessageIndex(), custom);

    private static void AssertRoute(ReplyDecision d, ReplyTarget expected)
    {
        Assert.Equal(ReplyKind.Route, d.Kind);
        Assert.Equal(expected, d.Target);
    }

    private static void AssertBlocked(ReplyDecision d, string reasonContains)
    {
        Assert.Equal(ReplyKind.Blocked, d.Kind);
        Assert.Null(d.Target);
        Assert.Contains(reasonContains, d.Reason);
    }

    // ---- default (not a reply to a relay)

    [Fact]
    public void NotAReply_UsesDefault()
    {
        Assert.Equal(ReplyKind.NotAReply, Decide(Reply(null)).Kind);
    }

    [Fact]
    public void ReplyToAHumanMessage_UsesDefault()
    {
        Assert.Equal(ReplyKind.NotAReply, Decide(Reply(99, "**[say]** looks like a relay", "Carol", isRelay: false)).Kind);
    }

    [Theory]
    [InlineData("just a normal message")]
    [InlineData("🍐 Discord Chat Pears self-test dcp-…")]
    [InlineData("")]
    public void ReplyToBotTextThatIsNotARelay_UsesDefault(string content)
    {
        Assert.Equal(ReplyKind.NotAReply, Decide(Reply(99, content, "Chat Pears")).Kind);
    }

    // ---- routed

    [Fact]
    public void ReplyToIndexedRelay_UsesRecordedTarget()
    {
        var index = new RelayedMessageIndex();
        index.Record(50, new ReplyTarget(XivChatType.Ls3, null));

        AssertRoute(Decide(Reply(50, "ignored"), index), new ReplyTarget(XivChatType.Ls3, null));
    }

    [Fact]
    public void ReplyToIndexedTell_IsTellToPartner()
    {
        var index = new RelayedMessageIndex();
        index.Record(50, ReplyRouting.ForRelay(XivChatType.TellIncoming, "Bob Smith＠Gilgamesh"));

        AssertRoute(Decide(Reply(50), index), new ReplyTarget(XivChatType.TellOutgoing, "Bob Smith＠Gilgamesh"));
    }

    [Theory]
    [InlineData("[say] hello", XivChatType.Say)]                 // current plain tag (#56)
    [InlineData("🍐[ls1] hello", XivChatType.Ls1)]
    [InlineData("[customemote]  walks over.", XivChatType.CustomEmote)]
    [InlineData("**[say]** hello", XivChatType.Say)]             // older bold tag
    [InlineData("🍐**[ls1]** hello", XivChatType.Ls1)]
    [InlineData("**[cwls2]** hello", XivChatType.CrossLinkShell2)]
    [InlineData("**[p]** pull in 5", XivChatType.Party)]
    [InlineData("**[fc]** hi", XivChatType.FreeCompany)]
    [InlineData("**[nn]** question", XivChatType.NoviceNetwork)]
    [InlineData("**[e]** note", XivChatType.Echo)]
    [InlineData("**[customemote]**  walks over to the other bed.", XivChatType.CustomEmote)]
    public void ReplyToRelay_NotIndexed_ParsesChannelFromContent(string content, XivChatType expected)
    {
        AssertRoute(Decide(Reply(99, content, "Bob Smith＠Gilgamesh")), new ReplyTarget(expected, null));
    }

    [Fact]
    public void ReplyToIncomingTellRelay_TellsTheSender()
    {
        AssertRoute(Decide(Reply(99, "**[tell]** wanna run maps?", "Bob Smith＠Gilgamesh")),
                    new ReplyTarget(XivChatType.TellOutgoing, "Bob Smith＠Gilgamesh"));
    }

    [Theory]
    [InlineData("[tell] → Bob Smith＠Gilgamesh: on my way")]
    [InlineData("**[tell]** → Bob Smith＠Gilgamesh: on my way")]
    [InlineData("**[tell]** Ane Mora＠Famfrit → Bob Smith＠Gilgamesh: on my way")]
    public void ReplyToOutgoingTellRelay_TellsTheRecipient(string content)
    {
        AssertRoute(Decide(Reply(99, content, "Ane Mora＠Famfrit")), new ReplyTarget(XivChatType.TellOutgoing, "Bob Smith＠Gilgamesh"));
    }

    [Fact]
    public void CustomSlug_IsParsedBack()
    {
        var custom = new Dictionary<XivChatType, string> { [XivChatType.FreeCompany] = "🏠" };

        AssertRoute(Decide(Reply(99, "**[🏠]** hi", "Bob"), custom: custom), new ReplyTarget(XivChatType.FreeCompany, null));
    }

    // ---- blocked: never the default

    public static TheoryData<int> GmTypes() => new() { 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94 };

    [Theory]
    [MemberData(nameof(GmTypes))]
    public void ReplyToGmRelay_Indexed_IsBlocked(int gmType)
    {
        var index = new RelayedMessageIndex();
        index.Record(50, ReplyRouting.ForRelay((XivChatType)gmType, null));

        AssertBlocked(Decide(Reply(50, "anything"), index), "GM");
    }

    [Theory]
    [InlineData("gmtell")]
    [InlineData("gmsay")]
    [InlineData("gmshout")]
    [InlineData("gmyell")]
    [InlineData("gmp")]
    [InlineData("gmfc")]
    [InlineData("gmls1")]
    [InlineData("gmls8")]
    [InlineData("gmnn")]
    public void ReplyToGmRelay_Parsed_IsBlocked(string slug)
    {
        AssertBlocked(Decide(Reply(99, $"**[{slug}]** please stop", "GM Name")), "GM");
    }

    [Fact]
    public void GmChannelWithCustomSlug_IsStillBlocked()
    {
        var custom = new Dictionary<XivChatType, string> { [(XivChatType)80] = "staff" };

        AssertBlocked(Decide(Reply(99, "**[staff]** hello", "GM Name"), custom: custom), "GM");
    }

    [Theory]
    [InlineData("**[retainersale]** The Titanium Ore you put up for sale has sold.", "Retainer")]
    [InlineData("**[sysmsg]** You have entered a sanctuary.", "can't reply")]
    [InlineData("**[standardemote]** Bob waves.", "can't reply")]
    [InlineData("**[npctalk]** Welcome!", "can't reply")]
    [InlineData("**[random]** Random! You roll a 42.", "can't reply")]
    public void ReplyToNonChatRelay_IsBlocked(string content, string reasonContains)
    {
        AssertBlocked(Decide(Reply(99, content, "Ane Mora＠Famfrit")), reasonContains);
    }

    [Fact]
    public void ReplyToIndexedRetainerSale_IsBlocked()
    {
        var index = new RelayedMessageIndex();
        index.Record(50, ReplyRouting.ForRelay(XivChatType.RetainerSale, null));

        AssertBlocked(Decide(Reply(50), index), "can't reply");
    }

    [Fact]
    public void RelayWithUnknownSlug_IsBlocked()
    {
        AssertBlocked(Decide(Reply(99, "**[mystery]** hi", "Bob")), "can't tell which channel");
    }

    [Fact]
    public void TellWithoutPartner_IsBlocked()
    {
        var index = new RelayedMessageIndex();
        index.Record(50, ReplyRouting.ForRelay(XivChatType.TellIncoming, null));

        AssertBlocked(Decide(Reply(50), index), "who that tell was with");
    }

    // ---- defense in depth

    [Theory]
    [MemberData(nameof(GmTypes))]
    public void GmTypes_AreNeverInboundTargets_NorSendable(int gmType)
    {
        var type = (XivChatType)gmType;

        Assert.True(ReplyRouting.IsGmChannel(type));
        Assert.DoesNotContain(type, InboundRules.AllowedTargets);
        Assert.False(ReplyRouting.CanReplyTo(new ReplyTarget(type, "Some One")));
        Assert.False(GameChatCommand.TryBuild(type, "hello", "Some One@World", out _, out var reason));
        Assert.Contains("GM", reason);
    }

    [Theory]
    [InlineData("gmtell")]
    [InlineData("gmfc")]
    public void GmSlugs_CannotBeSetAsInboundTarget(string slug)
    {
        Assert.False(InboundRules.TryParseTarget(slug, out _, out _));
    }

    [Fact]
    public void NoOverrideAliasReachesAGmChannel()
    {
        foreach (var (_, type) in ChannelOverride.AllAliases())
            Assert.False(ReplyRouting.IsGmChannel(type));
    }

    [Theory]
    [InlineData(XivChatType.Say)]
    [InlineData(XivChatType.TellOutgoing)]
    [InlineData(XivChatType.RetainerSale)]
    [InlineData(XivChatType.NoviceNetwork)]  // 27, not GM novice network (94)
    public void NonGmTypes_AreNotGm(XivChatType type)
    {
        Assert.False(ReplyRouting.IsGmChannel(type));
    }
}
