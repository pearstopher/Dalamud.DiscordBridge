using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.DiscordBridge.Model;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Game.Text.SeStringHandling;
using Discord;
using Discord.WebSocket;
using Lumina.Excel.Sheets;

namespace Dalamud.DiscordBridge.SelfTests
{
    /// <summary>
    /// The in-game self-tests. Each one maps to steps in docs/IN_GAME_TEST_PLAN.md (see the "Automated by" column).
    /// When you add a feature whose behaviour depends on the game or Discord, add a test here.
    ///
    /// Safety: nothing here ever sends chat to the game server. The only send test uses Echo (/e), which the client
    /// handles locally.
    /// </summary>
    internal static class GameSelfTests
    {
        private const string Prefix = "dcp-selftest";

        internal static IReadOnlyList<ISelfTest> Build(DiscordBridgePlugin plugin) =>
        [
            new DelegateSelfTest("env.build", "Environment", "Which build is running",
                _ => Task.FromResult(SelfTestResult.Pass(BuildInfo.Describe(Service.Interface.AssemblyLocation.FullName)))),

            new DelegateSelfTest("env.logged-in", "Environment", "Logged in to a character (game tests need it)",
                async _ => await LoggedIn()
                    ? SelfTestResult.Pass($"{plugin.cachedPlayerName}＠{plugin.cachedPlayerWorld}")
                    : SelfTestResult.Fail("Not logged in: log in to a character, then run again. Game tests will be skipped.")),

            new DelegateSelfTest("env.discord-ready", "Environment", "Discord bot connected, and /dcp's state matches the gateway (#48)",
                _ => Task.FromResult(plugin.Discord.State != DiscordState.Ready
                    ? SelfTestResult.Fail($"Discord state is {plugin.Discord.State}. Check the token in {Constant.MainCommand}.")
                    : plugin.Discord.IsConnected
                        ? SelfTestResult.Pass("Connected")
                        : SelfTestResult.Fail("/dcp says Connected but the gateway isn't connected (state machine out of sync)"))),

            new DelegateSelfTest("config.dalamud-format", "Environment", "Your settings survive a save and reload in Dalamud's config format (#46)",
                _ => Task.FromResult(ConfigRoundTrip(plugin.Config))),

            new DelegateSelfTest("env.owner", "Environment", "Owner configured (needed for commands and inbound permissions)",
                _ => Task.FromResult(string.IsNullOrWhiteSpace(plugin.Config.DiscordOwnerName)
                    ? SelfTestResult.Warn($"No owner set in {Constant.MainCommand}; Discord commands will be refused.")
                    : SelfTestResult.Pass(plugin.Config.DiscordOwnerName))),

            new DelegateSelfTest("data.item-sheet", "Game data", "Item sheet and icon URL used by retainer-sale posts",
                async _ =>
                {
                    var (name, icon) = await Service.Framework.RunOnFrameworkThread(() =>
                    {
                        var row = Service.Data.GetExcelSheet<Item>().GetRow(12537); // Titanium Ore
                        return (row.Name.ExtractText(), (uint)row.Icon);
                    });
                    var url = DiscordMessageQueue.ItemIconUrl(icon);
                    return !string.IsNullOrEmpty(name) && icon > 0 && url.StartsWith("https://")
                        ? SelfTestResult.Pass($"{name}, icon {icon}")
                        : SelfTestResult.Fail($"Unexpected item data: name '{name}', icon {icon}");
                }),

            new DelegateSelfTest("outbound.fc-login", "Game → Discord", "An FC login line with a real player link resolves to name, world and 🟢 (#53)",
                async _ =>
                {
                    if (!await LoggedIn())
                        return SelfTestResult.Skip("not logged in");

                    var (name, worldId) = await Service.Framework.RunOnFrameworkThread(() =>
                        (Service.PlayerState.CharacterName ?? string.Empty, Service.PlayerState.HomeWorld.RowId));
                    // Built locally, never printed or posted: just run the relay's own resolution on it.
                    var line = new SeStringBuilder().Add(new PlayerPayload(name, worldId)).AddText(name).Add(RawPayload.LinkTerminator).AddText(" has logged in.").Build();
                    var (who, world, text) = DiscordMessageQueue.ResolveFcLogin(line, new Dalamud.Game.Text.SeStringHandling.SeString(), plugin.cachedPlayerWorld, line.TextValue);

                    return who == name && world == plugin.cachedPlayerWorld && text == $"🟢 {name} has logged in."
                        ? SelfTestResult.Pass($"{who}＠{world}: {text}")
                        : SelfTestResult.Fail($"got {who}＠{world}: {text}");
                }),

            new DelegateSelfTest("outbound.tell-recipient", "Game → Discord", "Outgoing tell recipient is read from a player link as Name＠World (plan B10)",
                async _ =>
                {
                    if (!await LoggedIn())
                        return SelfTestResult.Skip("not logged in");

                    var (expected, actual) = await Service.Framework.RunOnFrameworkThread(() =>
                    {
                        var name = Service.PlayerState.CharacterName ?? string.Empty;
                        var world = Service.PlayerState.HomeWorld;
                        var sender = new Dalamud.Game.Text.SeStringHandling.SeString(
                            new Dalamud.Game.Text.SeStringHandling.Payloads.PlayerPayload(name, world.RowId));
                        return ($"{name}＠{world.Value.Name.ExtractText()}", DiscordMessageQueue.TellRecipient(sender));
                    });
                    return actual == expected
                        ? SelfTestResult.Pass(actual)
                        : SelfTestResult.Fail($"expected '{expected}', got '{actual}'");
                }),

            new DelegateSelfTest("ipc.api-version", "IPC", "DiscordBridge.APIVersion IPC answers",
                async _ =>
                {
                    var version = await Service.Framework.RunOnFrameworkThread(() =>
                        Service.Interface.GetIpcSubscriber<int>("DiscordBridge.APIVersion").InvokeFunc());
                    if (version != 1)
                        return SelfTestResult.Fail($"APIVersion returned {version}");
                    return plugin.DiscordBridgeProvider.OwnsIpc
                        ? SelfTestResult.Pass("APIVersion = 1 (provided by this plugin)")
                        : SelfTestResult.Warn("APIVersion = 1 but another plugin (official Discord Chat Bridge?) owns the IPC");
                }),

            new DelegateSelfTest("lodestone.portrait", "Lodestone", "NetStone finds your character's portrait (catches Lodestone layout changes)",
                async _ =>
                {
                    if (!await LoggedIn())
                        return SelfTestResult.Skip("not logged in");
                    if (!plugin.Discord.LodestoneReady)
                        return SelfTestResult.Skip("Lodestone client not started (Discord not connected yet)");

                    var avatar = await plugin.Discord.FindLodestoneAvatarAsync(plugin.cachedPlayerName, plugin.cachedPlayerWorld);
                    return avatar is { } url && url.StartsWith("https://")
                        ? SelfTestResult.Pass("portrait found")
                        : SelfTestResult.Fail($"No Lodestone result for {plugin.cachedPlayerName}＠{plugin.cachedPlayerWorld} (Lodestone layout change, maintenance, or private profile)");
                }, TimeSpan.FromSeconds(40)),

            new DelegateSelfTest("inbound.display.loop-guard", "Discord → game", "Shown line appears in chat and is NOT relayed back to Discord (plan C1, C10)",
                token => DisplayLoopTest(plugin, asEcho: false, token)),

            new DelegateSelfTest("inbound.display.echo-mode", "Discord → game", "Echo display mode is also swallowed by the loop guard (plan C8)",
                token => DisplayLoopTest(plugin, asEcho: true, token)),

            new DelegateSelfTest("send.sanitizer", "Send as me", "Typical converter output passes the client's chat sanitizer unchanged (otherwise sends are rejected)",
                async _ =>
                {
                    // Representative DiscordToGameConverter output: replies, ASCII emoji, placeholders, neutralized slash, non-English.
                    string[] samples =
                    [
                        "hello world 123",
                        "re @Bob: gg (+1) [image] [sticker: Wave]",
                        "and/or 50/50 (not a command)",
                        "café ñ ü — 日本語 テスト ~ !? <3 :D",
                        "see the guide (https://example.com/g?x=1&y=2)",
                    ];

                    var (changed, glyphKept) = await Service.Framework.RunOnFrameworkThread(() =>
                    {
                        var changedSamples = samples.Where(s => InboundSender.Sanitize(s) != s).ToList();
                        return (changedSamples, InboundSender.Sanitize("ab") == "ab");
                    });

                    if (changed.Count > 0)
                        return SelfTestResult.Fail($"the client sanitizer changes normal text, so these would be rejected: {string.Join(" | ", changed)}");

                    // Informational: the client allows its own private-use icons; the converter strips them before sending.
                    return SelfTestResult.Pass($"{samples.Length} samples unchanged (game icons {(glyphKept ? "allowed by client; stripped by converter" : "removed by client")})");
                }),

            new DelegateSelfTest("send.echo-roundtrip", "Send as me", "Send path via /e (local only): typed, echoed, swallowed by the loop guard (plan D2, D3)",
                async token =>
                {
                    if (!await LoggedIn())
                        return SelfTestResult.Skip("not logged in");

                    var tag = NewTag();
                    var sentTag = SentMarker.Tag(plugin.Config.SentTag);
                    var watch = WatchChat(plugin, text => text.Contains(tag) && !text.StartsWith(sentTag));
                    var marked = WatchChat(plugin, text => text.StartsWith(sentTag) && text.Contains(tag));
                    var outcome = await plugin.InboundSenderService.SendAsync(FakeMessage(XivChatType.Echo, isOwner: true), $"{tag} send test");
                    if (outcome.Result != InboundResult.Sent)
                        return SelfTestResult.Fail($"send returned {outcome.Result} ({outcome.Reason})");

                    var (seen, swallowed) = await watch.WaitAsync(TimeSpan.FromSeconds(5), token);
                    if (!seen)
                        return SelfTestResult.Fail("the /e line never appeared in chat");
                    if (!swallowed)
                        return SelfTestResult.Fail("the echo was NOT swallowed: it would be relayed back to Discord");

                    if (!plugin.Config.MarkSentInGame)
                        return SelfTestResult.Pass("sent, echoed, and not relayed to Discord (sent-tag display is off)");
                    var (markedSeen, markedSwallowed) = await marked.WaitAsync(TimeSpan.FromSeconds(3), token);
                    return markedSeen && markedSwallowed
                        ? SelfTestResult.Pass($"sent, echoed, shown as \"{sentTag} …\", nothing relayed to Discord")
                        : SelfTestResult.Fail(markedSeen ? $"the {sentTag} copy was relayed to Discord" : $"no {sentTag} copy appeared");
                }),

            new DelegateSelfTest("send.tell-self", "Send as me", "Opt-in: a real tell to yourself through the send path; both copies swallowed (plan D3)",
                async token =>
                {
                    if (!plugin.Config.DevSelfTestTellSelf)
                        return SelfTestResult.Skip("opt-in: tick \"Self-tests may send a tell to myself\" (it reaches the server, only to you)");
                    if (!await LoggedIn())
                        return SelfTestResult.Skip("not logged in");

                    var self = await Service.Framework.RunOnFrameworkThread(() =>
                        $"{Service.PlayerState.CharacterName}＠{Service.PlayerState.HomeWorld.Value.Name.ExtractText()}");
                    var tag = NewTag();
                    var copies = new List<(XivChatType Type, bool Swallowed)>();
                    void Observer(XivChatType type, string text, bool swallowed)
                    {
                        if (text.Contains(tag))
                            lock (copies) copies.Add((type, swallowed));
                    }

                    plugin.ChatObserver += Observer;
                    try
                    {
                        var message = FakeMessage(XivChatType.TellOutgoing, isOwner: true) with { TellTarget = self };
                        var outcome = await plugin.InboundSenderService.SendAsync(message, $"{tag} tell test");
                        if (outcome.Result != InboundResult.Sent)
                            return SelfTestResult.Fail($"send returned {outcome.Result} ({outcome.Reason})");

                        // Two real copies (outgoing + incoming), plus a tagged re-show of each when [S] marking is on.
                        var expected = plugin.Config.MarkSentInGame ? 4 : 2;
                        var deadline = DateTime.UtcNow.AddSeconds(8);
                        while (DateTime.UtcNow < deadline && copies.Count < expected)
                            await Task.Delay(100, token);
                    }
                    finally
                    {
                        plugin.ChatObserver -= Observer;
                    }

                    lock (copies)
                    {
                        if (copies.Count == 0)
                            return SelfTestResult.Fail($"the tell to {self} never appeared in chat");
                        if (copies.Any(c => !c.Swallowed))
                            return SelfTestResult.Fail($"not swallowed: {string.Join(", ", copies.Where(c => !c.Swallowed).Select(c => c.Type))}; it would be relayed back to Discord");
                        return SelfTestResult.Pass($"tell to {self}: {copies.Count} copies ({string.Join(", ", copies.Select(c => c.Type))}), all swallowed");
                    }
                }, TimeSpan.FromSeconds(15)),

            new DelegateSelfTest("send.slash-rejected", "Send as me", "Text starting with / is rejected and never executed (plan D5)",
                async _ =>
                {
                    if (!await LoggedIn())
                        return SelfTestResult.Skip("not logged in");

                    var outcome = await plugin.InboundSenderService.SendAsync(FakeMessage(XivChatType.Echo, isOwner: true), "/logout");
                    await Task.Delay(500);
                    if (!await LoggedIn())
                        return SelfTestResult.Fail("logged out!");
                    return outcome.Result == InboundResult.Rejected
                        ? SelfTestResult.Pass($"rejected: {outcome.Reason}")
                        : SelfTestResult.Fail($"expected Rejected, got {outcome.Result}");
                }),

            new DelegateSelfTest("discord.channels", "Discord", "Every configured channel is reachable; webhook permission per channel",
                async _ =>
                {
                    if (plugin.Discord.State != DiscordState.Ready)
                        return SelfTestResult.Skip("Discord not connected");
                    var ids = plugin.Config.ChannelConfigs.Keys.ToList();
                    if (ids.Count == 0)
                        return SelfTestResult.Skip("no channels configured");

                    var missing = new List<ulong>();
                    var noWebhook = new List<string>();
                    var summary = new List<string>();
                    foreach (var id in ids)
                    {
                        var (channel, route) = await plugin.Discord.ResolveDeliveryAsync(id, plugin.Config.ForceEmbedFallbackMode);
                        if (channel == null)
                        {
                            missing.Add(id);
                            continue;
                        }

                        summary.Add($"#{channel.Name}: {route}");
                        if (route == DiscordHandler.DeliveryRoute.MissingWebhookPermission)
                            noWebhook.Add($"#{channel.Name}");
                    }

                    if (missing.Count > 0)
                        return SelfTestResult.Fail($"unreachable channel IDs: {string.Join(", ", missing)}");
                    if (noWebhook.Count > 0)
                        return SelfTestResult.Warn($"missing Manage Webhooks in {string.Join(", ", noWebhook)} (posts fall back to embeds)");
                    return SelfTestResult.Pass(string.Join("; ", summary));
                }, TimeSpan.FromSeconds(30)),

            new DelegateSelfTest("discord.reply-routing", "Discord", "Real relay messages read back from Discord route replies correctly (plan D8)",
                token => ReplyRoutingTest(plugin, token), TimeSpan.FromSeconds(40)),

            new DelegateSelfTest("discord.status-online", "Discord", "The online status post fired once this load (#34, plan A6)",
                _ => Task.FromResult(!plugin.Config.PostStatusMessages
                    ? SelfTestResult.Warn("PostStatusMessages is off (xl!togglestatus); nothing is posted.")
                    : plugin.Status.OnlineAnnounced
                        ? SelfTestResult.Pass($"Posted to {StatusAnnouncer.TargetChannels(plugin.Config.ChannelConfigs).Count} channel(s); check they show it once.")
                        : SelfTestResult.Fail("Discord connected but no online post was attempted this load."))),

            new DelegateSelfTest("ui.server-info-bar", "UI", "The server info bar entry exists and shows the current state (#54)",
                async _ =>
                {
                    var (text, shown) = await Service.Framework.RunOnFrameworkThread(() => plugin.DtrSnapshot);
                    var expected = PauseStatus.StateOf(plugin.Discord.State == DiscordState.Ready, plugin.Config.Paused);
                    if (text == null)
                        return SelfTestResult.Fail("no server info bar entry");
                    return shown == expected && text == PauseStatus.Glyph(expected)
                        ? SelfTestResult.Pass($"{expected}: \"{text}\"")
                        : SelfTestResult.Fail($"shows {shown?.ToString() ?? "nothing"}, expected {expected}");
                }),

            new DelegateSelfTest("discord.round-trip", "Discord", "Post to the self-test channel and see it come back through the gateway",
                token => DiscordRoundTrip(plugin, token), TimeSpan.FromSeconds(30)),

            new DelegateSelfTest("discord.webhook-reuse", "Discord", "The self-test channel's webhook client is created once and reused (#40)",
                _ => WebhookReuseTest(plugin), TimeSpan.FromSeconds(20)),

            new DelegateSelfTest("discord.no-pings", "Discord", "A relayed line containing a mention doesn't ping anyone (#41)",
                token => NoPingsTest(plugin, token), TimeSpan.FromSeconds(30)),

            new DelegateSelfTest("discord.reaction", "Discord", "Reacting to a just-received message works from the gateway cache (#50)",
                token => ReactionTest(plugin, token), TimeSpan.FromSeconds(30)),

            new DelegateSelfTest("discord.ping", "Discord", "xl!ping's reaction matches your login state (#37)",
                token => PingTest(plugin, token), TimeSpan.FromSeconds(30)),

            new DelegateSelfTest("lodestone.cache", "Lodestone", "A second portrait lookup comes from the cache, instantly (#47)",
                async _ =>
                {
                    if (!plugin.Discord.LodestoneReady)
                        return SelfTestResult.Skip("Lodestone client not ready yet");
                    if (string.IsNullOrEmpty(plugin.cachedPlayerName) || string.IsNullOrEmpty(plugin.cachedPlayerWorld))
                        return SelfTestResult.Skip("not logged in");

                    var first = await plugin.Discord.LookupAvatarAsync(plugin.cachedPlayerName, plugin.cachedPlayerWorld);
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    var second = await plugin.Discord.LookupAvatarAsync(plugin.cachedPlayerName, plugin.cachedPlayerWorld);
                    watch.Stop();

                    if (first != second)
                        return SelfTestResult.Fail("the two lookups disagreed");
                    return watch.ElapsedMilliseconds < 50
                        ? SelfTestResult.Pass($"{(first == null ? "miss" : "portrait")} cached; second lookup {watch.ElapsedMilliseconds} ms")
                        : SelfTestResult.Fail($"second lookup took {watch.ElapsedMilliseconds} ms (not cached?)");
                }, TimeSpan.FromSeconds(20)),
        ];

