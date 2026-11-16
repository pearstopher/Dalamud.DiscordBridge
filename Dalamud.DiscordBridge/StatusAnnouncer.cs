using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Dalamud.DiscordBridge.Model;

namespace Dalamud.DiscordBridge
{
    /// <summary>
    /// "Plugin online / offline" posts (#34): which channels get them, their text, and a once-per-load gate.
    /// Pure (unit tested); the Discord calls live in <c>DiscordHandler.AnnounceStatusAsync</c>.
    /// </summary>
    internal sealed class StatusAnnouncer
    {
        internal const string OnlineTitle = "🟢 Online";
        internal const string OfflineTitle = "🔴 Offline";

        // 0 = nothing posted, 1 = online posted, 2 = offline posted. Lives on the plugin, so recreating the
        // DiscordHandler (/dcp Save) or a gateway reconnect never posts again.
        private int state;

        /// <summary>True once the online post was sent (or attempted) in this plugin load.</summary>
        internal bool OnlineAnnounced => Volatile.Read(ref this.state) >= 1;

        /// <summary>True only the first time it's called in this load (the first Discord Ready).</summary>
        internal bool TryBeginOnline() => Interlocked.CompareExchange(ref this.state, 1, 0) == 0;

        /// <summary>True only once, and only if online was announced in this load.</summary>
        internal bool TryBeginOffline() => Interlocked.CompareExchange(ref this.state, 2, 1) == 1;

        /// <summary>Channels the bridge actually uses: any subscribed chat kind, duty pops, or Discord → game relay.</summary>
        internal static IReadOnlyList<ulong> TargetChannels(IReadOnlyDictionary<ulong, DiscordChannelConfig> channels) =>
            channels
                .Where(kv => kv.Value != null &&
                             (kv.Value.ChatTypes.Count > 0 || kv.Value.IsContentFinder || kv.Value.InboundChatType != null))
                .Select(kv => kv.Key)
                .OrderBy(id => id)
                .ToList();

        /// <param name="version">Plugin version, e.g. "2.1.0.0".</param>
        /// <param name="character">"Name＠World" if a character is logged in, otherwise null.</param>
        /// <param name="pausedPrefix">Command prefix if the bridge starts paused (#54); null when not paused.</param>
        internal static string OnlineText(string version, string? character, string? pausedPrefix = null) =>
            $"{Constant.PluginName} v{version} is running"
            + (string.IsNullOrWhiteSpace(character) ? " (not logged in to a character yet)." : $" (logged in as **{character}**).")
            + (pausedPrefix == null ? string.Empty : $"\n⏸️ **Paused:** nothing is relayed until `{pausedPrefix}resume`.");

        internal static string OfflineText(string version) =>
            $"{Constant.PluginName} v{version} stopped (game closed or plugin unloaded). "
            + "Messages posted here won't reach the game, and get no reaction, until it's running again.";
    }
}
