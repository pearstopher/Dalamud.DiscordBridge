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
