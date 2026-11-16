namespace Dalamud.DiscordBridge.Tests;

public class SpecialCharsHandlerTests
{
    private readonly SpecialCharsHandler handler = new();

    [Fact]
    public void TransformToUnicode_PlainText_Unchanged()
    {
        Assert.Equal("hello world", handler.TransformToUnicode("hello world"));
    }

    [Fact]
    public void TransformToUnicode_AutoTranslateBrackets_UseFallbackEmoji()
    {
        Assert.Equal("🟩Hello🟥", handler.TransformToUnicode("Hello"));
    }

    [Fact]
    public void TransformToUnicode_HqMarker_UsesFallbackEmoji()
    {
        Assert.Equal("Potion❇", handler.TransformToUnicode("Potion"));
    }

    [Fact]
    public void TransformToUnicode_BoxedLetter_BecomesRegionalIndicatorWithZeroWidthSpace()
    {
        // U+E071 is boxed 'A' -> regional indicator A (U+1F1E6) + ZWSP.
        Assert.Equal("\U0001F1E6​", handler.TransformToUnicode(""));
    }

    [Theory]
    [InlineData('', "⓪")]
    [InlineData('', "①")]
    [InlineData('', "⑲")]
    [InlineData('', "㉑")]
    public void TransformToUnicode_NumberSquares_BecomeEnclosedNumbers(char xivChar, string expected)
    {
        Assert.Equal(expected, handler.TransformToUnicode(xivChar.ToString()));
    }

    [Fact]
    public void TransformToUnicode_RepeatedGlyph_ReplacesAll()
    {
        Assert.Equal("ʛ100 ʛ200", handler.TransformToUnicode("100 200"));
    }
}
