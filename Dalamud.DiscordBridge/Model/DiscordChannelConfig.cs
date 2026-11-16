using System;
using System.Collections.Generic;
using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge.Model
{
    public class DiscordChannelConfig
    {
        public List<XivChatType> ChatTypes { get; set; } = new List<XivChatType>();
        public ulong WebhookId { get; set; }

        [Obsolete("Never used by any version; superseded by InboundChatType. Kept so old configs still deserialize.")]
        public bool IsBidirectional { get; set; }

        public bool IsContentFinder { get; set; }

        /// <summary>
        /// In-game chat type that Discord messages from this channel are relayed into. Null means inbound relay is off.
        /// </summary>
        public XivChatType? InboundChatType { get; set; }

        /// <summary>Who may relay Discord messages from this channel into the game.</summary>
        public InboundPermission InboundPermission { get; set; } = InboundPermission.OwnerOnly;

        /// <summary>Role required when <see cref="InboundPermission"/> is <see cref="InboundPermission.Role"/>.</summary>
        public ulong? InboundRoleId { get; set; }

        /// <summary>
        /// No longer used: sending is controlled only by <c>Configuration.AllowSendAsCharacter</c> in /dcp (#33).
        /// Kept so older configs still load.
        /// </summary>
        [Obsolete("Sending is controlled only by Configuration.AllowSendAsCharacter (in game).")]
        public bool InboundSendAsOwner { get; set; }

        public void SetUnique(XivChatType type)
        {
            SetUnique(new []{type});
        }

        // Copy-on-write (#46): the relay queue may be reading ChatTypes while a command changes it, so changes build a
        // new list and swap it in instead of modifying the one being read.
        public void SetUnique(IEnumerable<XivChatType> types)
        {
            var updated = new List<XivChatType>(ChatTypes);
            foreach (var xivChatType in types)
            {
                if (!updated.Contains(xivChatType))
                    updated.Add(xivChatType);
            }

            ChatTypes = updated;
        }

        public void UnsetUnique(XivChatType type)
        {
            UnsetUnique(new []{type});
        }

        public void UnsetUnique(IEnumerable<XivChatType> types)
        {
            var updated = new List<XivChatType>(ChatTypes);
            foreach (var xivChatType in types)
            {
                updated.Remove(xivChatType);
            }

            ChatTypes = updated;
        }
    }

    /// <summary>Who may relay Discord messages into the game.</summary>
    public enum InboundPermission
    {
        /// <summary>Only the configured plugin owner (default).</summary>
        OwnerOnly,

        /// <summary>The owner and members with a specific role.</summary>
        Role,

        /// <summary>Anyone who can post in the channel.</summary>
        Everyone,
    }
}
