namespace Dalamud.DiscordBridge.Tests;

public class AvatarCacheTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Unknown_MustBeLookedUp()
    {
        Assert.False(new AvatarCache().TryGet("Ane Mora＠Famfrit", T0, out _));
    }

    [Fact]
    public void Hit_IsKeptForTheSession()
    {
        var cache = new AvatarCache();
        cache.AddHit("a", "https://img/a.jpg");

        Assert.True(cache.TryGet("a", T0.AddDays(30), out var url));
        Assert.Equal("https://img/a.jpg", url);
    }

    [Fact]
    public void Miss_IsRememberedUntilItExpires()
    {
        var cache = new AvatarCache();
        cache.AddMiss("a", T0, AvatarCache.NotFoundTtl);

        Assert.True(cache.TryGet("a", T0.AddMinutes(59), out var url));
        Assert.Null(url);
        Assert.False(cache.TryGet("a", T0.AddHours(1), out _));
    }

    [Fact]
    public void HitAfterMiss_Wins()
    {
        var cache = new AvatarCache();
        cache.AddMiss("a", T0, AvatarCache.NotFoundTtl);
        cache.AddHit("a", "https://img/a.jpg");

        Assert.True(cache.TryGet("a", T0, out var url));
        Assert.Equal("https://img/a.jpg", url);
    }

    [Fact]
    public void LookupTimeout_IsShort()
    {
        Assert.InRange(DiscordHandler.LodestoneTimeout, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Handler_LooksUpPortraitsOnlyThroughTheCache()
    {
        // Regression (#47): misses weren't cached, so each message from an unknown character repeated a slow search.
        var source = File.ReadAllText(Path.Combine(DocumentationTests.Root, "Dalamud.DiscordBridge", "DiscordHandler.cs"));

        Assert.DoesNotContain("CachedResponses", source);
        Assert.Contains(".WaitAsync(LodestoneTimeout)", source);
        Assert.Contains("this.LookupAvatarAsync(senderName, senderWorld)", source);
    }
}

public class LodestoneNameMatchTests
{
    [Theory]
    [InlineData("Reaper&#39; Grimm")]
    [InlineData("Reaper’ Grimm")]
    [InlineData("reaper' grimm")]
    [InlineData("Reaper' Grimm")]
    public void ApostropheEncodingAndCase_DontMatter(string lodestoneName)
        => Assert.True(DiscordHandler.SameName(lodestoneName, "Reaper' Grimm"));

    [Fact]
    public void DifferentNames_DontMatch()
    {
        Assert.False(DiscordHandler.SameName("Grimm Reaper-", "Reaper' Grimm"));
        Assert.False(DiscordHandler.SameName(null, "Reaper' Grimm"));
    }
}
