using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.DiscordBridge.Model;
using Dalamud.Game.Text;
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

            new DelegateSelfTest("env.discord-ready", "Environment", "Discord bot connected",
                _ => Task.FromResult(plugin.Discord.State == DiscordState.Ready
                    ? SelfTestResult.Pass("Connected")
                    : SelfTestResult.Fail($"Discord state is {plugin.Discord.State}. Check the token in {Constant.MainCommand}."))),

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

            new DelegateSelfTest("send.sanitizer", "Send as me", "Client SanitizeString keeps plain text and rejects game glyphs",
                async _ =>
                {
                    var (plain, glyph) = await Service.Framework.RunOnFrameworkThread(() =>
                        (InboundSender.Sanitize("hello world 123"), InboundSender.Sanitize("ab")));
                    if (plain != "hello world 123")
                        return SelfTestResult.Fail($"plain text was changed to '{plain}': every send would be rejected");
                    return glyph != "ab"
                        ? SelfTestResult.Pass("plain text kept, glyph removed")
                        : SelfTestResult.Warn("a private-use glyph passed sanitization (the converter strips these anyway)");
                }),

            new DelegateSelfTest("send.echo-roundtrip", "Send as me", "Send path via /e (local only): typed, echoed, swallowed by the loop guard (plan D2, D3)",
                async token =>
                {
                    if (!await LoggedIn())
                        return SelfTestResult.Skip("not logged in");

                    var tag = NewTag();
                    var watch = WatchChat(plugin, text => text.Contains(tag));
                    var outcome = await plugin.InboundSenderService.SendAsync(FakeMessage(XivChatType.Echo, isOwner: true), $"{tag} send test");
                    if (outcome.Result != InboundResult.Sent)
                        return SelfTestResult.Fail($"send returned {outcome.Result} ({outcome.Reason})");

                    var (seen, swallowed) = await watch.WaitAsync(TimeSpan.FromSeconds(5), token);
                    if (!seen)
                        return SelfTestResult.Fail("the /e line never appeared in chat");
                    return swallowed
                        ? SelfTestResult.Pass("sent, echoed, and not relayed to Discord")
                        : SelfTestResult.Fail("the echo was NOT swallowed: it would be relayed back to Discord");
                }),

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

            new DelegateSelfTest("discord.round-trip", "Discord", "Post to the self-test channel and see it come back through the gateway",
                token => DiscordRoundTrip(plugin, token), TimeSpan.FromSeconds(30)),
        ];

        private static async Task<SelfTestResult> DisplayLoopTest(DiscordBridgePlugin plugin, bool asEcho, CancellationToken token)
        {
            if (!await LoggedIn())
                return SelfTestResult.Skip("not logged in");
            if (!plugin.Config.InboundDisplayEnabled)
                return SelfTestResult.Skip($"\"Show Discord messages in game chat\" is off in {Constant.MainCommand}");

            var tag = NewTag();
            var watch = WatchChat(plugin, text => text.Contains(tag));
            var result = await plugin.InboundDisplayService.ShowAsync(FakeMessage(XivChatType.FreeCompany, isOwner: false),
                                                                      $"{tag} display test", asEcho);
            if (result != InboundResult.Shown)
                return SelfTestResult.Fail($"display returned {result}");

            var (seen, swallowed) = await watch.WaitAsync(TimeSpan.FromSeconds(3), token);
            if (!seen)
                return SelfTestResult.Warn("printed, but Print didn't raise ChatMessage, so no loop is possible (the guard entry just expires)");
            return swallowed
                ? SelfTestResult.Pass("shown and swallowed by the loop guard")
                : SelfTestResult.Fail("the printed line was NOT swallowed: it would be relayed back to Discord");
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
