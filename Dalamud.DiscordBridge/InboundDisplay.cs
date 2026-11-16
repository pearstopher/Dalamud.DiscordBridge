using System;
using System.Threading.Tasks;
using Dalamud.DiscordBridge.Model;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace Dalamud.DiscordBridge
{
    /// <summary>What happened to an inbound Discord message.</summary>
    public enum InboundResult
    {
        /// <summary>Printed in the local chat log.</summary>
        Shown,

        /// <summary>Said in game by the player's character (send-as-me, #16).</summary>
        Sent,

        /// <summary>Queued to be sent shortly (send-as-me rate limit, #16).</summary>
        Queued,

        /// <summary>Not logged in to a character, so nothing was shown.</summary>
        NotLoggedIn,

        /// <summary>Inbound display is turned off in the settings window.</summary>
        Disabled,

        /// <summary>Nothing displayable was left after conversion.</summary>
        Empty,

        /// <summary>Rejected (e.g. too long or not allowed for sending). See the accompanying reason.</summary>
        Rejected,

        /// <summary>An error occurred.</summary>
        Failed,
    }

    /// <summary>The chat entry to print for an inbound message.</summary>
    internal readonly record struct InboundEntry(XivChatType Type, string Name, string Message);

    /// <summary>Pure formatting of inbound chat entries (unit tested).</summary>
    internal static class InboundFormat
    {
        internal const string DefaultSenderFormat = "[D] {name}";

        /// <summary>
        /// Build the entry. Normally it's printed as the target chat type, so it's coloured and filtered like real
        /// FC/LS chat, with the sender label as the name. In echo mode it's printed as Echo with the channel in the text.
        /// </summary>
        internal static InboundEntry Build(InboundDiscordMessage message, string text, string? senderFormat, bool asEcho)
        {
            var name = SenderLabel(message.AuthorName, senderFormat);

            if (!asEcho)
                return new InboundEntry(message.TargetType, name, text);

            return new InboundEntry(XivChatType.Echo, string.Empty, $"[{message.TargetType.GetSlug().ToUpperInvariant()}] {name}: {text}");
        }

        internal static string SenderLabel(string authorName, string? senderFormat)
        {
            var format = string.IsNullOrWhiteSpace(senderFormat) ? DefaultSenderFormat : senderFormat;
            if (!format.Contains("{name}"))
                format += " {name}";

            return format.Replace("{name}", DiscordToGameConverter.ConvertName(authorName)).Trim();
        }
    }

    /// <summary>
    /// Shows converted Discord messages in the player's own chat log (client-side only; nothing is sent to the server).
    /// </summary>
    internal sealed class InboundDisplay
    {
        private static IPluginLog Logger => Service.Logger;

        private readonly Configuration config;
        private readonly InboundEchoGuard echoGuard;

        internal InboundDisplay(Configuration config, InboundEchoGuard echoGuard)
        {
            this.config = config;
            this.echoGuard = echoGuard;
        }

        /// <param name="asEchoOverride">Self-tests: force Echo mode on/off instead of using the setting.</param>
        internal async Task<InboundResult> ShowAsync(InboundDiscordMessage message, string text, bool? asEchoOverride = null)
        {
            if (!this.config.InboundDisplayEnabled)
                return InboundResult.Disabled;

            if (string.IsNullOrEmpty(text))
                return InboundResult.Empty;

            var entry = InboundFormat.Build(message, text, this.config.InboundSenderFormat, asEchoOverride ?? this.config.InboundDisplayAsEcho);

            try
            {
                return await Service.Framework.RunOnFrameworkThread(() =>
                {
                    if (!Service.State.IsLoggedIn)
                        return InboundResult.NotLoggedIn;

                    // Register first: the print can raise ChatMessage synchronously.
                    this.echoGuard.Register(entry.Type, entry.Message);
                    Service.Chat.Print(new XivChatEntry
                    {
                        Type = entry.Type,
                        Name = new SeStringBuilder().AddText(entry.Name).Build(),
                        Message = new SeStringBuilder().AddText(entry.Message).Build(),
                    });
                    return InboundResult.Shown;
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not show Discord message in game.");
                return InboundResult.Failed;
            }
        }
    }
}
