using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge.Tests;

public class XivChatTypeExtensionsTests
{
    [Theory]
    [InlineData(XivChatType.Say, "say")]
    [InlineData(XivChatType.FreeCompany, "fc")]
    [InlineData(XivChatType.Ls1, "ls1")]
    [InlineData(XivChatType.TellIncoming, "tell")]
    [InlineData(XivChatType.TellOutgoing, "tell")]
    public void GetSlug_KnownType_ReturnsSlug(XivChatType type, string slug)
    {
        Assert.Equal(slug, type.GetSlug());
    }

    [Fact]
    public void GetInfo_HighBitsSet_MasksTo7Bits()
    {
        var withHighBits = (XivChatType)((int)XivChatType.FreeCompany | 0x80);

        Assert.Equal("fc", withHighBits.GetSlug());
    }

    [Fact]
    public void GetInfo_UnknownType_Throws()
    {
        // 25 isn't a chat type the plugin maps.
        Assert.Throws<ArgumentException>(() => ((XivChatType)25).GetInfo());
    }

    [Fact]
    public void GetBySlug_RoundTripsForEveryUniqueSlug()
    {
        var uniqueSlugs = XivChatTypeExtensions.TypeInfoDict
            .GroupBy(kv => kv.Value.Slug)
            .Where(g => g.Count() == 1)
            .Select(g => g.Single());

        foreach (var (type, info) in uniqueSlugs)
            Assert.Equal(type, XivChatTypeExtensions.GetBySlug(info.Slug!));
    }

    [Fact]
    public void Slugs_AreLowercaseWithoutSpaces()
    {
        foreach (var info in XivChatTypeExtensions.TypeInfoDict.Values)
        {
            Assert.False(string.IsNullOrEmpty(info.Slug));
            Assert.Equal(info.Slug!.ToLowerInvariant(), info.Slug);
            Assert.DoesNotContain(' ', info.Slug);
        }
    }

    [Fact]
    public void IpcChatType_HasInfo()
    {
        Assert.NotNull(XivChatTypeExtensions.IpcChatType.GetInfo());
    }
}
