namespace Dalamud.DiscordBridge.Tests;

public class DuplicateFilterTests
{
    [Fact]
    public void IsNewer_ComparesInstantsNotOffsets()
    {
        // Same UTC offset (the old code compared .Offset, which is equal here, so it always picked "right").
        var earlier = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        var later = earlier.AddMilliseconds(250);

        Assert.True(DuplicateFilter.IsNewer(later, earlier));
        Assert.False(DuplicateFilter.IsNewer(earlier, later));
    }

    [Fact]
    public void IsNewer_DifferentOffsets_UsesActualTime()
    {
        var utc = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        // 13:00 at +02:00 is 11:00 UTC, so it's earlier even though its offset is larger.
        var plusTwo = new DateTimeOffset(2026, 11, 16, 13, 0, 0, TimeSpan.FromHours(2));

        Assert.True(DuplicateFilter.IsNewer(utc, plusTwo));
    }

    [Fact]
    public void GetAgeMs_ReturnsElapsedMilliseconds()
    {
        var ts = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(1500, DuplicateFilter.GetAgeMs(ts, ts.AddMilliseconds(1500)));
    }

    [Fact]
    public void IsDuplicate_SameChannelAuthorSlugText_IsDuplicate()
    {
        Assert.True(DuplicateFilter.IsDuplicate(1, "Alice＠Cactuar", "**[fc]** hello", 1, "Alice＠Cactuar", "🍐**[fc]** hello"));
        Assert.True(DuplicateFilter.IsDuplicate(1, "Alice＠Cactuar", "[fc] hello", 1, "Alice＠Cactuar", "🍐[fc] hello"));
    }

    [Fact]
    public void IsDuplicate_BoldAndPlainTag_AreTheSameMessage()
    {
        // A post from before #56 and its plain-tag repeat right after updating.
        Assert.True(DuplicateFilter.IsDuplicate(1, "Alice＠Cactuar", "[fc] hello", 1, "Alice＠Cactuar", "**[fc]** hello"));
    }

    [Theory]
    [InlineData(2ul, "Alice＠Cactuar", "**[fc]** hello")]   // different channel
    [InlineData(1ul, "Bob＠Cactuar", "**[fc]** hello")]     // different author
    [InlineData(1ul, "Alice＠Cactuar", "**[ls1]** hello")]  // different chat kind
    [InlineData(1ul, "Alice＠Cactuar", "**[fc]** hello!")]  // different text
    public void IsDuplicate_AnyDifference_IsNotDuplicate(ulong channel, string author, string content)
    {
        Assert.False(DuplicateFilter.IsDuplicate(1, "Alice＠Cactuar", "**[fc]** hello", channel, author, content));
    }

    [Theory]
    [InlineData(true, "[fc] hello", true)]                               // current plain tag (#56)
    [InlineData(true, "🍐[ls1] Ane Mora: hi", true)]
    [InlineData(true, "**[fc]** hello", true)]                           // older bold tag
    [InlineData(true, "🍐 **[ls1]** Ane Mora: hi", true)]
    [InlineData(false, "**[fc]** hello", false)]                         // channel the bridge doesn't use
    [InlineData(true, "New commit pushed to master", false)]              // GitHub-style webhook
    [InlineData(true, "**Build passed** in 2m", false)]                   // bold but not a relay
    [InlineData(true, "", false)]
    [InlineData(true, null, false)]
    public void IsCandidate_OnlyRelaysInConfiguredChannels(bool configured, string? content, bool expected)
    {
        Assert.Equal(expected, DuplicateFilter.IsCandidate(configured, content));
    }
}
