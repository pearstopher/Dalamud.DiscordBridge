using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.DiscordBridge.Model;
using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge
{
    /// <summary>Where a Discord reply should go in game: a chat type, plus the person for tells.</summary>
    public readonly record struct ReplyTarget(XivChatType Type, string? TellPartner)
    {
        public bool IsTell => Type is XivChatType.TellOutgoing or XivChatType.TellIncoming;
    }

    /// <summary>
    /// Remembers which in-game channel (and tell partner) each relayed Discord message came from, so replies can be
    /// routed back. Bounded and in memory; <see cref="ReplyRouting.Resolve"/> falls back to parsing the message.
    /// </summary>
    internal sealed class RelayedMessageIndex
    {
        private readonly int capacity;
        private readonly object gate = new();
        private readonly Dictionary<ulong, ReplyTarget> targets = [];
        private readonly Queue<ulong> order = new();

        internal RelayedMessageIndex(int capacity = 2000)
        {
            this.capacity = capacity;
        }

        internal int Count
        {
            get
            {
                lock (this.gate)
                    return this.targets.Count;
            }
        }

        internal void Record(ulong discordMessageId, ReplyTarget target)
        {
            if (discordMessageId == 0)
                return;

            lock (this.gate)
            {
                if (!this.targets.ContainsKey(discordMessageId))
                    this.order.Enqueue(discordMessageId);
                this.targets[discordMessageId] = target;

                while (this.order.Count > this.capacity)
                    this.targets.Remove(this.order.Dequeue());
            }
        }

        internal ReplyTarget? Find(ulong discordMessageId)
        {
            lock (this.gate)
                return this.targets.TryGetValue(discordMessageId, out var target) ? target : null;
        }
    }

    /// <summary>Pure reply-routing rules (unit tested).</summary>
    internal static class ReplyRouting
    {
        // "→ Bob＠World: text" or "Me＠World → Bob＠World: text" (ChatContentFormat.Body for outgoing tells).
        private static readonly Regex OutgoingTellBody = new(@"^(?:.*? )?→ (?<partner>[^:]+): ", RegexOptions.Compiled);

        /// <summary>
        /// The target for an outbound relay of <paramref name="chatType"/>: tells (either direction) become a tell to
        /// the other person; other types keep their type.
        /// </summary>
        internal static ReplyTarget ForRelay(XivChatType chatType, string? tellPartner)
            => chatType is XivChatType.TellOutgoing or XivChatType.TellIncoming
                ? new ReplyTarget(XivChatType.TellOutgoing, string.IsNullOrWhiteSpace(tellPartner) ? null : tellPartner)
                : new ReplyTarget(chatType, null);

        /// <summary>
        /// Where a reply should go, or null to use the channel's default (<c>setinbound</c>) target.
        /// </summary>
        /// <param name="message">The inbound Discord message (with its reply fields).</param>
        /// <param name="index">Recorded relays.</param>
        /// <param name="customSlugs">User-defined slug overrides (setchattypename), so they can be parsed back.</param>
        internal static ReplyTarget? Resolve(InboundDiscordMessage message, RelayedMessageIndex index,
                                             IReadOnlyDictionary<XivChatType, string>? customSlugs = null)
        {
            if (message.ReplyToMessageId is not { } replyId)
                return null;

            var target = index.Find(replyId) ?? Parse(message, customSlugs);
            return target is { } t && CanReplyTo(t) ? t : null;
        }

        /// <summary>Fallback when the relay isn't in memory (e.g. after a reload): read the relayed message itself.</summary>
        internal static ReplyTarget? Parse(InboundDiscordMessage message, IReadOnlyDictionary<XivChatType, string>? customSlugs)
        {
            if (!message.ReplyToIsRelay || string.IsNullOrEmpty(message.ReplyToContent))
                return null;

            var parsed = new ContentParser(message.ReplyToContent);
            if (string.IsNullOrEmpty(parsed.Slug) || SlugToType(parsed.Slug, customSlugs) is not { } type)
                return null;

            if (type is not (XivChatType.TellOutgoing or XivChatType.TellIncoming))
                return new ReplyTarget(type, null);

            var outgoing = OutgoingTellBody.Match(parsed.Text);
            var partner = outgoing.Success ? outgoing.Groups["partner"].Value.Trim() : message.ReplyToAuthor;
            return ForRelay(XivChatType.TellOutgoing, partner);
        }

        /// <summary>Chat types a reply can be sent or shown in: the inbound targets, plus tells to a known person.</summary>
        internal static bool CanReplyTo(ReplyTarget target)
            => target.IsTell ? !string.IsNullOrWhiteSpace(target.TellPartner) : InboundRules.AllowedTargets.Contains(target.Type);

        private static XivChatType? SlugToType(string slug, IReadOnlyDictionary<XivChatType, string>? customSlugs)
        {
            if (customSlugs != null)
            {
                foreach (var (type, custom) in customSlugs)
                {
                    if (custom == slug)
                        return Normalize(type);
                }
            }

            foreach (var (type, info) in XivChatTypeExtensions.TypeInfoDict)
            {
                if (info.Slug == slug)
                    return Normalize(type);
            }

            return null;
        }

        // Party and cross-party share the "p" slug; /p works for both. Tells share "tell".
        private static XivChatType Normalize(XivChatType type) => type switch
        {
            XivChatType.CrossParty => XivChatType.Party,
            XivChatType.TellIncoming => XivChatType.TellOutgoing,
            _ => type,
        };
    }
}
