using Dalamud.Game.Text;
using Newtonsoft.Json;

namespace Dalamud.DiscordBridge.Tests;

/// <summary>
/// Dalamud saves plugin configs with Newtonsoft type metadata (<c>"$type"</c> on every object, including dictionaries)
/// and loads them with <c>TypeNameHandling.Objects</c>. A config saved by an older version must still load: changing the
/// declared type of a saved collection (e.g. Dictionary → ConcurrentDictionary) breaks this and loses every setting.
/// </summary>
public class DalamudConfigFormatTests
{
    /// <summary>
    /// The settings Dalamud's PluginConfigurations uses to save and load plugin configs. A copy, because Dalamud's own
    /// store can't run outside the game (PluginConfigurations.Save/LoadForType hang waiting for Dalamud services, and
    /// its serializer methods are internal). The in-game self-test config.dalamud-format checks the real thing by
    /// saving and reloading through Dalamud itself.
    /// </summary>
    internal static readonly JsonSerializerSettings DalamudSettings = new()
    {
        TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
        TypeNameHandling = TypeNameHandling.Objects,
    };

    /// <summary>Shape of a real DiscordChatPears.json written by v2.0/v2.1 (values made up).</summary>
    private const string SavedByV2 = """
        {
          "$type": "Dalamud.DiscordBridge.Configuration, DiscordChatPears",
          "DefaultAvatarURL": "https://example.com/logo.png",
          "Version": 0,
          "DuplicateCheckMS": 5000,
          "DiscordToken": "token",
          "DiscordOwnerName": "owner",
          "DiscordBotPrefix": "xl!",
          "ChatTypeAvatarURL": {
            "$type": "System.Collections.Generic.Dictionary`2[[Dalamud.Game.Text.XivChatType, Dalamud],[System.String, System.Private.CoreLib]], System.Private.CoreLib"
          },
          "ChannelConfigs": {
            "$type": "System.Collections.Generic.Dictionary`2[[System.UInt64, System.Private.CoreLib],[Dalamud.DiscordBridge.Model.DiscordChannelConfig, DiscordChatPears]], System.Private.CoreLib",
            "123": {
              "$type": "Dalamud.DiscordBridge.Model.DiscordChannelConfig, DiscordChatPears",
              "ChatTypes": [ 10, 12, 13 ],
              "WebhookId": 99,
              "IsContentFinder": false,
              "InboundChatType": 56
            }
          },
          "PrefixConfigs": {
            "$type": "System.Collections.Generic.Dictionary`2[[Dalamud.Game.Text.XivChatType, Dalamud],[System.String, System.Private.CoreLib]], System.Private.CoreLib",
            "FreeCompany": "🍐"
          },
          "CustomSlugsConfigs": {
            "$type": "System.Collections.Generic.Dictionary`2[[Dalamud.Game.Text.XivChatType, Dalamud],[System.String, System.Private.CoreLib]], System.Private.CoreLib"
          },
          "CFPrefixConfig": ""
        }
        """;

    [Fact]
    public void ConfigSavedByV2_LoadsWithDalamudSettings()
    {
        var config = JsonConvert.DeserializeObject<Configuration>(SavedByV2, DalamudSettings)!;

        Assert.Equal("owner", config.DiscordOwnerName);
        Assert.Equal([XivChatType.Say, XivChatType.TellOutgoing, XivChatType.TellIncoming], config.ChannelConfigs[123].ChatTypes);
        Assert.Equal(99UL, config.ChannelConfigs[123].WebhookId);
        Assert.Equal(XivChatType.Echo, config.ChannelConfigs[123].InboundChatType);
        Assert.Equal("🍐", config.PrefixConfigs[XivChatType.FreeCompany]);
    }

    [Fact]
    public void ConfigSavedNow_LoadsWithDalamudSettings()
    {
        var config = JsonConvert.DeserializeObject<Configuration>(SavedByV2, DalamudSettings)!;

        var json = JsonConvert.SerializeObject(config, DalamudSettings);
        var back = JsonConvert.DeserializeObject<Configuration>(json, DalamudSettings)!;

        Assert.Equal(99UL, back.ChannelConfigs[123].WebhookId);
        Assert.Equal("🍐", back.PrefixConfigs[XivChatType.FreeCompany]);
    }

    [Fact]
    public void SavedCollections_KeepTheirV2Types()
    {
        // The $type written for these must not change, or configs written now won't load in an older version
        // (and vice versa). Dictionary<,> is what v2.0 wrote.
        var json = JsonConvert.SerializeObject(new Configuration(), DalamudSettings);

        Assert.Contains("System.Collections.Generic.Dictionary`2[[System.UInt64", json);
        Assert.DoesNotContain("ConcurrentDictionary", json);
    }
}
