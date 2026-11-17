using System;
using System.Collections.Generic;
using System.Net;

namespace Dalamud.DiscordBridge
{
    /// <summary>
    /// Your Free Company's Lodestone member list (#55): name＠world → portrait. FC members get their portrait from here,
    /// with no per-player search. That matters because the Lodestone's character search never returns some names
    /// (hyphenated ones such as "Astra-lune Elek"), and the game has no Lodestone ID for other players.
    /// Loaded in the background by <c>DiscordHandler</c>; this class only holds and answers. Thread-safe; pure (the clock
    /// is passed in, unit tested).
    /// </summary>
    internal sealed class FcRoster
    {
        /// <summary>How long a loaded roster is used before it's fetched again (new members, changed portraits).</summary>
        internal static readonly TimeSpan RefreshAfter = TimeSpan.FromHours(1);

        /// <summary>Least time between load attempts, so a failing Lodestone isn't hammered.</summary>
        internal static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);

        private readonly object gate = new();
        private Dictionary<string, string> portraits = new(StringComparer.OrdinalIgnoreCase);
        private string? owner;
        private DateTimeOffset loadedAt = DateTimeOffset.MinValue;
        private DateTimeOffset attemptedAt = DateTimeOffset.MinValue;

        /// <summary>Members currently known.</summary>
        internal int Count
        {
            get { lock (this.gate) return this.portraits.Count; }
        }

        /// <summary>
        /// Name and world as one lookup key. Lodestone names may arrive HTML-encoded ("Reaper&amp;#39; Grimm") and with a
        /// typographic apostrophe; both are normalised. Case doesn't matter.
        /// </summary>
        internal static string Key(string name, string world)
            => $"{WebUtility.HtmlDecode(name).Replace('’', '\'').Trim()}＠{world.Trim()}";

        /// <summary>
        /// Whether a load should start now for <paramref name="who"/> (your "Name＠World"): never loaded, a different
        /// character, or older than <see cref="RefreshAfter"/>; and no attempt in the last <see cref="RetryAfter"/>.
        /// True also records the attempt, so concurrent callers start only one load.
        /// </summary>
        internal bool TryBeginLoad(string who, DateTimeOffset now)
        {
            lock (this.gate)
            {
                var stale = this.owner != who || now - this.loadedAt >= RefreshAfter;
                if (!stale || (this.owner == who && now - this.attemptedAt < RetryAfter))
                    return false;

                if (this.owner != who)
                {
                    // Switched characters: the old FC's members aren't yours any more.
                    this.owner = who;
                    this.portraits = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    this.loadedAt = DateTimeOffset.MinValue;
                }

                this.attemptedAt = now;
                return true;
            }
        }

        /// <summary>Replace the roster with a fresh load for <paramref name="who"/>. Members without a portrait are skipped.</summary>
        internal void Replace(string who, IEnumerable<(string Name, string World, string? Avatar)> members, DateTimeOffset now)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, world, avatar) in members)
            {
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(world) && !string.IsNullOrWhiteSpace(avatar))
                    map[Key(name, world)] = avatar;
            }

            lock (this.gate)
            {
                if (this.owner != who)
                    return; // a character switch happened mid-load
                this.portraits = map;
                this.loadedAt = now;
            }
        }

        /// <summary>A member's portrait, if they're on the loaded roster.</summary>
        internal bool TryGet(string name, string world, out string? url)
        {
            lock (this.gate)
            {
                var found = this.portraits.TryGetValue(Key(name, world), out var hit);
                url = hit;
                return found;
            }
        }
    }
}
