namespace Dalamud.DiscordBridge
{
    public enum DiscordState
    {
        None,
        Ready,
        TokenInvalid,

        /// <summary>Was connected, lost the gateway; Discord.Net is reconnecting (#48).</summary>
        Disconnected,
    }

    /// <summary>Gateway events that change <see cref="DiscordState"/>.</summary>
    public enum DiscordStateEvent
    {
        Ready,
        Connected,
        Disconnected,
        LoginRejected,
    }

    /// <summary>
    /// The connection state machine shown in /dcp (#48). Before, the state never left Ready after a disconnect, so
    /// /dcp said "Connected!" while offline. Pure (unit tested).
    /// </summary>
    internal static class DiscordStates
    {
        internal static DiscordState Next(DiscordState current, DiscordStateEvent e) => e switch
        {
            DiscordStateEvent.Ready => DiscordState.Ready,
            DiscordStateEvent.LoginRejected => DiscordState.TokenInvalid,
            // A resumed session fires Connected but not Ready.
            DiscordStateEvent.Connected => current == DiscordState.Disconnected ? DiscordState.Ready : current,
            // Only a working connection can be lost; "not started" and "bad token" stay what they are.
            DiscordStateEvent.Disconnected => current == DiscordState.Ready ? DiscordState.Disconnected : current,
            _ => current,
        };
    }
}
