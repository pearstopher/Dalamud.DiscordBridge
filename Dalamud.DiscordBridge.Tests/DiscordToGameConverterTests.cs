using System.Text;
using Dalamud.DiscordBridge.Model;
using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge.Tests;

public class DiscordToGameConverterTests
{
    private static InboundDiscordMessage Msg(string clean, string? raw = null, IReadOnlyList<InboundAttachment>? attachments = null,
                                             IReadOnlyList<string>? stickers = null, string? replyTo = null) => new()
    {
        ChannelId = 1, MessageId = 2, AuthorId = 3, AuthorName = "Alice", IsOwner = false,
        TargetType = XivChatType.FreeCompany,
        Content = raw ?? clean,
        CleanContent = clean,
        Attachments = attachments ?? [],
        Stickers = stickers ?? [],
        ReplyToAuthor = replyTo,
    };

    private readonly DiscordToGameConverter converter = new();

    [Fact]
    public void PlainText_Unchanged()
    {
        Assert.Equal("anyone up for maps?", converter.Convert(Msg("anyone up for maps?")));
    }

    [Fact]
    public void UsesCleanContent_ForMentions()
    {
        Assert.Equal("hi @Bob in #raids", converter.Convert(Msg("hi @Bob in #raids", raw: "hi <@123> in <#456>")));
    }

    [Fact]
    public void FallsBackToRawContent_WhenCleanEmpty()
    {
        Assert.Equal("raw text", converter.Convert(Msg("", raw: "raw text")));
    }

    [Theory]
    [InlineData("nice <:pepeHappy:123456>", "nice :pepeHappy:")]
    [InlineData("<a:dance:99> party", ":dance: party")]
    public void CustomEmoji_BecomesName(string input, string expected)
    {
        Assert.Equal(expected, converter.Convert(Msg(input)));
    }

    [Theory]
    [InlineData("gg 👍", "gg (+1)")]
    [InlineData("love it ❤️", "love it <3")]
    [InlineData("ok 👍🏽", "ok (+1)")]            // skin tone dropped
    [InlineData("rocket 🚀 launch", "rocket launch")] // unmapped emoji removed
    [InlineData("👨‍👩‍👧 family", "family")]          // ZWJ sequence removed
    public void UnicodeEmoji_MappedOrRemoved(string input, string expected)
    {
        Assert.Equal(expected, converter.Convert(Msg(input)));
    }

    [Theory]
    [InlineData("**bold** and __under__ and ~~gone~~", "bold and under and gone")]
    [InlineData("*leaning* text", "leaning text")]
    [InlineData("2*3*4 = 24", "2*3*4 = 24")]           // not italics
    [InlineData("snake_case_name", "snake_case_name")] // underscores kept
    [InlineData("say `/echo hi` now", "say /echo hi now")]
    [InlineData("```cs\nvar x = 1;\n```", "var x = 1;")]
    [InlineData("the boss is ||Zodiark||", "the boss is [spoiler]")]
    [InlineData("# Header\n> quoted\n-# small", "Header quoted small")]
    [InlineData("see [the guide](https://example.com/g)", "see the guide (https://example.com/g)")]
    [InlineData("link <https://example.com>", "link https://example.com")]
    [InlineData("at <t:1700000000:R>", "at [time]")]
    public void Markdown_Stripped(string input, string expected)
    {
        Assert.Equal(expected, converter.Convert(Msg(input)));
    }

    [Fact]
    public void Newlines_CollapseToSpaces()
    {
        Assert.Equal("line one line two", converter.Convert(Msg("line one\n\n  line\ttwo  ")));
    }

    [Fact]
    public void PrivateUseAndControlChars_Removed()
    {
        Assert.Equal("ab", converter.Convert(Msg("a\u0007​b")));
    }

    [Fact]
    public void Attachments_Placeholders()
    {
        var attachments = new[] { new InboundAttachment("a.png", true), new InboundAttachment("b.jpg", true), new InboundAttachment("c.txt", false) };

        Assert.Equal("look [2 images] [file]", converter.Convert(Msg("look", attachments: attachments)));
    }

    [Fact]
    public void AttachmentOnly_IsJustPlaceholder()
    {
        Assert.Equal("[image]", converter.Convert(Msg("", attachments: [new InboundAttachment("a.png", true)])));
    }

    [Fact]
    public void Sticker_Placeholder()
    {
        Assert.Equal("[sticker: Wave]", converter.Convert(Msg("", stickers: ["Wave"])));
    }

    [Fact]
    public void Reply_Prefixed()
    {
        Assert.Equal("re @Bob: me too", converter.Convert(Msg("me too", replyTo: "Bob")));
    }

    [Fact]
    public void Empty_ReturnsEmpty()
    {
        Assert.Equal("", converter.Convert(Msg("   ")));
        Assert.Equal("", converter.Convert(Msg("🚀")));
    }

    [Theory]
    [InlineData("/logout", "/logout")]
    [InlineData(" /logout", "/logout")]
    [InlineData("**/logout**", "/logout")]
    [InlineData("a /b", "a /b")]
    public void LeadingSlash_KeptAsIs_DisplayNeverRunsCommands(string input, string expected)
    {
        // Send mode rejects these (GameChatCommandTests); display prints them literally.
        Assert.Equal(expected, converter.Convert(Msg(input)));
    }

    [Fact]
    public void LongText_TruncatedToByteBudgetWithEllipsis()
    {
        var result = converter.Convert(Msg(new string('a', 600)));

        Assert.Equal(500, Encoding.UTF8.GetByteCount(result));
        Assert.EndsWith("...", result);
    }

    [Fact]
    public void Truncation_NeverSplitsMultibyteCharacters()
    {
        // Each あ is 3 bytes; a 100-byte budget leaves 97 for text -> 32 characters.
        var result = converter.Convert(Msg(new string('あ', 200)), byteBudget: 100);

        Assert.True(Encoding.UTF8.GetByteCount(result) <= 100);
        Assert.Equal(new string('あ', 32) + "...", result);
    }

    [Fact]
    public void Sanitizer_IsApplied()
    {
        var upper = new DiscordToGameConverter(new UpperSanitizer());

        Assert.Equal("HELLO", upper.Convert(Msg("hello")));
    }

    private sealed class UpperSanitizer : IGameTextSanitizer
    {
        public string Sanitize(string text) => text.ToUpperInvariant();
    }

    [Theory]
    [InlineData("Alice", "Alice")]
    [InlineData("  Alice 🍐 ", "Alice (pear)")]
    [InlineData("🚀🚀", "Discord user")]
    [InlineData(null, "Discord user")]
    [InlineData("A Very Long Display Name That Keeps Going", "A Very Long Display Name That Ke")]
    public void ConvertName(string? input, string expected)
    {
        Assert.Equal(expected, DiscordToGameConverter.ConvertName(input));
    }
}
