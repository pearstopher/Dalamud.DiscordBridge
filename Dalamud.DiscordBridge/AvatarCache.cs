using System;
using System.Collections.Concurrent;

namespace Dalamud.DiscordBridge
{
    /// <summary>
    /// Lodestone portrait lookups, remembered (#47). Found portraits are kept for the session. Characters that weren't
    /// found are remembered too, for <see cref="NotFoundTtl"/>, so every message from them doesn't repeat a slow search
    /// and stall the relay queue. Failed or timed-out lookups aren't remembered: the next message retries (#55). Thread-safe; pure (the clock is
    /// passed in, unit tested).
    /// </summary>
    internal sealed class AvatarCache
    {
        /// <summary>How long a "no such character on the Lodestone" answer is remembered.</summary>
        internal static readonly TimeSpan NotFoundTtl = TimeSpan.FromHours(1);

        private readonly ConcurrentDictionary<string, string> hits = new();
        private readonly ConcurrentDictionary<string, DateTimeOffset> misses = new();

        /// <summary>
        /// True if the answer is known: <paramref name="url"/> is the portrait, or null for a remembered miss.
        /// False means look it up.
        /// </summary>
        internal bool TryGet(string key, DateTimeOffset now, out string? url)
        {
            if (this.hits.TryGetValue(key, out var hit))
            {
                url = hit;
                return true;
            }

            url = null;
            if (!this.misses.TryGetValue(key, out var until))
                return false;
            if (now < until)
                return true;

            this.misses.TryRemove(key, out _);
            return false;
        }

        internal void AddHit(string key, string url)
        {
            this.hits[key] = url;
            this.misses.TryRemove(key, out _);
        }

        internal void AddMiss(string key, DateTimeOffset now, TimeSpan ttl) => this.misses[key] = now + ttl;
    }
}
