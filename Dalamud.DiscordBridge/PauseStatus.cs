using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge
{
    /// <summary>What the bridge is doing, as shown in the server info bar (#54).</summary>
    internal enum BridgeState
    {
        /// <summary>Connected and relaying both ways.</summary>
        Running,

        /// <summary>Connected, but relaying nothing until resumed (<c>Configuration.Paused</c>).</summary>
        Paused,

        /// <summary>The Discord bot isn't connected (no token, bad token, connection lost).</summary>
        Off,
    }

    /// <summary>
    /// Pause/resume (#54): the server info bar entry's icon, tooltip and click action, and the Discord-side texts.
    /// Pure (unit tested).
    /// </summary>
    internal static class PauseStatus
    {
        /// <summary>Reaction on Discord messages (and xl!ping) while paused.</summary>
        internal const string PausedReaction = "💤";

        internal static BridgeState StateOf(bool connected, bool paused) =>
            !connected ? BridgeState.Off : paused ? BridgeState.Paused : BridgeState.Running;

        /// <summary>
        /// The bar icon. The game font has no ▶/⏸ symbols, so: the game's own arrow icon for running, "||" for paused,
        /// the game's cross icon for off.
        /// </summary>
        internal static string Glyph(BridgeState state) => state switch
        {
            BridgeState.Running => SeIconChar.ArrowRight.ToIconString(),
            BridgeState.Paused => "||",
            _ => SeIconChar.Cross.ToIconString(),
        };

        internal static string Tooltip(BridgeState state) => state switch
        {
            BridgeState.Running => $"{Constant.PluginName}: running. Click to pause relaying (both ways).",
            BridgeState.Paused => $"{Constant.PluginName}: paused, nothing is relayed. Click to resume.",
            _ => $"{Constant.PluginName}: not connected to Discord. Click to open settings.",
        };

        /// <summary>Clicking the bar entry: pause/resume while connected, open settings when off.</summary>
        internal static bool ClickOpensSettings(BridgeState state) => state == BridgeState.Off;

        /// <summary>xl!ping's reaction: 💤 while paused, else ✅ / ⏸️ by login state.</summary>
        internal static string PingReaction(bool paused, bool loggedIn) =>
            paused ? PausedReaction : InboundRules.PingReaction(loggedIn);

        /// <summary>xl!ping's text fallback (no Add Reactions permission).</summary>
        internal static string PingText(bool paused, bool loggedIn, string prefix) =>
            paused ? $"Pong: running but paused. `{prefix}resume` to continue." : InboundRules.PingText(loggedIn);

        internal static string ChangeTitle(bool paused) => paused ? "⏸️ Paused" : "▶️ Resumed";

        /// <param name="source">Where it was changed: "Discord", "the server info bar", "/dcp".</param>
        internal static string ChangeText(bool paused, string source, string prefix) => paused
            ? $"Relaying is paused (from {source}). Nothing goes between the game and Discord until `{prefix}resume` or a click on the server info bar icon. Settings are unchanged."
            : $"Relaying resumed (from {source}). Everything works as before.";
    }
}
