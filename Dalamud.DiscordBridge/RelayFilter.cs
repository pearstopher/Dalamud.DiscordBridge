using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Dalamud.DiscordBridge.Model;
using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge
{
    /// <summary>
    /// Decides on the game thread whether any Discord channel wants a chat line, so unwanted lines (battle log, system
    /// spam in combat) are dropped before they're queued and processed (#49). Mirrors <c>SendChatEvent</c>'s channel
    /// choice exactly (outgoing tells are sent as tells; GM lines follow their player channel). The answer per chat type
    /// is cached until the config changes (<see cref="Configuration.Revision"/>). Thread-safe.
    /// </summary>
    internal sealed class RelayFilter
    {
        private ConcurrentDictionary<XivChatType, bool> cache = new();
        private long revision = long.MinValue;

        /// <summary>Pure: would any of these channels receive a relayed line of <paramref name="type"/>?</summary>
        internal static bool Wants(IEnumerable<DiscordChannelConfig> channels, XivChatType type)
        {
            // SendChatEvent sends outgoing tells as incoming tells (both share the "tell" slug).
            var effective = type == XivChatType.TellOutgoing ? XivChatType.TellIncoming : type;
            return channels.Any(channel => ChatTypeGroups.Receives(channel, effective));
        }

        /// <summary>Cached <see cref="Wants"/> for the current config.</summary>
        internal bool ShouldRelay(Configuration config, XivChatType type)
        {
            var current = config.Revision;
            if (current != this.revision)
            {
                this.cache = new ConcurrentDictionary<XivChatType, bool>();
                this.revision = current;
            }

            return this.cache.GetOrAdd(type, t => Wants(config.ChannelConfigs.Values, t));
        }
    }
}
