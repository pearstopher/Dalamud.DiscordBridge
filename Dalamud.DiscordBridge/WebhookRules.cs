using System.Net;

namespace Dalamud.DiscordBridge
{
    /// <summary>Pure webhook decisions (#40), unit tested.</summary>
    internal static class WebhookRules
    {
        /// <summary>Discord's JSON error code for "Unknown Webhook".</summary>
        internal const int UnknownWebhookCode = 10015;

        /// <summary>The webhook was deleted (someone removed it in channel settings): make a new one.</summary>
        internal static bool IsGone(HttpStatusCode httpCode, int? discordCode)
            => discordCode == UnknownWebhookCode || httpCode == HttpStatusCode.NotFound;

        /// <summary>Only write the config file when the channel's webhook actually changed.</summary>
        internal static bool ShouldSaveId(ulong storedId, ulong currentId) => storedId != currentId;
    }
}