        private static async Task<SelfTestResult> DisplayLoopTest(DiscordBridgePlugin plugin, bool asEcho, CancellationToken token)
        {
            if (!await LoggedIn())
                return SelfTestResult.Skip("not logged in");
            if (!plugin.Config.InboundDisplayEnabled)
                return SelfTestResult.Skip($"\"Show Discord messages in game chat\" is off in {Constant.MainCommand}");

            var tag = NewTag();
            var watch = WatchChat(plugin, text => text.Contains(tag));
            // Tell-styled and clearly labelled: this line is printed locally only (IChatGui.Print), never sent.
            var fake = FakeMessage(XivChatType.TellOutgoing, isOwner: false) with { TellTarget = "local test" };
            var result = await plugin.InboundDisplayService.ShowAsync(fake, $"{tag} display test (only visible to you)", asEcho);
            if (result != InboundResult.Shown)
                return SelfTestResult.Fail($"display returned {result}");

            var (seen, swallowed) = await watch.WaitAsync(TimeSpan.FromSeconds(3), token);
            if (!seen)
                return SelfTestResult.Warn("printed, but Print didn't raise ChatMessage, so no loop is possible (the guard entry just expires)");
            return swallowed
                ? SelfTestResult.Pass("shown and swallowed by the loop guard")
                : SelfTestResult.Fail("the printed line was NOT swallowed: it would be relayed back to Discord");
        }

