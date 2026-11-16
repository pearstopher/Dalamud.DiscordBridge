using System.Reflection;
using System.Text.RegularExpressions;
using Dalamud.DiscordBridge.Model;

namespace Dalamud.DiscordBridge.Tests;

/// <summary>
/// Keeps the docs in sync with the code: a new command, setting, or source file fails these tests until it's documented.
/// </summary>
public class DocumentationTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dalamud.DiscordBridge.sln")))
            dir = dir.Parent;
        return dir!.FullName;
    }

    private static string Read(params string[] path) => File.ReadAllText(Path.Combine([Root, .. path]));

    [Theory]
    [InlineData("ping")]
    [InlineData("test")]
    public void PingCommand_IsDocumentedAndInHelp(string command)
    {
        Assert.Contains($"xl!{command}", Read("docs", "COMMANDS.md"));
        Assert.Contains("DiscordBotPrefix}ping", HandlerSource);
    }

    private static string HandlerSource => Read("Dalamud.DiscordBridge", "DiscordHandler.cs");

    /// <summary>Discord commands: every `DiscordBotPrefix + "name"` the handler reacts to.</summary>
    public static TheoryData<string> DiscordCommands()
    {
        var data = new TheoryData<string>();
        foreach (var name in Regex.Matches(HandlerSource, @"DiscordBotPrefix \+ ""([a-z]+)""").Select(m => m.Groups[1].Value).Distinct())
            data.Add(name);
        return data;
    }

    /// <summary>In-game commands from [Command] and [Aliases] attributes.</summary>
    public static TheoryData<string> GameCommands()
    {
        var data = new TheoryData<string> { Constant.MainCommand };
        var source = Read("Dalamud.DiscordBridge", "DiscordBridgePlugin.cs");
        foreach (Match m in Regex.Matches(source, @"\[(?:Command|Aliases)\(""(/[a-z]+)""\)\]"))
            data.Add(m.Groups[1].Value);
        return data;
    }

    public static TheoryData<string> Settings()
    {
        var data = new TheoryData<string>();
        foreach (var type in new[] { typeof(Configuration), typeof(DiscordChannelConfig) })
        {
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                data.Add(p.Name);
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                data.Add(f.Name);
        }

        return data;
    }

    public static TheoryData<string> SourceFiles()
    {
        var data = new TheoryData<string>();
        var plugin = Path.Combine(Root, "Dalamud.DiscordBridge");
        foreach (var file in Directory.GetFiles(plugin, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(plugin, file).Replace('\\', '/');
            if (!relative.StartsWith("obj/") && !relative.StartsWith("bin/"))
                data.Add(relative);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DiscordCommands))]
    public void DiscordCommand_IsInCommandReference(string command)
    {
        Assert.Contains($"xl!{command}", Read("docs", "COMMANDS.md"));
    }

    [Theory]
    [MemberData(nameof(DiscordCommands))]
    public void DiscordCommand_IsInHelpEmbed_UnlessDeveloperOnly(string command)
    {
        if (command is "help" or "selftesthere")
            return;

        Assert.Matches(new Regex($@"AddField\(\$""\{{this\.plugin\.Config\.DiscordBotPrefix\}}{command}[ ""]"), HandlerSource);
    }

    [Theory]
    [MemberData(nameof(GameCommands))]
    public void GameCommand_IsInCommandReference(string command)
    {
        Assert.Contains($"`{command}", Read("docs", "COMMANDS.md"));
    }

    [Theory]
    [MemberData(nameof(Settings))]
    public void Setting_IsInSettingsReference(string setting)
    {
        Assert.Contains($"`{setting}`", Read("docs", "SETTINGS.md"));
    }

    [Theory]
    [MemberData(nameof(SourceFiles))]
    public void SourceFile_IsInArchitecture(string relativePath)
    {
        var architecture = Read("docs", "ARCHITECTURE.md");
        var name = Path.GetFileName(relativePath);
        var folder = relativePath.Contains('/') ? relativePath[..(relativePath.IndexOf('/') + 1)] : null;

        Assert.True(
            architecture.Contains(name) || (folder != null && architecture.Contains($"`{folder}")) ||
            (relativePath.StartsWith("Model/Queued") && architecture.Contains("Model/Queued*Event.cs")),
            $"docs/ARCHITECTURE.md doesn't mention {relativePath} (or its folder)");
    }

    [Theory]
    [InlineData("README.md", "docs/COMMANDS.md")]
    [InlineData("README.md", "docs/SETTINGS.md")]
    [InlineData("docs/README.md", "COMMANDS.md")]
    [InlineData("docs/README.md", "SETTINGS.md")]
    public void References_AreLinked(string file, string link)
    {
        Assert.Contains(link, Read(file.Split('/')));
    }
}
