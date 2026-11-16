using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge.Tests;

public class PauseStatusTests
{
    [Theory]
    [InlineData(true, false, "Running")]
    [InlineData(true, true, "Paused")]
    [InlineData(false, false, "Off")]
    [InlineData(false, true, "Off")]
    public void StateOf(bool connected, bool paused, string expected)
    {
        Assert.Equal(expected, PauseStatus.StateOf(connected, paused).ToString());
    }

    [Fact]
    public void Glyphs_AreTheOwnersPlainCharacters()
    {
        var running = PauseStatus.Glyph(BridgeState.Running);
        var paused = PauseStatus.Glyph(BridgeState.Paused);
        var off = PauseStatus.Glyph(BridgeState.Off);

        Assert.Equal("↑↓", running);
        Assert.Equal("| |", paused);
        Assert.Equal("X", off);
        Assert.Equal(3, new[] { running, paused, off }.Distinct().Count());
    }

    [Fact]
    public void Click_TogglesWhileConnected_OpensSettingsWhenOff()
    {
        Assert.False(PauseStatus.ClickOpensSettings(BridgeState.Running));
        Assert.False(PauseStatus.ClickOpensSettings(BridgeState.Paused));
        Assert.True(PauseStatus.ClickOpensSettings(BridgeState.Off));
    }

    [Fact]
    public void Tooltips_SayWhatAClickDoes()
    {
        Assert.Contains("Click to pause", PauseStatus.Tooltip(BridgeState.Running));
        Assert.Contains("Click to resume", PauseStatus.Tooltip(BridgeState.Paused));
        Assert.Contains("open settings", PauseStatus.Tooltip(BridgeState.Off));
    }

    [Theory]
    [InlineData(true, true, "💤")]
    [InlineData(true, false, "💤")]
    [InlineData(false, true, "✅")]
    [InlineData(false, false, "⏸️")]
    public void PingReaction(bool paused, bool loggedIn, string expected)
    {
        Assert.Equal(expected, PauseStatus.PingReaction(paused, loggedIn));
    }

    [Fact]
    public void Texts_MentionResume()
    {
        Assert.Contains("xl!resume", PauseStatus.ChangeText(true, "Discord", "xl!"));
        Assert.Contains("xl!resume", PauseStatus.PingText(true, true, "xl!"));
        Assert.Contains("xl!resume", StatusAnnouncer.OnlineText("2.4.0.0", null, "xl!"));
        Assert.DoesNotContain("Paused", StatusAnnouncer.OnlineText("2.4.0.0", null));
    }

    [Fact]
    public void Paused_DefaultsOff_ForOldConfigs()
    {
        var config = Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>("{\"DiscordBotPrefix\":\"xl!\"}")!;

        Assert.False(config.Paused);
    }

    [Fact]
    public void EveryRelayPath_ChecksPause()
    {
        static string Source(string file) => File.ReadAllText(Path.Combine(DocumentationTests.Root, "Dalamud.DiscordBridge", file));
        var plugin = Source("DiscordBridgePlugin.cs");
        var chat = plugin[plugin.IndexOf("private void ChatOnOnChatMessage", StringComparison.Ordinal)..];
        var cf = plugin[plugin.IndexOf("private void ClientStateOnCfPop", StringComparison.Ordinal)..];

        // Game → Discord: after the echo guard (loop prevention still sees every line), before queueing.
        Assert.True(chat.IndexOf("this.EchoGuard.Consume(", StringComparison.Ordinal) < chat.IndexOf("if (this.Config.Paused) return;", StringComparison.Ordinal));
        Assert.True(chat.IndexOf("if (this.Config.Paused) return;", StringComparison.Ordinal) < chat.IndexOf("MessageQueue.Enqueue(", StringComparison.Ordinal));
        Assert.True(cf.IndexOf("this.Config.Paused", StringComparison.Ordinal) < cf.IndexOf("MessageQueue.Enqueue(", StringComparison.Ordinal));
        Assert.Contains("this.plugin.Config.Paused", Source("API/DiscordBridgeAPI.cs"));
        // Discord → game.
        Assert.Contains("PauseStatus.PausedReaction", DocumentationTests.HandlerSource);
    }
}
