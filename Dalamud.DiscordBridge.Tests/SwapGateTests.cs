namespace Dalamud.DiscordBridge.Tests;

public class SwapGateTests
{
    [Fact]
    public void TryBegin_SecondWhileBusy_IsRefused()
    {
        var gate = new SwapGate();

        Assert.True(gate.TryBegin());
        Assert.True(gate.IsBusy);
        Assert.False(gate.TryBegin());
    }

    [Fact]
    public void End_AllowsTheNextSwap()
    {
        var gate = new SwapGate();
        gate.TryBegin();

        gate.End();

        Assert.False(gate.IsBusy);
        Assert.True(gate.TryBegin());
    }

    [Fact]
    public void ParallelClicks_OnlyOneWins()
    {
        var gate = new SwapGate();
        var wins = 0;

        Parallel.For(0, 500, _ =>
        {
            if (gate.TryBegin())
                Interlocked.Increment(ref wins);
        });

        Assert.Equal(1, wins);
    }

    [Fact]
    public void Ui_NeverDisposesTheHandlerOnTheGameThread()
    {
        // Regression (#44): Save disposed the handler inside Draw (game thread), freezing the game for seconds.
        var source = File.ReadAllText(Path.Combine(DocumentationTests.Root, "Dalamud.DiscordBridge", "PluginUI.cs"));
        var save = source[source.IndexOf("ImGui.Button(\"Save\")", StringComparison.Ordinal)..];

        Assert.DoesNotContain("this.Plugin.Discord.Dispose()", source);
        Assert.True(save.IndexOf("Task.Run(", StringComparison.Ordinal) < save.IndexOf("old.Dispose()", StringComparison.Ordinal));
        Assert.Contains("ImGuiInputTextFlags.Password", source);
        Assert.DoesNotContain("togglesend", source);
    }
}
