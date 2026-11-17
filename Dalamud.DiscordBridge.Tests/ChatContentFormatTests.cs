namespace Dalamud.DiscordBridge.Tests;

public class ChatContentFormatTests
{
    [Theory]
    [InlineData(false, null, "hello")]
    [InlineData(true, null, "Ane＠Famfrit: hello")]
    [InlineData(false, "Bob＠Gilgamesh", "→ Bob＠Gilgamesh: hello")]
    [InlineData(true, "Bob＠Gilgamesh", "Ane＠Famfrit → Bob＠Gilgamesh: hello")]
    [InlineData(false, "  ", "hello")]
    public void Body(bool senderInMessage, string? recipient, string expected)
    {
        Assert.Equal(expected, ChatContentFormat.Body("hello", senderInMessage, "Ane＠Famfrit", recipient));
    }

    [Fact]
    public void Content_Normal_HasSlug()
    {
        Assert.Equal("💬[tell] → Bob: hi", ChatContentFormat.Content("💬", "tell", isIpc: false, "→ Bob: hi"));
    }

    [Fact]
    public void Content_Ipc_HasNoSlug()
    {
        Assert.Equal(" hi", ChatContentFormat.Content(null, "ipc", isIpc: true, "hi"));
    }

    [Fact]
    public void Content_RoundTripsThroughContentParser_ForDuplicateFilter()
    {
        var body = ChatContentFormat.Body("hello", false, "Ane", "Bob＠Gilgamesh");
        var parsed = new ContentParser(ChatContentFormat.Content("🍐", "tell", false, body));

        Assert.Equal("tell", parsed.Slug);
        Assert.Equal(body, parsed.Text);
    }

    [Theory]
    [InlineData(">> Bob Smith@Gilgamesh", "Bob Smith@Gilgamesh")]
    [InlineData(">>Bob Smith", "Bob Smith")]
    [InlineData("Bob Smith", "Bob Smith")]
    [InlineData("  Bob Smith:  ", "Bob Smith")]
    [InlineData(">>", "")]
    public void CleanRecipientText(string raw, string expected)
    {
        Assert.Equal(expected, ChatContentFormat.CleanRecipientText(raw));
    }
}
