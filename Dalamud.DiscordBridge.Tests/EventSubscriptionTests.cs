using System.Text.RegularExpressions;

namespace Dalamud.DiscordBridge.Tests;

/// <summary>
/// Guards against leaked Dalamud event handlers: every "Event += Handler" in a plugin source file
/// must have a matching "Event -= Handler" (usually in Dispose).
/// </summary>
public class EventSubscriptionTests
{
    private static readonly Regex Subscription = new(@"(?<target>[\w\.]+)\s*(?<op>\+=|-=)\s*(?:this\.)?(?<handler>[\w\.]+)\s*;");

    private static string PluginSourceDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dalamud.DiscordBridge.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Dalamud.DiscordBridge");
    }

    public static TheoryData<string> FilesWithSubscriptions() => new() { "DiscordBridgePlugin.cs" };

    [Theory]
    [MemberData(nameof(FilesWithSubscriptions))]
    public void EverySubscription_HasMatchingUnsubscription(string file)
    {
        var source = File.ReadAllText(Path.Combine(PluginSourceDir(), file));
        var matches = Subscription.Matches(source).Select(m => (
            Op: m.Groups["op"].Value,
            Key: $"{Normalize(m.Groups["target"].Value)} {m.Groups["handler"].Value.Replace("this.", "")}")).ToList();

        var added = matches.Where(m => m.Op == "+=").Select(m => m.Key).ToHashSet();
        var removed = matches.Where(m => m.Op == "-=").Select(m => m.Key).ToHashSet();

        Assert.NotEmpty(added);
        Assert.Empty(added.Except(removed));
    }

    // "pluginInterface.UiBuilder.Draw" and "Service.Interface.UiBuilder.Draw" are the same event.
    private static string Normalize(string target) =>
        target.Replace("this.", "").Replace("Service.Interface.", "").Replace("pluginInterface.", "");
}
