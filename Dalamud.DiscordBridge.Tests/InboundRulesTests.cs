using Dalamud.DiscordBridge.Model;
using Dalamud.Game.Text;
using Newtonsoft.Json;

namespace Dalamud.DiscordBridge.Tests;

public class InboundRulesTests
{
    [Theory]
    [InlineData("fc", XivChatType.FreeCompany)]
    [InlineData("FC", XivChatType.FreeCompany)]
    [InlineData("ls3", XivChatType.Ls3)]
    [InlineData("cwls8", XivChatType.CrossLinkShell8)]
    [InlineData("p", XivChatType.Party)]
    [InlineData("say", XivChatType.Say)]
    [InlineData("e", XivChatType.Echo)]
    [InlineData("nn", XivChatType.NoviceNetwork)]
    public void TryParseTarget_AllowedSlug_ReturnsType(string slug, XivChatType expected)
    {
        Assert.True(InboundRules.TryParseTarget(slug, out var target, out _));
        Assert.Equal(expected, target);
    }

    [Theory]
    [InlineData("off")]
    [InlineData("none")]
    public void TryParseTarget_Off_ReturnsNullTarget(string arg)
    {
        Assert.True(InboundRules.TryParseTarget(arg, out var target, out _));
        Assert.Null(target);
    }

    [Theory]
    [InlineData("tell")]          // exists, but not an allowed target
    [InlineData("retainersale")]  // exists, not chat you can talk in
    [InlineData("bogus")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseTarget_Invalid_FailsWithMessage(string? arg)
    {
        Assert.False(InboundRules.TryParseTarget(arg, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void AllowedTargets_AllHaveSlugs()
    {
        foreach (var type in InboundRules.AllowedTargets)
            Assert.False(string.IsNullOrEmpty(type.GetSlug()));
    }

    [Fact]
    public void TryParsePermission_Owner()
    {
        Assert.True(InboundRules.TryParsePermission(["owner"], out var perm, out var role, out _));
        Assert.Equal(InboundPermission.OwnerOnly, perm);
        Assert.Null(role);
    }

    [Fact]
    public void TryParsePermission_Everyone()
    {
        Assert.True(InboundRules.TryParsePermission(["Everyone"], out var perm, out _, out _));
        Assert.Equal(InboundPermission.Everyone, perm);
    }

    [Theory]
    [InlineData("<@&123456>")]
    [InlineData("123456")]
    public void TryParsePermission_Role_ParsesMentionOrId(string roleArg)
    {
        Assert.True(InboundRules.TryParsePermission(["role", roleArg], out var perm, out var role, out _));
        Assert.Equal(InboundPermission.Role, perm);
        Assert.Equal(123456ul, role);
    }

    [Theory]
    [InlineData("")]
    [InlineData("role")]
    [InlineData("role @Members")]
    [InlineData("admins")]
    public void TryParsePermission_Invalid_Fails(string joinedArgs)
    {
        var args = joinedArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.False(InboundRules.TryParsePermission(args, out _, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Theory]
    [InlineData("pear", "0000", 1ul, "pear", true)]          // new-style username
    [InlineData("pear", "1234", 1ul, "pear#1234", true)]     // legacy tag
    [InlineData("pear", "0000", 42ul, "42", true)]           // user ID
    [InlineData("pear", "1234", 1ul, "pear", false)]         // legacy account, bare name doesn't match
    [InlineData("apple", "0000", 1ul, "pear", false)]
    [InlineData("pear", "0000", 1ul, "", false)]             // owner not configured
    public void IsOwner_MatchesConfiguredOwner(string user, string disc, ulong id, string configured, bool expected)
    {
        Assert.Equal(expected, InboundRules.IsOwner(user, disc, id, configured));
    }

    [Theory]
    [InlineData(InboundPermission.OwnerOnly, true, null, true)]
    [InlineData(InboundPermission.OwnerOnly, false, null, false)]
    [InlineData(InboundPermission.Everyone, false, null, true)]
    [InlineData(InboundPermission.Role, false, 7ul, true)]    // has role 7
    [InlineData(InboundPermission.Role, false, 8ul, false)]   // lacks role 8
    [InlineData(InboundPermission.Role, false, null, false)]  // role not configured
    [InlineData(InboundPermission.Role, true, 8ul, true)]     // owner always allowed
    public void IsAllowedSender(InboundPermission perm, bool isOwner, ulong? requiredRole, bool expected)
    {
        Assert.Equal(expected, InboundRules.IsAllowedSender(perm, isOwner, [5ul, 7ul], requiredRole));
    }

    [Theory]
    [InlineData(false, false, "hello", XivChatType.FreeCompany, true)]
    [InlineData(true, false, "hello", XivChatType.FreeCompany, false)]   // bot
    [InlineData(false, true, "hello", XivChatType.FreeCompany, false)]   // webhook (our own relays)
    [InlineData(false, false, "xl!help", XivChatType.FreeCompany, false)] // command
    [InlineData(false, false, "  xl!help", XivChatType.FreeCompany, false)]
    [InlineData(false, false, "", XivChatType.FreeCompany, true)]        // attachment-only message
    [InlineData(false, false, "hello", null, false)]                      // inbound off
    public void ShouldRelay(bool bot, bool webhook, string content, XivChatType? target, bool expected)
    {
        Assert.Equal(expected, InboundRules.ShouldRelay(bot, webhook, content, "xl!", target));
    }

    [Fact]
    public void Describe_Off()
    {
        Assert.Equal("Inbound (Discord → game): off", InboundRules.Describe(new DiscordChannelConfig()));
    }

    [Fact]
    public void Describe_RoleMode_MentionsRole()
    {
        var config = new DiscordChannelConfig
        {
            InboundChatType = XivChatType.FreeCompany,
            InboundPermission = InboundPermission.Role,
            InboundRoleId = 99,
        };

        Assert.Contains("<@&99>", InboundRules.Describe(config));
    }

    [Fact]
    public void ChannelConfig_OldJsonWithoutInboundFields_DefaultsToOff()
    {
        var config = JsonConvert.DeserializeObject<DiscordChannelConfig>("""{ "ChatTypes": [24], "WebhookId": 1, "IsBidirectional": true }""")!;

        Assert.Null(config.InboundChatType);
        Assert.Equal(InboundPermission.OwnerOnly, config.InboundPermission);
        Assert.Null(config.InboundRoleId);
    }

    [Fact]
    public void ChannelConfig_InboundFields_RoundTrip()
    {
        var original = new DiscordChannelConfig
        {
            InboundChatType = XivChatType.CrossLinkShell2,
            InboundPermission = InboundPermission.Role,
            InboundRoleId = 1234,
        };

        var copy = JsonConvert.DeserializeObject<DiscordChannelConfig>(JsonConvert.SerializeObject(original))!;

        Assert.Equal(XivChatType.CrossLinkShell2, copy.InboundChatType);
        Assert.Equal(InboundPermission.Role, copy.InboundPermission);
        Assert.Equal(1234ul, copy.InboundRoleId);
    }
}
