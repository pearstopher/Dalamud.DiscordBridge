using System.Text.RegularExpressions;

namespace Dalamud.DiscordBridge.Tests;

public class DiscordStateTests
{
    [Theory]
    [InlineData(DiscordState.None, DiscordStateEvent.Ready, DiscordState.Ready)]
    [InlineData(DiscordState.Ready, DiscordStateEvent.Disconnected, DiscordState.Disconnected)]
    [InlineData(DiscordState.Disconnected, DiscordStateEvent.Connected, DiscordState.Ready)]
    [InlineData(DiscordState.Disconnected, DiscordStateEvent.Ready, DiscordState.Ready)]
    [InlineData(DiscordState.None, DiscordStateEvent.Connected, DiscordState.None)]          // wait for Ready on first connect
    [InlineData(DiscordState.None, DiscordStateEvent.Disconnected, DiscordState.None)]
    [InlineData(DiscordState.TokenInvalid, DiscordStateEvent.Disconnected, DiscordState.TokenInvalid)]
    [InlineData(DiscordState.None, DiscordStateEvent.LoginRejected, DiscordState.TokenInvalid)]
    [InlineData(DiscordState.Ready, DiscordStateEvent.Connected, DiscordState.Ready)]
    public void Next(DiscordState current, DiscordStateEvent e, DiscordState expected)
    {
        Assert.Equal(expected, DiscordStates.Next(current, e));
    }

    [Fact]
    public void Ui_HasTextForEveryState()
    {
        var ui = File.ReadAllText(Path.Combine(DocumentationTests.Root, "Dalamud.DiscordBridge", "PluginUI.cs"));
        foreach (var state in Enum.GetNames<DiscordState>())
            Assert.Contains($"DiscordState.{state} =>", ui);
    }

    [Fact]
    public void EveryAsyncVoid_CatchesEverything()
    {
        // Regression (#48): async void handlers without try/catch could throw unobserved.
        foreach (var file in Directory.GetFiles(Path.Combine(DocumentationTests.Root, "Dalamud.DiscordBridge"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;
            var source = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(source, @"async void \w+\([^)]*\)\s*\{\s*(\S+)"))
                Assert.True(m.Groups[1].Value == "try", $"{Path.GetFileName(file)}: '{m.Value.Split('(')[0]}' must wrap its body in try/catch");
        }
    }

    [Fact]
    public void Dispose_LogoutHasATimeout()
    {
        var source = File.ReadAllText(Path.Combine(DocumentationTests.Root, "Dalamud.DiscordBridge", "DiscordHandler.cs"));

        Assert.DoesNotContain("LogoutAsync().GetAwaiter().GetResult()", source);
        Assert.Contains("Task.Run(this.socketClient.LogoutAsync).Wait(TimeSpan.FromSeconds(3))", source);
    }
}
