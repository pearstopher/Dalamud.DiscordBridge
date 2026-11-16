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
    public void SetEmotes_AllThreeFound_AllApplied()
    {
        handler.SetEmotes([("other", 1), ("xivAtl", 10), ("xivAtr", 11), ("xivHq", 12)]);

        Assert.Equal("<:xivAtl:10>Hi<:xivAtr:11> Potion<:xivHq:12>", handler.TransformToUnicode("Hi Potion"));
    }

    [Fact]
    public void SetEmotes_SomeMissing_KeepsFallbacksForMissing()
    {
        handler.SetEmotes([("xivHq", 12)]);

        Assert.Equal("🟩Hi🟥 Potion<:xivHq:12>", handler.TransformToUnicode("Hi Potion"));
    }

    [Fact]
    public void SetEmotes_DuplicateNames_FirstWins()
    {
        handler.SetEmotes([("xivHq", 1), ("xivHq", 2)]);

        Assert.Equal("<:xivHq:1>", handler.TransformToUnicode(""));
    }

    [Fact]
    public void SetEmotes_CalledAgainWithoutEmotes_RevertsToFallbacks()
    {
        handler.SetEmotes([("xivHq", 12)]);
        handler.SetEmotes([]);

        Assert.Equal("❇", handler.TransformToUnicode(""));
    }

    [Fact]
    public void TransformToUnicode_RepeatedGlyph_ReplacesAll()
    {
        Assert.Equal("ʛ100 ʛ200", handler.TransformToUnicode("100 200"));
    }
}
