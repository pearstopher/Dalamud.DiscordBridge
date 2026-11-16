using OnlineStatus = FFXIVClientStructs.FFXIV.Client.UI.Info.InfoProxyCommonList.CharacterData.OnlineStatus;

namespace Dalamud.DiscordBridge.Tests;

public class FriendListProbeTests
{
    [Fact]
    public void StatusOf_OnlineFlag_IsOnline()
    {
        Assert.Equal("online", FriendListProbe.StatusOf((ulong)(OnlineStatus.Online | OnlineStatus.Busy), false));
    }

    [Fact]
    public void StatusOf_OtherServerWithoutOnline_IsUnknown()
    {
        Assert.StartsWith("unknown", FriendListProbe.StatusOf(0, true));
    }

    [Fact]
    public void StatusOf_NoFlags_IsOffline()
    {
        Assert.Equal("offline", FriendListProbe.StatusOf(0, false));
    }

    [Fact]
    public void Describe_DifferentCurrentWorld_ShowsBoth()
    {
        var friend = new FriendListProbe.Friend("Ane Mora", "Famfrit", "Gilgamesh", (ulong)OnlineStatus.Online, false, false);

        Assert.Equal("3. Ane Mora@Famfrit (on Gilgamesh): online (flags 0x800000000000)", FriendListProbe.Describe(3, friend));
    }

    [Fact]
    public void Describe_PendingAndSameWorld_NoWorldSuffix()
    {
        var friend = new FriendListProbe.Friend("Bob Smith", "Famfrit", "Famfrit", 0, false, true);

        Assert.Equal("1. Bob Smith@Famfrit: offline [request pending] (flags 0x0)", FriendListProbe.Describe(1, friend));
    }
}
