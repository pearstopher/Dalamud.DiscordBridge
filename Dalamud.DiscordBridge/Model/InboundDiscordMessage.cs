using System.Collections.Generic;
using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge.Model
{
    /// <summary>
    /// A Discord message that should be relayed into the game. It's a plain snapshot with no Discord.Net types, so
    /// the converter and display/send code can be unit tested.
    /// </summary>
    public record InboundDiscordMessage
    {
        public required ulong ChannelId { get; init; }
        public required ulong MessageId { get; init; }
        public required ulong AuthorId { get; init; }

        /// <summary>Server nickname if set, otherwise global display name, otherwise username.</summary>
        public required string AuthorName { get; init; }

        /// <summary>Whether the author is the configured plugin owner (see <c>InboundRules.IsOwner</c>).</summary>
        public required bool IsOwner { get; init; }

        /// <summary>In-game chat type this message is relayed into.</summary>
        public required XivChatType TargetType { get; init; }

        /// <summary>Raw content, with mention tokens such as &lt;@123&gt;.</summary>
        public string Content { get; init; } = string.Empty;

        /// <summary>Content with mentions resolved to names (Discord.Net's CleanContent).</summary>
        public string CleanContent { get; init; } = string.Empty;

        /// <summary>File names of attachments (images and files).</summary>
        public IReadOnlyList<InboundAttachment> Attachments { get; init; } = [];

        /// <summary>Names of stickers on the message.</summary>
        public IReadOnlyList<string> Stickers { get; init; } = [];

        /// <summary>Display name of the author of the message this one replies to, if any. For relayed messages this is
        /// the webhook name, e.g. "Name＠World".</summary>
        public string? ReplyToAuthor { get; init; }

        /// <summary>Discord ID of the message this one replies to, if any.</summary>
        public ulong? ReplyToMessageId { get; init; }

        /// <summary>Content of the replied-to message (if Discord provided it).</summary>
        public string? ReplyToContent { get; init; }

        /// <summary>Whether the replied-to message was posted by a webhook or bot (i.e. likely one of our relays).</summary>
        public bool ReplyToIsRelay { get; init; }

        /// <summary>For tells (<see cref="TargetType"/> TellOutgoing): who to send/show the tell to, "Name＠World".</summary>
        public string? TellTarget { get; init; }
    }

    /// <summary>An attachment on an inbound message.</summary>
    public record InboundAttachment(string FileName, bool IsImage);
}
