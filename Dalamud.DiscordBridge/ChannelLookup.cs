using System.Collections.Generic;

namespace Dalamud.DiscordBridge
{
    /// <summary>What happened when a configured Discord channel was looked up.</summary>
    internal enum ChannelLookupResult
    {
        /// <summary>The channel exists and the bot can see it.</summary>
        Found,

        /// <summary>Discord answered definitively that the channel doesn't exist (deleted).</summary>
        NotFound,

        /// <summary>Anything else: network error, timeout, missing access, outage. Never a reason to forget the channel.</summary>
        Error,
    }

    /// <summary>
    /// Decides what to do when a configured channel can't be reached (#39). A Discord hiccup must never wipe a
    /// channel's setup: only a definitive "Unknown Channel" removes it. Pure (unit tested).
    /// </summary>
    internal static class ChannelLookup
    {
        /// <summary>Discord's JSON error code for "Unknown Channel".</summary>
        internal const int UnknownChannelCode = 10003;

        /// <param name="found">The lookup returned a channel.</param>
        /// <param name="threw">The lookup threw (network, permissions, rate limit…).</param>
        /// <param name="discordErrorCode">Discord's error code from the exception, if any.</param>
        internal static ChannelLookupResult Classify(bool found, bool threw, int? discordErrorCode)
        {
            if (found)
                return ChannelLookupResult.Found;
            if (!threw)
                return ChannelLookupResult.NotFound; // Discord.Net returns null only for a 404.
            return discordErrorCode == UnknownChannelCode ? ChannelLookupResult.NotFound : ChannelLookupResult.Error;
        }

        /// <summary>Only a channel Discord says is gone may be removed from the config.</summary>
        internal static bool ShouldForget(ChannelLookupResult result) => result == ChannelLookupResult.NotFound;
    }

    /// <summary>Remembers which channels were already warned about, so a broken channel logs once per session.</summary>
    internal sealed class WarnOnce
    {
        private readonly HashSet<ulong> warned = [];
        private readonly object gate = new();

        /// <summary>True the first time for each ID.</summary>
        internal bool ShouldWarn(ulong id)
        {
            lock (this.gate)
                return this.warned.Add(id);
        }

        /// <summary>The channel works again: warn again if it breaks later.</summary>
        internal void Reset(ulong id)
        {
            lock (this.gate)
                this.warned.Remove(id);
        }
    }
}
