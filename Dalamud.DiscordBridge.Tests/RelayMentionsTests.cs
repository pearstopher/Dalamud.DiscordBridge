using System.Text.RegularExpressions;

namespace Dalamud.DiscordBridge.Tests;

/// <summary>
/// Regression (#41): relayed game text could ping Discord roles/users. Every message the bot or its webhooks send must
/// pass <c>RelayMentions</c> (no pings).
/// </summary>
public class RelayMentionsTests
{
    private static string Source(string file) => File.ReadAllText(Path.Combine(DocumentationTests.Root, "Dalamud.DiscordBridge", file));

    [Fact]
    public void RelayMentions_AllowsNothing()
    {
        var mentions = DiscordHandler.RelayMentions;

        Assert.Equal(Discord.AllowedMentionTypes.None, mentions.AllowedTypes ?? Discord.AllowedMentionTypes.None);
        Assert.True(mentions.UserIds == null || mentions.UserIds.Count == 0);
        Assert.True(mentions.RoleIds == null || mentions.RoleIds.Count == 0);
    }

    [Theory]
    [InlineData("DiscordHandler.cs")]
    public void EverySend_PassesRelayMentions(string file)
    {
        var source = Source(file);
        var calls = Regex.Matches(source, @"SendMessageAsync\(");
        Assert.NotEmpty(calls);

        foreach (Match call in calls)
        {
            // The call runs until the end of its statement.
            var end = source.IndexOf(';', call.Index);
            var statement = source[call.Index..end];
            Assert.True(statement.Contains("allowedMentions: RelayMentions"),
                $"{file}: SendMessageAsync without allowedMentions: RelayMentions near: {statement[..Math.Min(120, statement.Length)]}");
        }
    }

    [Fact]
    public void NoCodeAllowsRoleUserOrEveryoneMentions()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(DocumentationTests.Root, "Dalamud.DiscordBridge"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;
            var source = File.ReadAllText(file);
            Assert.DoesNotMatch(@"AllowedMentionTypes\.(Roles|Users|Everyone)", source);
            Assert.DoesNotContain("AllowedMentions.All", source);
        }
    }
}
