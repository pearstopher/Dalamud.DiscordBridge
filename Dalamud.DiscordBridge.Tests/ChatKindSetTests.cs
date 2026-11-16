using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge.Tests;

public class ChatKindSetTests
{
    [Fact]
    public void Any_IncludesBothTellDirections()
    {
        Assert.Contains(XivChatType.TellIncoming, DiscordHandler.DefaultChatTypes);
        Assert.Contains(XivChatType.TellOutgoing, DiscordHandler.DefaultChatTypes);
    }

    [Fact]
    public void All_IsEveryKnownKindExceptNone()
    {
        var all = DiscordHandler.AllChatTypes;

        Assert.DoesNotContain(XivChatType.None, all);
        Assert.Equal(XivChatTypeExtensions.TypeInfoDict.Count - 1, all.Length);
        Assert.Contains(XivChatType.SystemMessage, all);
        Assert.Contains(XivChatType.RetainerSale, all);
    }

    [Fact]
    public void All_IsASupersetOfAny()
    {
        Assert.Empty(DiscordHandler.DefaultChatTypes.Except(DiscordHandler.AllChatTypes));
    }
}
