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
            config.SetChannel(id, new DiscordChannelConfig { IsContentFinder = i % 2 == 0 });
            config.ChangePrefixes(m => m[(XivChatType)(i % 30)] = i.ToString());
            if (i % 3 == 0)
            {
                config.RemoveChannel(id);
                config.ChangePrefixes(m => m.Remove((XivChatType)(i % 30)));
            }
        }

        stop.Cancel();
        await Task.WhenAll(readers);
    }

    [Fact]
    public void ParallelWriters_LoseNoChanges()
    {
        var config = new Configuration();

        Parallel.For(0, 1000, i => config.SetChannel((ulong)i, new DiscordChannelConfig()));
        Parallel.For(0, 1000, i => config.ChangePrefixes(m => m[(XivChatType)(i % 100)] = "x"));

        Assert.Equal(1000, config.ChannelConfigs.Count);
        Assert.Equal(100, config.PrefixConfigs.Count);
        Assert.True(config.RemoveChannel(5));
        Assert.False(config.RemoveChannel(5));
    }

    [Fact]
    public void PluginCode_NeverChangesSavedMapsInPlace()
    {
        // Only Configuration's copy-on-write helpers may change these maps (#46).
        var pattern = new System.Text.RegularExpressions.Regex(
            @"(ChannelConfigs|PrefixConfigs|CustomSlugsConfigs|ChatTypeAvatarURL)(\[[^\]]*\]\s*=[^=]|\.(Try)?Remove\(|\.Add\(|\.Clear\(|\.TryAdd\()");
        foreach (var file in Directory.GetFiles(Path.Combine(DocumentationTests.Root, "Dalamud.DiscordBridge"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.EndsWith("Configuration.cs"))
                continue;
            var match = pattern.Match(File.ReadAllText(file));
            Assert.False(match.Success, $"{Path.GetFileName(file)}: in-place change '{match.Value}'; use Config.SetChannel/RemoveChannel/Change*");
        }
    }
}
