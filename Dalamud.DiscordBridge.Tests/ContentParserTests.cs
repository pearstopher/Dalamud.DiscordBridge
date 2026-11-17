namespace Dalamud.DiscordBridge.Tests;

public class ContentParserTests
{
    [Fact]
    public void Parse_FormattedMessage_ExtractsSlugAndText()
    {
        var parser = new ContentParser("**[fc]** hello there");

        Assert.Equal("", parser.Prefix);
        Assert.Equal("fc", parser.Slug);
        Assert.Equal("hello there", parser.Text);
    }

    [Fact]
    public void Parse_WithPrefix_ExtractsPrefix()
    {
        var parser = new ContentParser("🍐 **[ls1]** pears are great");

        Assert.Equal("🍐 ", parser.Prefix);
        Assert.Equal("ls1", parser.Slug);
        Assert.Equal("pears are great", parser.Text);
    }

    [Theory]
    [InlineData("[fc] hello there", "")]
    [InlineData("🍐 [ls1] hello there", "🍐 ")]
    [InlineData("💬[tell] hello there", "💬")]
    public void Parse_PlainTag_ExtractsPrefixSlugAndText(string content, string prefix)
    {
        // The current format (#56); the bold one above is still parsed for older messages.
        var parser = new ContentParser(content);

        Assert.Equal(prefix, parser.Prefix);
        Assert.NotEqual("", parser.Slug);
        Assert.Equal("hello there", parser.Text);
    }

    [Fact]
    public void Parse_FirstTagWins_BracketsInTextStayInText()
    {
        var parser = new ContentParser("[say] see [this] and **[that]** too");

        Assert.Equal("say", parser.Slug);
        Assert.Equal("see [this] and **[that]** too", parser.Text);
    }

    [Fact]
    public void Parse_BracketsWithSpaces_AreNotATag()
    {
        Assert.Equal("", new ContentParser("[not a tag] hi").Slug);
    }

    [Fact]
    public void Parse_UnformattedMessage_ReturnsEmptyGroups()
    {
        var parser = new ContentParser("just some text");

        Assert.Equal("", parser.Slug);
        Assert.Equal("", parser.Text);
    }

    [Fact]
    public void ParseText_UnformattedMessage_FallsBackToWholeContent()
    {
        Assert.Equal("just some text", ContentParser.ParseText("just some text"));
    }

    [Fact]
    public void ParseText_FormattedMessage_ReturnsTextOnly()
    {
        Assert.Equal("hi", ContentParser.ParseText("**[say]** hi"));
    }
}
