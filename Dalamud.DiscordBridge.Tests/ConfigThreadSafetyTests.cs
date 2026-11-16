using Dalamud.DiscordBridge.Model;
using Dalamud.Game.Text;
using Newtonsoft.Json;

namespace Dalamud.DiscordBridge.Tests;

/// <summary>
/// Regression (#46): commands (gateway), the relay queue and the UI touch the config at the same time. Reading,
/// enumerating and serializing it while it changes must never throw.
/// </summary>
public class ConfigThreadSafetyTests
{
    private const int Iterations = 20_000;

    [Fact]
    public void OldJsonWithPlainDictionaries_StillLoads()
    {
        const string json = """
            {
              "ChannelConfigs": { "123": { "ChatTypes": [ 12, 13 ], "WebhookId": 5, "IsContentFinder": true } },
              "PrefixConfigs": { "FreeCompany": "🍐" },
              "CustomSlugsConfigs": { "12": "tell" },
              "ChatTypeAvatarURL": { "FreeCompany": "https://example.com/a.png" }
            }
            """;

        var config = JsonConvert.DeserializeObject<Configuration>(json)!;

        Assert.Equal([XivChatType.TellOutgoing, XivChatType.TellIncoming], config.ChannelConfigs[123].ChatTypes);
        Assert.Equal(5UL, config.ChannelConfigs[123].WebhookId);
        Assert.Equal("🍐", config.PrefixConfigs[XivChatType.FreeCompany]);
        Assert.Equal("tell", config.CustomSlugsConfigs[XivChatType.TellOutgoing]);
        Assert.Single(config.ChatTypeAvatarURL);
    }

    [Fact]
    public void RoundTrip_KeepsTheSameJsonShape()
    {
        var config = new Configuration();
        config.ChannelConfigs[42] = new DiscordChannelConfig { IsContentFinder = true };
        config.PrefixConfigs[XivChatType.Say] = "!";

        var json = JsonConvert.SerializeObject(config);
        var back = JsonConvert.DeserializeObject<Configuration>(json)!;

        Assert.Contains("\"ChannelConfigs\":{\"42\":", json);
        Assert.True(back.ChannelConfigs[42].IsContentFinder);
        Assert.Equal("!", back.PrefixConfigs[XivChatType.Say]);
    }

    [Fact]
    public async Task ChangingChatTypes_WhileTheRelayReadsThem_NeverThrows()
    {
        var channel = new DiscordChannelConfig();
        using var stop = new CancellationTokenSource();

        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                foreach (var type in channel.ChatTypes)
                    _ = ChatTypeGroups.Receives(channel, type);
                _ = ChatTypeGroups.Receives(channel, XivChatType.FreeCompany);
            }
        });

        for (var i = 0; i < Iterations; i++)
        {
            channel.SetUnique([XivChatType.FreeCompany, XivChatType.Ls1, (XivChatType)(i % 100)]);
            channel.UnsetUnique([XivChatType.Ls1, (XivChatType)(i % 100)]);
        }

        stop.Cancel();
        await reader; // rethrows "collection was modified" if it happened
        Assert.Equal([XivChatType.FreeCompany], channel.ChatTypes);
    }

    [Fact]
    public async Task ChangingChannels_WhileEnumeratingAndSerializing_NeverThrows()
    {
        var config = new Configuration();
        using var stop = new CancellationTokenSource();

        var readers = new[]
        {
            Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                    _ = config.ChannelConfigs.Where(kv => kv.Value.IsContentFinder).ToList();
            }),
            Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                    _ = JsonConvert.SerializeObject(config);
            }),
            Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                    _ = StatusAnnouncer.TargetChannels(config.ChannelConfigs);
            }),
        };

        for (var i = 0; i < Iterations; i++)
        {
            var id = (ulong)(i % 50);
            config.ChannelConfigs[id] = new DiscordChannelConfig { IsContentFinder = i % 2 == 0 };
            config.PrefixConfigs[(XivChatType)(i % 30)] = i.ToString();
            if (i % 3 == 0)
            {
                config.ChannelConfigs.TryRemove(id, out _);
                config.PrefixConfigs.TryRemove((XivChatType)(i % 30), out _);
            }
        }

        stop.Cancel();
        await Task.WhenAll(readers);
    }
}
