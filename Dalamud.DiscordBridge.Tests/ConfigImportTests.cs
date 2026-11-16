using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge.Tests;

public class ConfigImportTests
{
    // Shape of a config written by the official plugin (synthetic values; never use a real config here).
    private const string LegacyJson = """
        {
          "$type": "Dalamud.DiscordBridge.Configuration, Dalamud.DiscordBridge",
          "DefaultAvatarURL": "https://example.com/default.png",
          "Version": 0,
          "DuplicateCheckMS": 3000,
          "DiscordToken": "fake.token.value",
          "DiscordOwnerName": "pearowner",
          "DiscordBotPrefix": "p!",
          "ChatTypeAvatarURL": {
            "$type": "System.Collections.Generic.Dictionary`2[[Dalamud.Game.Text.XivChatType, Dalamud],[System.String, System.Private.CoreLib]], System.Private.CoreLib",
            "FreeCompany": "https://example.com/fc.png"
          },
          "ChannelConfigs": {
            "$type": "System.Collections.Generic.Dictionary`2[[System.UInt64, System.Private.CoreLib],[Dalamud.DiscordBridge.Model.DiscordChannelConfig, Dalamud.DiscordBridge]], System.Private.CoreLib",
            "123456789012345678": {
              "$type": "Dalamud.DiscordBridge.Model.DiscordChannelConfig, Dalamud.DiscordBridge",
              "ChatTypes": {
                "$type": "System.Collections.Generic.List`1[[Dalamud.Game.Text.XivChatType, Dalamud]], System.Private.CoreLib",
                "$values": [ 24, 16 ]
              },
              "WebhookId": 42,
              "IsBidirectional": false,
              "IsContentFinder": true
            }
          },
          "PrefixConfigs": { "$type": "System.Collections.Generic.Dictionary`2[[Dalamud.Game.Text.XivChatType, Dalamud],[System.String, System.Private.CoreLib]], System.Private.CoreLib", "Say": "💬" },
          "CustomSlugsConfigs": {},
          "CFPrefixConfig": "@here",
          "ForceDefaultNameAvatar": false,
          "ForceEmbedFallbackMode": true,
          "SenderInMessage": true
        }
        """;

    [Fact]
    public void FromLegacyJson_ReadsAllSettingsDespiteOldTypeNames()
    {
        var config = ConfigImport.FromLegacyJson(LegacyJson);

        Assert.Equal("fake.token.value", config.DiscordToken);
        Assert.Equal("pearowner", config.DiscordOwnerName);
        Assert.Equal("p!", config.DiscordBotPrefix);
        Assert.Equal(3000, config.DuplicateCheckMS);
        Assert.Equal("https://example.com/default.png", config.DefaultAvatarURL);
        Assert.Equal("https://example.com/fc.png", config.ChatTypeAvatarURL[XivChatType.FreeCompany]);
        Assert.Equal("💬", config.PrefixConfigs[XivChatType.Say]);
        Assert.Equal("@here", config.CFPrefixConfig);
        Assert.True(config.ForceEmbedFallbackMode);
        Assert.True(config.SenderInMessage);

        var channel = Assert.Single(config.ChannelConfigs);
        Assert.Equal(123456789012345678ul, channel.Key);
        Assert.Equal([XivChatType.FreeCompany, XivChatType.Ls1], channel.Value.ChatTypes);
        Assert.Equal(42ul, channel.Value.WebhookId);
        Assert.True(channel.Value.IsContentFinder);
    }

    [Fact]
    public void FromLegacyJson_MissingFields_KeepDefaults()
    {
        var config = ConfigImport.FromLegacyJson("""{ "DiscordToken": "t" }""");

        Assert.Equal("t", config.DiscordToken);
        Assert.Equal("xl!", config.DiscordBotPrefix);
        Assert.Equal(5000, config.DuplicateCheckMS);
        Assert.Empty(config.ChannelConfigs);
    }

    [Fact]
    public void TryReadLegacy_MissingFile_ReturnsNull()
    {
        Assert.Null(ConfigImport.TryReadLegacy(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")));
    }

    [Fact]
    public void TryReadLegacy_CorruptFile_ReturnsNullAndReportsError()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        File.WriteAllText(path, "{ not json");
        Exception? reported = null;
        try
        {
            Assert.Null(ConfigImport.TryReadLegacy(path, ex => reported = ex));
            Assert.NotNull(reported);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LegacyConfigPath_IsSiblingNamedAfterOfficialPlugin()
    {
        Assert.Equal(
            Path.Combine(@"C:\cfg", "Dalamud.DiscordBridge.json"),
            ConfigImport.LegacyConfigPath(@"C:\cfg\DiscordChatPears.json"));
    }
}