        /// <summary>
        /// Save the live settings with Dalamud and load them back with Dalamud's own loader (the same calls as a plugin
        /// load), then compare. Reimplementing the loader here doesn't work: inside the game, the plugin's types live in
        /// the plugin's own load context, which only Dalamud's loader resolves.
        /// </summary>
        private static SelfTestResult ConfigRoundTrip(Configuration config)
        {
            try
            {
                config.Save();
                var text = File.ReadAllText(Service.Interface.ConfigFile.FullName);
                if (text.Contains("ConcurrentDictionary"))
                    return SelfTestResult.Fail("saved with a collection type older versions can't load");

                if (Service.Interface.GetPluginConfig() is not Configuration back)
                    return SelfTestResult.Fail("Dalamud couldn't load the saved settings: the next plugin load would start empty");
                if (ReferenceEquals(back, config))
                    return SelfTestResult.Warn("Dalamud returned the live object instead of reloading the file; nothing to compare");

                if (back.ChannelConfigs.Count != config.ChannelConfigs.Count || back.PrefixConfigs.Count != config.PrefixConfigs.Count ||
                    back.DiscordOwnerName != config.DiscordOwnerName ||
                    back.ChannelConfigs.Any(kv => !config.ChannelConfigs.TryGetValue(kv.Key, out var live) || !live.ChatTypes.SequenceEqual(kv.Value.ChatTypes)))
                    return SelfTestResult.Fail("settings changed after a save and reload");

                return SelfTestResult.Pass($"{config.ChannelConfigs.Count} channel(s), {config.PrefixConfigs.Count} prefix(es) saved and reloaded by Dalamud");
            }
            catch (Exception ex)
            {
                return SelfTestResult.Fail($"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>The self-test channel, or a Skip result explaining why there isn't one.</summary>
        private static async Task<(IMessageChannel? Channel, DiscordHandler.DeliveryRoute Route, SelfTestResult? Skip)> SelfTestChannel(DiscordBridgePlugin plugin)
        {
            if (plugin.Config.SelfTestChannelId is not { } channelId)
                return (null, default, SelfTestResult.Skip($"no self-test channel; run {plugin.Config.DiscordBotPrefix}selftesthere in a private channel to enable"));
            if (plugin.Discord.State != DiscordState.Ready)
                return (null, default, SelfTestResult.Skip("Discord not connected"));

            var (channel, route) = await plugin.Discord.ResolveDeliveryAsync(channelId, forceEmbed: false);
            return channel == null
                ? (null, default, SelfTestResult.Fail($"self-test channel {channelId} not reachable"))
                : (channel, route, null);
        }

        /// <summary>Post as the bot and wait for the gateway to deliver it back (so it's in the message cache).</summary>
        private static async Task<SocketMessage?> PostAndObserve(DiscordBridgePlugin plugin, IMessageChannel channel, string content, CancellationToken token)
        {
            var arrived = new TaskCompletionSource<SocketMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Observer(SocketMessage m)
            {
                if (m.Content == content)
                    arrived.TrySetResult(m);
            }

            plugin.Discord.MessageObserved += Observer;
            try
            {
                await channel.SendMessageAsync(content, allowedMentions: DiscordHandler.RelayMentions);
                var finished = await Task.WhenAny(arrived.Task, Task.Delay(TimeSpan.FromSeconds(15), token));
                return finished == arrived.Task ? arrived.Task.Result : null;
            }
            finally
            {
                plugin.Discord.MessageObserved -= Observer;
            }
        }

        private static async Task<SelfTestResult> WebhookReuseTest(DiscordBridgePlugin plugin)
        {
            var (channel, route, skip) = await SelfTestChannel(plugin);
            if (skip is { } skipped)
                return skipped;
            if (route != DiscordHandler.DeliveryRoute.Webhook)
                return SelfTestResult.Skip($"self-test channel uses {route}, not a webhook");

            var first = await plugin.Discord.GetOrCreateWebhookClient((ITextChannel)channel!);
            var second = await plugin.Discord.GetOrCreateWebhookClient((ITextChannel)channel!);
            if (first == null)
                return SelfTestResult.Fail("couldn't get or create the webhook");
            return ReferenceEquals(first, second)
                ? SelfTestResult.Pass("same client both times")
                : SelfTestResult.Fail("a new webhook client was created for the second message (leak)");
        }

        private static async Task<SelfTestResult> NoPingsTest(DiscordBridgePlugin plugin, CancellationToken token)
        {
            var (channel, route, skip) = await SelfTestChannel(plugin);
            if (skip is { } skipped)
                return skipped;
            if (route != DiscordHandler.DeliveryRoute.Webhook || await plugin.Discord.GetOrCreateWebhookClient((ITextChannel)channel!) is not { } webhook)
                return SelfTestResult.Skip($"self-test channel uses {route}, not a webhook");

            // Mention the bot itself (harmless) the way a player could type it in game, and send it like a relay.
            var tag = NewTag();
            var content = $"**[say]** <@{plugin.Discord.BotUserId}> {tag} mention test (safe to ignore; deleted automatically)";
            var arrived = new TaskCompletionSource<SocketMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Observer(SocketMessage m)
            {
                if (m.Content.Contains(tag))
                    arrived.TrySetResult(m);
            }

            plugin.Discord.MessageObserved += Observer;
            try
            {
                var id = await webhook.SendMessageAsync(content, username: Constant.WebhookName, allowedMentions: DiscordHandler.MentionsForPrefix(null));
                var finished = await Task.WhenAny(arrived.Task, Task.Delay(TimeSpan.FromSeconds(15), token));
                try { await webhook.DeleteMessageAsync(id); } catch { /* best effort */ }
                if (finished != arrived.Task)
                    return SelfTestResult.Fail("the test message never came back through the gateway");

                var message = arrived.Task.Result;
                return message.MentionedUsers.Count == 0
                    ? SelfTestResult.Pass("Discord reports no pinged users")
                    : SelfTestResult.Fail($"Discord reports {message.MentionedUsers.Count} pinged user(s): relayed text can still ping");
            }
            finally
            {
                plugin.Discord.MessageObserved -= Observer;
            }
        }

        private static async Task<SelfTestResult> ReactionTest(DiscordBridgePlugin plugin, CancellationToken token)
        {
            var (channel, _, skip) = await SelfTestChannel(plugin);
            if (skip is { } skipped)
                return skipped;

            var seen = await PostAndObserve(plugin, channel!, $"🍐 {Constant.PluginName} reaction test {NewTag()} (safe to ignore; deleted automatically)", token);
            if (seen == null)
                return SelfTestResult.Fail("the test message never came back through the gateway");
            try
            {
                if ((channel as ISocketMessageChannel)?.GetCachedMessage(seen.Id) == null)
                    return SelfTestResult.Warn("the message wasn't in the gateway cache; reactions fall back to fetching it");

                await plugin.Discord.ReactAsync(channel!.Id, seen.Id, "✅");
                return await WaitForOwnReaction(channel, seen.Id, "✅", token)
                    ? SelfTestResult.Pass("reacted ✅ using the cached message")
                    : SelfTestResult.Fail("no ✅ reaction on the message (Add Reactions permission?)");
            }
            finally
            {
                try { await seen.DeleteAsync(); } catch { /* best effort */ }
            }
        }

        /// <summary>
        /// Whether the bot's reaction shows up on the message within 5 s. The message comes from Discord.Net's cache,
        /// which only learns about the reaction when Discord's "reaction added" event arrives, a moment after the
        /// reaction call returns. Checking once raced that event and failed now and then.
        /// </summary>
        private static async Task<bool> WaitForOwnReaction(IMessageChannel channel, ulong messageId, string emoji, CancellationToken token)
        {
            var until = DateTimeOffset.UtcNow.AddSeconds(5);
            while (true)
            {
                var message = await channel.GetMessageAsync(messageId);
                if (message?.Reactions.Any(r => r.Key.Name == emoji && r.Value.IsMe) == true)
                    return true;
                if (DateTimeOffset.UtcNow >= until)
                    return false;
                await Task.Delay(250, token);
            }
        }

        private static async Task<SelfTestResult> PingTest(DiscordBridgePlugin plugin, CancellationToken token)
        {
            var (channel, _, skip) = await SelfTestChannel(plugin);
            if (skip is { } skipped)
                return skipped;

            var seen = await PostAndObserve(plugin, channel!, $"🍐 {Constant.PluginName} ping test {NewTag()} (safe to ignore; deleted automatically)", token);
            if (seen == null)
                return SelfTestResult.Fail("the test message never came back through the gateway");
            try
            {
                var expected = InboundRules.PingReaction(await LoggedIn());
                await plugin.Discord.AnswerPingAsync(seen);
                return await WaitForOwnReaction(channel!, seen.Id, expected, token)
                    ? SelfTestResult.Pass($"reacted {expected}")
                    : SelfTestResult.Fail($"expected a {expected} reaction (or the bot replied 'Pong' instead: missing Add Reactions permission?)");
            }
            finally
            {
                try { await seen.DeleteAsync(); } catch { /* best effort */ }
            }
        }

        private static async Task<SelfTestResult> DiscordRoundTrip(DiscordBridgePlugin plugin, CancellationToken token)
        {
            if (plugin.Config.SelfTestChannelId is not { } channelId)
                return SelfTestResult.Skip($"no self-test channel; run {plugin.Config.DiscordBotPrefix}selftesthere in a private channel to enable");
            if (plugin.Discord.State != DiscordState.Ready)
                return SelfTestResult.Skip("Discord not connected");

            var (channel, route) = await plugin.Discord.ResolveDeliveryAsync(channelId, forceEmbed: false);
            if (channel == null)
                return SelfTestResult.Fail($"self-test channel {channelId} not reachable");

            var tag = NewTag();
            var arrived = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sentAt = DateTimeOffset.UtcNow;
            void Observer(SocketMessage m)
            {
                if (m.Content.Contains(tag))
                    arrived.TrySetResult((long)(DateTimeOffset.UtcNow - sentAt).TotalMilliseconds);
            }

            plugin.Discord.MessageObserved += Observer;
            try
            {
                var content = $"🍐 {Constant.PluginName} self-test {tag} (safe to ignore; deleted automatically)";
                Func<Task> cleanup;
                if (route == DiscordHandler.DeliveryRoute.Webhook && await plugin.Discord.GetOrCreateWebhookClient((ITextChannel)channel) is { } webhook)
                {
                    var id = await webhook.SendMessageAsync(content, username: Constant.WebhookName, allowedMentions: AllowedMentions.None);
                    cleanup = () => webhook.DeleteMessageAsync(id);
                }
                else
                {
                    var sent = await channel.SendMessageAsync(content, allowedMentions: AllowedMentions.None);
                    cleanup = () => sent.DeleteAsync();
                }

                var finished = await Task.WhenAny(arrived.Task, Task.Delay(TimeSpan.FromSeconds(15), token));
                try { await cleanup(); } catch { /* best effort */ }

                return finished == arrived.Task
                    ? SelfTestResult.Pass($"#{channel.Name} via {route}: came back in {arrived.Task.Result} ms")
                    : SelfTestResult.Fail($"posted to #{channel.Name} via {route} but it never came back through the gateway (Message Content intent off?)");
            }
            finally
            {
                plugin.Discord.MessageObserved -= Observer;
            }
        }

        /// <summary>
        /// Posts relay-formatted webhook messages to the self-test channel, fetches them back from Discord (REST), and
        /// runs them through the same reply-field extraction and routing as a real reply, both from the in-memory index
        /// and by parsing. Deletes the messages afterwards.
        /// </summary>
        private static async Task<SelfTestResult> ReplyRoutingTest(DiscordBridgePlugin plugin, CancellationToken token)
        {
            if (plugin.Config.SelfTestChannelId is not { } channelId)
                return SelfTestResult.Skip($"no self-test channel; run {plugin.Config.DiscordBotPrefix}selftesthere in a private channel to enable");
            if (plugin.Discord.State != DiscordState.Ready)
                return SelfTestResult.Skip("Discord not connected");

            var (channel, route) = await plugin.Discord.ResolveDeliveryAsync(channelId, forceEmbed: false);
            if (channel == null)
                return SelfTestResult.Fail($"self-test channel {channelId} not reachable");
            if (route != DiscordHandler.DeliveryRoute.Webhook || await plugin.Discord.GetOrCreateWebhookClient((ITextChannel)channel) is not { } webhook)
                return SelfTestResult.Skip($"self-test channel uses {route}, not a webhook");

            var me = string.IsNullOrEmpty(plugin.cachedPlayerName) ? "Test Player＠World" : $"{plugin.cachedPlayerName}＠{plugin.cachedPlayerWorld}";
            var tag = NewTag();
            var cases = new (string Content, ReplyTarget? Expected)[]
            {
                // null = must be blocked (GM, retainer sale): never routed, never the default.
                ($"**[gmtell]** {tag} reply test GM", null),
                ($"**[retainersale]** {tag} reply test R", null),
                ($"**[say]** {tag} reply test A", new ReplyTarget(XivChatType.Say, null)),
                ($"**[ls1]** {tag} reply test B", new ReplyTarget(XivChatType.Ls1, null)),
                ($"**[tell]** → Bob Smith＠Gilgamesh: {tag} reply test C", new ReplyTarget(XivChatType.TellOutgoing, "Bob Smith＠Gilgamesh")),
                ($"**[tell]** {tag} reply test D", new ReplyTarget(XivChatType.TellOutgoing, me)),
                ($"**[customemote]**  {tag} reply test E", new ReplyTarget(XivChatType.CustomEmote, null)),
            };

            var posted = new List<ulong>();
            var failures = new List<string>();
            try
            {
                for (var i = 0; i < cases.Length; i++)
                {
                    var id = await webhook.SendMessageAsync(cases[i].Content, username: me, allowedMentions: AllowedMentions.None);
                    posted.Add(id);
                }

                for (var i = 0; i < cases.Length; i++)
                {
                    var fetched = await channel.GetMessageAsync(posted[i]);
                    var fields = DiscordHandler.ReplyFieldsOf(fetched, posted[i]);
                    var draft = FakeMessage(XivChatType.FreeCompany, isOwner: true) with
                    {
                        ReplyToMessageId = fields.MessageId,
                        ReplyToAuthor = fields.Author,
                        ReplyToContent = fields.Content,
                        ReplyToIsRelay = fields.IsRelay,
                    };

                    // Parsed from the message itself (as after a reload)...
                    var parsed = ReplyRouting.Decide(draft, new RelayedMessageIndex(), plugin.Config.CustomSlugsConfigs);
                    if (!Matches(parsed, cases[i].Expected))
                        failures.Add($"case {i + 1} parsed → {parsed.Kind} {parsed.Target?.ToString() ?? parsed.Reason} (relay={fields.IsRelay}, author='{fields.Author}', content='{fields.Content}')");

                    // ...and from the in-memory index (as right after relaying), for routable cases.
                    if (cases[i].Expected is { } expected)
                    {
                        var index = new RelayedMessageIndex();
                        index.Record(posted[i], expected);
                        var indexed = ReplyRouting.Decide(draft, index, plugin.Config.CustomSlugsConfigs);
                        if (!Matches(indexed, expected))
                            failures.Add($"case {i + 1} indexed → {indexed.Kind} {indexed.Target?.ToString() ?? indexed.Reason}");
                    }
                }
            }
            finally
            {
                foreach (var id in posted)
                {
                    try { await webhook.DeleteMessageAsync(id); } catch { /* best effort */ }
                }
            }

            return failures.Count == 0
                ? SelfTestResult.Pass($"{cases.Length} relay messages in #{channel.Name}: replies routed correctly, GM and retainer replies blocked")
                : SelfTestResult.Fail(string.Join(" | ", failures));
        }

        private static bool Matches(ReplyDecision decision, ReplyTarget? expected)
            => expected is { } e ? decision.Kind == ReplyKind.Route && decision.Target == e : decision.Kind == ReplyKind.Blocked;

        private static InboundDiscordMessage FakeMessage(XivChatType target, bool isOwner) => new()
        {
            ChannelId = 0, MessageId = 0, AuthorId = 0, AuthorName = "SelfTest", IsOwner = isOwner, TargetType = target,
        };

        private static string NewTag() => $"{Prefix}-{Guid.NewGuid().ToString("N")[..8]}";

        private static Task<bool> LoggedIn() => Service.Framework.RunOnFrameworkThread(() => Service.State.IsLoggedIn);

        /// <summary>Watch chat for a line matching <paramref name="match"/>; reports whether the echo guard swallowed it.</summary>
        private static ChatWatch WatchChat(DiscordBridgePlugin plugin, Func<string, bool> match) => new(plugin, match);

        private sealed class ChatWatch
        {
            private readonly DiscordBridgePlugin plugin;
            private readonly Func<string, bool> match;
            private readonly TaskCompletionSource<bool> seen = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly Action<XivChatType, string, bool> observer;

            internal ChatWatch(DiscordBridgePlugin plugin, Func<string, bool> match)
            {
                this.plugin = plugin;
                this.match = match;
                this.observer = (_, text, swallowed) =>
                {
                    if (this.match(text))
                        this.seen.TrySetResult(swallowed);
                };
                plugin.ChatObserver += this.observer;
            }

            internal async Task<(bool Seen, bool Swallowed)> WaitAsync(TimeSpan timeout, CancellationToken token)
            {
                try
                {
                    var finished = await Task.WhenAny(this.seen.Task, Task.Delay(timeout, token));
                    return finished == this.seen.Task ? (true, this.seen.Task.Result) : (false, false);
                }
                finally
                {
                    this.plugin.ChatObserver -= this.observer;
                }
            }
        }
    }
}
