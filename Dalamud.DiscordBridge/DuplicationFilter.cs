using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Discord.WebSocket;
using Dalamud.Utility;
using Dalamud.Plugin.Services;

namespace Dalamud.DiscordBridge
{
    /// <summary>
    /// Dedupes already posted messages whenever a message is received.
    /// Useful when several players running the plugin relay the same chat into one channel.
    /// </summary>
    /// <remarks>
    /// <see cref="OnMessageReceived"/> runs on the Discord gateway, while <see cref="CheckAlreadySent"/> runs on the
    /// message queue task. All access to the recent-message set goes through <see cref="recentLock"/>, which is never
    /// held across an await.
    /// </remarks>
    public class DuplicateFilter
    {
        private static IPluginLog? Logger => Service.Logger;

        /// <summary>
        /// Constructs a <see cref="DuplicateFilter"/> to dedupe messages for the given socket client.
        /// </summary>
        /// <param name="plugin"></param>
        /// <param name="client">The socket client to monitor for duplicate messages.</param>
        public DuplicateFilter(DiscordBridgePlugin plugin, DiscordSocketClient client)
        {
            this.plugin = plugin;

            client.MessageReceived += OnMessageReceived;
        }

        #region Methods

        /// <summary>
        /// Check if an in-game chat was already sent to the server, either by this client or another.
        /// </summary>
        /// <param name="channel">The channel to be the destination.</param>
        /// <param name="slug">The identifier for the chat type.</param>
        /// <param name="displayName">The name of the player who sent the chat message.</param>
        /// <param name="chatText">The relevant text of the chat message.</param>
        /// <returns>Whether the chat was recently sent and should be prevented.</returns>
        public bool CheckAlreadySent(Discord.IChannel channel, string slug, string displayName, string chatText)
        {
            SocketMessage? recentMsg;
            lock (this.recentLock)
            {
                recentMsg = this.recentMessages.FirstOrDefault(msg =>
                {
                    var contentParser = new ContentParser(msg.Content);

                    return msg.Channel.Id == channel.Id &&
                           msg.Author.Username == displayName &&
                           contentParser.Slug == slug &&
                           contentParser.Text == chatText;
                });
            }

            if (recentMsg == null)
                return false;

            long msgDiff = GetAgeMs(recentMsg.Timestamp, DateTimeOffset.UtcNow);

            if (msgDiff < plugin.Config.DuplicateCheckMS)
            {
                Logger?.Verbose($"[FILTER] Filtered outgoing message as duplicate. Diff: {msgDiff}ms, Threshold: {plugin.Config.DuplicateCheckMS}ms");
                return true;
            }

            return false;
        }

        #endregion

        #region Pure helpers (unit tested)

        /// <summary>
        /// Two relayed messages are duplicates when they're in the same channel, from the same webhook name,
        /// with the same chat-kind slug and text.
        /// </summary>
        internal static bool IsDuplicate(ulong leftChannel, string leftAuthor, string leftContent,
                                         ulong rightChannel, string rightAuthor, string rightContent)
        {
            if (leftChannel != rightChannel || leftAuthor != rightAuthor)
                return false;

            var leftParsed = new ContentParser(leftContent);
            var rightParsed = new ContentParser(rightContent);

            return leftParsed.Slug == rightParsed.Slug && leftParsed.Text == rightParsed.Text;
        }

        /// <summary>
        /// A webhook message the filter may compare and delete (#43): in a configured channel, and in the relay format
        /// <c>{prefix}[slug] text</c> (or the older bold <c>**[slug]**</c>). Other bots' webhooks (GitHub, RSS…) are never touched.
        /// </summary>
        internal static bool IsCandidate(bool channelConfigured, string? content)
        {
            if (!channelConfigured || string.IsNullOrEmpty(content))
                return false;

            var parsed = new ContentParser(content);
            return parsed.Slug.Length > 0 && parsed.Text.Length > 0;
        }

        /// <summary>
        /// True if <paramref name="left"/> was posted after <paramref name="right"/>. Compares instants, not UTC offsets.
        /// </summary>
        internal static bool IsNewer(DateTimeOffset left, DateTimeOffset right) => left > right;

        internal static long GetAgeMs(DateTimeOffset timestamp, DateTimeOffset now)
            => now.ToUnixTimeMilliseconds() - timestamp.ToUnixTimeMilliseconds();

        #endregion

        #region Private Functions

        private async Task OnMessageReceived(SocketMessage message)
        {
            // Only relays in channels the bridge uses (#43): never other integrations' webhooks or unrelated channels.
            if (!message.Author.IsWebhook ||
                !IsCandidate(this.plugin.Config.ChannelConfigs.ContainsKey(message.Channel.Id), message.Content))
                return;

            lock (this.recentLock)
            {
                this.recentMessages.Add(message);
            }

            await Dedupe();
        }

        private async Task Dedupe()
        {
            var now = DateTimeOffset.UtcNow;
            SocketMessage[] filtered;

            lock (this.recentLock)
            {
                // Forget anything older than the duplicate window.
                this.recentMessages.RemoveWhere(m => GetAgeMs(m.Timestamp, now) >= plugin.Config.DuplicateCheckMS);
                filtered = this.recentMessages.ToArray();
            }

            // Compare every message to every other message (outside the lock: deleting awaits network I/O).
            var deleted = new HashSet<SocketMessage>();
            for (var outerIdx = 0; outerIdx < filtered.Length; outerIdx++)
            {
                var recent = filtered[outerIdx];

                for (var innerIdx = outerIdx + 1; innerIdx < filtered.Length; innerIdx++)
                {
                    var other = filtered[innerIdx];

                    if (deleted.Contains(recent) || deleted.Contains(other))
                        continue;

                    if (IsDuplicate(recent.Channel.Id, recent.Author.Username, recent.Content,
                                    other.Channel.Id, other.Author.Username, other.Content))
                    {
                        deleted.Add(await DeleteMostRecent(recent, other));
                    }
                }
            }

            if (deleted.Count == 0)
                return;

            lock (this.recentLock)
            {
                this.recentMessages.ExceptWith(deleted);
            }
        }

        /// <returns>The most recent of the two messages, which was deleted.</returns>
        private static async Task<SocketMessage> DeleteMostRecent(SocketMessage left, SocketMessage right)
        {
            SocketMessage target = IsNewer(left.Timestamp, right.Timestamp) ? left : right;

            _ = await TryDeleteAsync(target);

            return target;
        }

        /// <returns>Whether the message existed on the server and was deleted.</returns>
        private static async Task<bool> TryDeleteAsync(SocketMessage message)
        {
            try
            {
                await message.DeleteAsync();
                return true;
            }
            catch (Discord.Net.HttpException ex)
            {
                // 404 is expected if the message was already deleted (e.g. by another bridge user's filter).
                if (ex.HttpCode != HttpStatusCode.NotFound)
                {
                    Logger?.Error(ex, "[FILTER] Unexpected exception when attempting to delete a message.");
                    throw;
                }
            }

            return false;
        }

        #endregion

        #region Private Data

        private readonly DiscordBridgePlugin plugin;

        private readonly object recentLock = new();

        private readonly HashSet<SocketMessage> recentMessages = [];

        #endregion
    }
}
