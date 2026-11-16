using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge.Tests;

public class ChatKindArgsTests
{
    private static IReadOnlyList<XivChatType> Parse(string? text)
    {
        Assert.True(ChatKindArgs.TryParse(text, DiscordHandler.DefaultChatTypes, DiscordHandler.AllChatTypes, out var types, out var error), error);
        return types;
    }

    private static string ParseError(string? text)
    {
        Assert.False(ChatKindArgs.TryParse(text, DiscordHandler.DefaultChatTypes, DiscordHandler.AllChatTypes, out _, out var error));
        return error;
    }

    [Fact]
    public void SingleSlug()
    {
        Assert.Equal([XivChatType.FreeCompany], Parse("fc"));
    }

    [Fact]
    public void List_IsCaseInsensitive_TrimmedAndInOrder()
    {
        Assert.Equal([XivChatType.Ls1, XivChatType.FreeCompany], Parse(" LS1 , Fc "));
    }

    [Fact]
    public void Tell_SelectsBothDirections()
    {
        var types = Parse("tell");

        Assert.Contains(XivChatType.TellIncoming, types);
        Assert.Contains(XivChatType.TellOutgoing, types);
    }

    [Fact]
    public void P_SelectsPartyAndCrossParty()
    {
        var types = Parse("p");

        Assert.Contains(XivChatType.Party, types);
        Assert.Contains(XivChatType.CrossParty, types);
    }

    [Fact]
    public void Any_IsDefaultChatTypes()
    {
        Assert.Equal(DiscordHandler.DefaultChatTypes, Parse("any"));
    }

    [Fact]
    public void All_IsEveryKind()
    {
        Assert.Equal(DiscordHandler.AllChatTypes.Length, Parse("all").Count);
    }

    [Fact]
    public void Duplicates_AreRemoved()
    {
        var types = Parse("fc,fc,any");

        Assert.Equal(types.Distinct().Count(), types.Count);
        Assert.Equal(XivChatType.FreeCompany, types[0]);
    }

    [Theory]
    [InlineData("fcc", "`fcc`")]
    [InlineData("fc,bogus,nope", "`bogus`, `nope`")]
    public void Unknown_NamesEachBadWord(string text, string expected)
    {
        Assert.Contains(expected, ParseError(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" , ,")]
    public void Empty_IsAnError(string? text)
    {
        Assert.Contains("No chat kinds", ParseError(text));
    }

    [Fact]
    public void EveryKnownSlug_Parses()
    {
        foreach (var slug in XivChatTypeExtensions.TypeInfoDict.Values.Select(v => v.Slug).Distinct())
            Assert.NotEmpty(Parse(slug));
    }

    [Fact]
    public void FormatList_Empty_UsesEmptyText()
    {
        Assert.Equal("(none)", ChatKindArgs.FormatList([], "(none)"));
    }

    [Fact]
    public void FormatList_Short_IsACodeBlock()
    {
        Assert.Equal("```\na\nb```", ChatKindArgs.FormatList(["a", "b"], "(none)"));
    }

    [Fact]
    public void FormatList_Long_IsTruncatedWithCount()
    {
        var lines = Enumerable.Range(0, 1000).Select(i => $"Line number {i} with some padding text").ToList();

        var text = ChatKindArgs.FormatList(lines, "(none)");

        Assert.True(text.Length < 4096, $"length {text.Length}");
        Assert.Matches(@"…and \d+ more```$", text);
    }

    [Fact]
    public void Describe_UnknownTypeKey_DoesNotThrow()
    {
        var values = new Dictionary<XivChatType, string> { [XivChatType.FreeCompany] = "🍐", [(XivChatType)250] = "x" };

        var lines = ChatKindArgs.Describe(values).ToList();

        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.EndsWith("- 🍐"));
    }

    [Theory]
    [InlineData("https://example.com/a.png", true)]
    [InlineData("http://example.com/a.png", true)]
    [InlineData("example.com/a.png", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///c:/x.png", false)]
    [InlineData("", false)]
    public void IsValidAvatarUrl(string url, bool expected)
    {
        Assert.Equal(expected, ChatKindArgs.IsValidAvatarUrl(url));
    }

    [Theory]
    [InlineData("5000", true, 5000)]
    [InlineData("0", true, 0)]
    [InlineData("none", true, 0)]
    [InlineData("OFF", true, 0)]
    [InlineData("-1", false, 0)]
    [InlineData("abc", false, 0)]
    [InlineData(null, false, 0)]
    public void TryParseDuplicateMs(string? text, bool ok, long expected)
    {
        Assert.Equal(ok, ChatKindArgs.TryParseDuplicateMs(text, out var ms));
        Assert.Equal(expected, ms);
    }

    [Fact]
    public void Handler_KindCommandsUseTheParser()
    {
        // Regression (#42): GetBySlug("any") etc. threw, and unsetprefix read a missing args[2].
        var source = File.ReadAllText(Path.Combine(DocumentationTests.Root, "Dalamud.DiscordBridge", "DiscordHandler.cs"));

        Assert.DoesNotContain("GetBySlug(", source);
        Assert.DoesNotContain(".Aggregate(", source);
        foreach (var command in new[] { "setchannel", "unsetchannel", "setprefix", "unsetprefix", "setchattypename", "unsetchattypename", "setavatar", "unsetavatar" })
            Assert.Contains($"TryParseKindsAsync(message, args, ", source[source.IndexOf($"+ \"{command}\" &&", StringComparison.Ordinal)..]);
    }
}
