using System;
using System.Collections.Generic;
using System.Text;
using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge
{
    /// <summary>
    /// Builds the chat-box line for "send as me" (pure, unit tested). The plugin always supplies the channel
    /// command itself; user text can never become a command of its own.
    /// </summary>
    internal static class GameChatCommand
    {
        private static readonly Dictionary<XivChatType, string> Prefixes = new()
        {
            [XivChatType.Say] = "/s",
            [XivChatType.Shout] = "/sh",
            [XivChatType.Yell] = "/y",
            [XivChatType.Party] = "/p",
            [XivChatType.Alliance] = "/a",
            [XivChatType.FreeCompany] = "/fc",
            [XivChatType.Ls1] = "/l1", [XivChatType.Ls2] = "/l2", [XivChatType.Ls3] = "/l3", [XivChatType.Ls4] = "/l4",
            [XivChatType.Ls5] = "/l5", [XivChatType.Ls6] = "/l6", [XivChatType.Ls7] = "/l7", [XivChatType.Ls8] = "/l8",
            [XivChatType.CrossLinkShell1] = "/cwl1", [XivChatType.CrossLinkShell2] = "/cwl2",
            [XivChatType.CrossLinkShell3] = "/cwl3", [XivChatType.CrossLinkShell4] = "/cwl4",
            [XivChatType.CrossLinkShell5] = "/cwl5", [XivChatType.CrossLinkShell6] = "/cwl6",
            [XivChatType.CrossLinkShell7] = "/cwl7", [XivChatType.CrossLinkShell8] = "/cwl8",
            [XivChatType.Echo] = "/e",
            [XivChatType.CustomEmote] = "/em",
        };

        private static readonly System.Text.RegularExpressions.Regex TellTargetPattern =
            new(@"^[A-Za-z][A-Za-z'\-]* [A-Za-z][A-Za-z'\-]*(@[A-Za-z]+)?$", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>"Bob Smith＠Gilgamesh" → "Bob Smith@Gilgamesh"; null if it isn't a valid FFXIV character name.</summary>
        internal static string? NormalizeTellTarget(string? target)
        {
            if (string.IsNullOrWhiteSpace(target))
                return null;

            var normalized = target.Trim().Replace('＠', '@');
            return TellTargetPattern.IsMatch(normalized) ? normalized : null;
        }

        /// <summary>The chat command for a channel, or null if sending to it isn't supported.</summary>
        internal static string? PrefixFor(XivChatType type) => Prefixes.TryGetValue(type, out var prefix) ? prefix : null;

        /// <summary>
        /// Build "/fc hello". Fails if the channel isn't supported, the text is empty, would start a command, contains
        /// line breaks, or the whole line is over the game's 500-byte limit.
        /// </summary>
        internal static bool TryBuild(XivChatType type, string text, out string line, out string reason)
            => TryBuild(type, text, null, out line, out reason);

        /// <param name="tellTarget">For tells: "First Last＠World" (or with a plain @), or "First Last" on your own world.</param>
        internal static bool TryBuild(XivChatType type, string text, string? tellTarget, out string line, out string reason)
        {
            line = string.Empty;
            reason = string.Empty;

            // Defense in depth: never anything GM-related, whatever the caller asked for.
            if (ReplyRouting.IsGmChannel(type))
            {
                reason = "the plugin never sends to GM channels";
                return false;
            }

            string prefix;
            if (type is XivChatType.TellOutgoing or XivChatType.TellIncoming)
            {
                if (NormalizeTellTarget(tellTarget) is not { } target)
                {
                    reason = string.IsNullOrWhiteSpace(tellTarget) ? "no one to send the tell to" : $"'{tellTarget}' isn't a valid character name";
                    return false;
                }

                prefix = $"/tell {target}";
            }
            else if (PrefixFor(type) is { } channelPrefix)
            {
                prefix = channelPrefix;
            }
            else
            {
                reason = $"sending to {type.GetFancyName()} isn't supported";
                return false;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                reason = "nothing to send";
                return false;
            }

            if (text.TrimStart().StartsWith('/'))
            {
                reason = "messages can't start with /";
                return false;
            }

            if (text.IndexOfAny(['\r', '\n']) >= 0)
            {
                reason = "messages can't contain line breaks";
                return false;
            }

            var candidate = $"{prefix} {text}";
            var bytes = Encoding.UTF8.GetByteCount(candidate);
            if (bytes > DiscordToGameConverter.GameMaxBytes)
            {
                reason = $"too long for game chat ({bytes - prefix.Length - 1} of {DiscordToGameConverter.GameMaxBytes - prefix.Length - 1} bytes)";
                return false;
            }

            line = candidate;
            return true;
        }
    }

    /// <summary>
    /// Spaces out "send as me" messages: at most one per <c>interval</c>, with a bounded backlog. Pure, clock injected.
    /// </summary>
    internal sealed class SendRateLimiter
    {
        private readonly Func<DateTimeOffset> clock;
        private readonly object gate = new();

        /// <summary>Slots in the future (messages waiting to be sent).</summary>
        private readonly List<DateTimeOffset> waiting = [];

        /// <summary>The most recently reserved slot, past or future.</summary>
        private DateTimeOffset? lastSlot;

        internal SendRateLimiter(TimeSpan interval, int maxQueued, Func<DateTimeOffset>? clock = null)
        {
            this.Interval = interval;
            this.MaxQueued = maxQueued;
            this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        internal TimeSpan Interval { get; set; }

        internal int MaxQueued { get; set; }

        /// <summary>
        /// Reserve the next send slot. Returns how long to wait before sending (zero = now), or null if the backlog is full.
        /// </summary>
        internal TimeSpan? TryReserve()
        {
            lock (this.gate)
            {
                var now = this.clock();
                this.waiting.RemoveAll(t => t <= now);

                var slot = this.lastSlot is { } last ? Max(now, last + this.Interval) : now;
                if (slot > now)
                {
                    if (this.waiting.Count >= this.MaxQueued)
                        return null;
                    this.waiting.Add(slot);
                }

                this.lastSlot = slot;
                return slot - now;
            }
        }

        private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
    }
}
