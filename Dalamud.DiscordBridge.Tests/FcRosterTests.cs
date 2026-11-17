namespace Dalamud.DiscordBridge.Tests;

public class FcRosterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private const string Me = "Ane Mora＠Famfrit";

    private static FcRoster Loaded(params (string Name, string World, string? Avatar)[] members)
    {
        var roster = new FcRoster();
        Assert.True(roster.TryBeginLoad(Me, T0));
        roster.Replace(Me, members, T0);
        return roster;
    }

    [Fact]
    public void HyphenatedName_IsFound()
    {
        // The Lodestone search can't find "Astra-lune Elek"; the FC member list can (#55).
        var roster = Loaded(("Astra-lune Elek", "Famfrit", "https://img/astra.jpg"));

        Assert.True(roster.TryGet("Astra-lune Elek", "Famfrit", out var url));
        Assert.Equal("https://img/astra.jpg", url);
    }

    [Theory]
    [InlineData("Reaper&#39; Grimm")]
    [InlineData("Reaper’ Grimm")]
    [InlineData("reaper' grimm")]
    public void ApostropheEncodingAndCase_DontMatter(string lodestoneName)
    {
        var roster = Loaded((lodestoneName, "Famfrit", "https://img/reaper.jpg"));

        Assert.True(roster.TryGet("Reaper' Grimm", "Famfrit", out _));
    }

    [Fact]
    public void OtherWorld_OrNoPortrait_IsNotFound()
    {
        var roster = Loaded(("Bob Smith", "Famfrit", "https://img/bob.jpg"), ("No Face", "Famfrit", null));

        Assert.False(roster.TryGet("Bob Smith", "Gilgamesh", out _));
        Assert.False(roster.TryGet("No Face", "Famfrit", out _));
        Assert.Equal(1, roster.Count);
    }

    [Fact]
    public void Load_StartsOnce_ThenRefreshesHourly()
    {
        var roster = new FcRoster();
        Assert.True(roster.TryBeginLoad(Me, T0));
        Assert.False(roster.TryBeginLoad(Me, T0)); // already loading
        roster.Replace(Me, [("Bob Smith", "Famfrit", "u")], T0);

        Assert.False(roster.TryBeginLoad(Me, T0 + FcRoster.RefreshAfter - TimeSpan.FromSeconds(1)));
        Assert.True(roster.TryBeginLoad(Me, T0 + FcRoster.RefreshAfter));
        Assert.True(roster.TryGet("Bob Smith", "Famfrit", out _)); // old roster kept while refreshing
    }

    [Fact]
    public void FailedLoad_RetriesAfterRetryInterval()
    {
        var roster = new FcRoster();
        Assert.True(roster.TryBeginLoad(Me, T0)); // never completes

        Assert.False(roster.TryBeginLoad(Me, T0 + FcRoster.RetryAfter - TimeSpan.FromSeconds(1)));
        Assert.True(roster.TryBeginLoad(Me, T0 + FcRoster.RetryAfter));
    }

    [Fact]
    public void SwitchingCharacters_DropsTheOldRoster()
    {
        var roster = Loaded(("Bob Smith", "Famfrit", "u"));

        Assert.True(roster.TryBeginLoad("Other Char＠Gilgamesh", T0));
        Assert.False(roster.TryGet("Bob Smith", "Famfrit", out _));

        roster.Replace(Me, [("Bob Smith", "Famfrit", "u")], T0); // late result for the old character: ignored
        Assert.False(roster.TryGet("Bob Smith", "Famfrit", out _));
    }
}
