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
            Assert.True(statement.Contains("allowedMentions: RelayMentions") || statement.Contains("allowedMentions: MentionsForPrefix(prefix)"),
                $"{file}: SendMessageAsync without RelayMentions/MentionsForPrefix near: {statement[..Math.Min(120, statement.Length)]}");
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
            Assert.DoesNotMatch(@"AllowedMentionTypes\.(Roles|Users)", source);
            Assert.DoesNotContain("AllowedMentions.All", source);
            if (!file.EndsWith("DiscordHandler.cs"))
                Assert.DoesNotContain("AllowedMentionTypes.Everyone", source);
        }

        // @everyone is only ever allowed by MentionsForPrefix, when the owner's own prefix contains it.
        Assert.Single(Regex.Matches(Source("DiscordHandler.cs"), @"AllowedMentionTypes\.Everyone"));
    }

    [Fact]
    public void ForOwnerPrefix_NoPrefix_AllowsNothing()
    {
        Assert.True(MentionPolicy.ForOwnerPrefix(null).None);
        Assert.True(MentionPolicy.ForOwnerPrefix("🍐 [FC]").None);
        Assert.Same(DiscordHandler.RelayMentions, DiscordHandler.MentionsForPrefix(""));
    }

    [Fact]
    public void ForOwnerPrefix_AllowsExactlyTheWrittenMentions()
    {
        var allowed = MentionPolicy.ForOwnerPrefix("<@123> <@!456> <@&789> <@123>");

        Assert.False(allowed.Everyone);
        Assert.Equal([123UL, 456UL], allowed.Users);
        Assert.Equal([789UL], allowed.Roles);
    }

    [Theory]
    [InlineData("@here")]
    [InlineData("@everyone duty!")]
    public void ForOwnerPrefix_HereOrEveryone_AllowsEveryone(string prefix)
    {
        Assert.True(MentionPolicy.ForOwnerPrefix(prefix).Everyone);
    }

    [Fact]
    public void MentionsForPrefix_BuildsAllowList()
    {
        var mentions = DiscordHandler.MentionsForPrefix("<@&789> @here");

        Assert.Equal(Discord.AllowedMentionTypes.Everyone, mentions.AllowedTypes);
        Assert.Equal([789UL], mentions.RoleIds);
        Assert.Empty(mentions.UserIds ?? []);
    }
}
