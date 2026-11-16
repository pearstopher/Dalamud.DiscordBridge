namespace Dalamud.DiscordBridge.Tests;

/// <summary>The command table that replaced the long if-chain (#52).</summary>
public class DiscordCommandTableTests
{
    [Fact]
    public void Table_HasEveryCommand()
    {
        string[] expected =
        [
            "setchannel", "unsetchannel", "setprefix", "unsetprefix", "setchattypename", "unsetchattypename",
            "setduplicatems", "toggledf", "togglestatus", "toggleembed", "toggledefaultnameavatar", "togglesender",
            "setcfprefix", "setinbound", "setup", "selftesthere", "setinboundperm", "listchannel", "setavatar",
            "unsetavatar", "help",
        ];

        Assert.Equal(expected.Order(), DiscordHandler.Commands.Keys.Order());
    }

    [Fact]
    public void OnlyHelp_IsOpenToEveryone()
    {
        // ping/test are open too, but handled before the table.
        Assert.Equal(["help"], DiscordHandler.Commands.Where(kv => !kv.Value.OwnerOnly).Select(kv => kv.Key));
    }

    [Fact]
    public void Names_AreLowercaseWithoutSeparators_AndDontShadowPing()
    {
        foreach (var name in DiscordHandler.Commands.Keys)
        {
            Assert.Matches("^[a-z]+$", name);
            Assert.False(InboundRules.IsPingCommand("xl!" + name, "xl!"), name);
        }
    }

    [Fact]
    public void EveryHandler_IsSet()
    {
        Assert.All(DiscordHandler.Commands.Values, c => Assert.NotNull(c.Run));
    }

    [Fact]
    public void Dispatcher_ChecksOwnerBeforeRunning()
    {
        var source = DocumentationTests.HandlerSource;
        var check = source.IndexOf("if (command.OwnerOnly && !await EnsureOwner(message.Author, message.Channel))", StringComparison.Ordinal);
        var run = source.IndexOf("await command.Run(this, message, args);", StringComparison.Ordinal);

        Assert.True(check > 0 && check < run);
        Assert.DoesNotContain("args[0] == this.plugin.Config.DiscordBotPrefix + \"", source); // the old if-chain is gone
    }
}
