using System.Threading;

namespace Dalamud.DiscordBridge
{
    /// <summary>
    /// Lets only one Discord reconnect (/dcp Save) run at a time (#44). The swap runs on a background task so the game
    /// never freezes; a second click while it runs is ignored. Thread-safe, pure (unit tested).
    /// </summary>
    internal sealed class SwapGate
    {
        private int busy;

        /// <summary>True while a reconnect is running.</summary>
        internal bool IsBusy => Volatile.Read(ref this.busy) == 1;

        /// <summary>Claim the gate. False if a reconnect is already running.</summary>
        internal bool TryBegin() => Interlocked.CompareExchange(ref this.busy, 1, 0) == 0;

        /// <summary>Release the gate (always, in a finally).</summary>
        internal void End() => Volatile.Write(ref this.busy, 0);
    }
}
