using System;
using System.Threading.Tasks;
using Dalamud.DiscordBridge.Model;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace Dalamud.DiscordBridge
{
    /// <summary>The outcome of handling an inbound message, with a short reason for rejections.</summary>
    public readonly record struct InboundOutcome(InboundResult Result, string? Reason = null);

    /// <summary>
    /// "Send as me": the owner's Discord messages are typed into the game's chat box as their character, so other
    /// players see them. Opt-in and off by default. This is outside Dalamud's plugin rules (see docs/DALAMUD_RULES.md).
    /// </summary>
    internal sealed class InboundSender
    {
        private static IPluginLog Logger => Service.Logger;

        /// <summary>
        /// Entities the client allows in typed chat. Same mask as ChatTwo/ChatAnywhere. Text that changes under
        /// sanitization is rejected rather than silently altered.
        /// </summary>
        private const AllowedEntities ChatEntities = (AllowedEntities)0x27F;

        private readonly Configuration config;
        private readonly InboundEchoGuard echoGuard;
        private readonly SendRateLimiter limiter;

        internal InboundSender(Configuration config, InboundEchoGuard echoGuard)
        {
            this.config = config;
            this.echoGuard = echoGuard;
            this.limiter = new SendRateLimiter(TimeSpan.FromMilliseconds(config.SendIntervalMs), config.SendQueueLimit);
        }

        /// <summary>
        /// Send <paramref name="text"/> (already converted, not truncated) into the message's target channel.
        /// </summary>
        internal async Task<InboundOutcome> SendAsync(InboundDiscordMessage message, string text)
        {
            if (!GameChatCommand.TryBuild(message.TargetType, text, message.TellTarget, out var line, out var reason))
                return new InboundOutcome(InboundResult.Rejected, reason);

            this.limiter.Interval = TimeSpan.FromMilliseconds(Math.Max(500, this.config.SendIntervalMs));
            this.limiter.MaxQueued = Math.Max(0, this.config.SendQueueLimit);
            if (this.limiter.TryReserve() is not { } wait)
                return new InboundOutcome(InboundResult.Rejected, "too many messages at once, slow down");

            if (wait > TimeSpan.Zero)
                await Task.Delay(wait);

            try
            {
                return await Service.Framework.RunOnFrameworkThread(() =>
                {
                    if (!Service.State.IsLoggedIn)
                        return new InboundOutcome(InboundResult.NotLoggedIn, "not logged in");

                    if (Sanitize(text) != text)
                        return new InboundOutcome(InboundResult.Rejected, "contains characters the game won't accept");

                    // The game echoes our own line back as chat from us; don't relay that back to Discord.
                    var isTell = message.TargetType is XivChatType.TellOutgoing or XivChatType.TellIncoming;
                    this.echoGuard.Register(isTell ? XivChatType.TellOutgoing : message.TargetType, text, EchoKind.Sent);

                    // A tell to yourself also arrives as an incoming tell.
                    if (isTell && IsSelf(message.TellTarget))
                        this.echoGuard.Register(XivChatType.TellIncoming, text, EchoKind.Sent);

                    SendChatBoxLine(line);
                    return new InboundOutcome(InboundResult.Sent);
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not send Discord message as your character.");
                return new InboundOutcome(InboundResult.Failed, "error while sending");
            }
        }

        /// <summary>Whether a tell target is the logged-in character (framework thread).</summary>
        private static bool IsSelf(string? tellTarget)
        {
            var name = Service.PlayerState.CharacterName;
            if (string.IsNullOrEmpty(name) || GameChatCommand.NormalizeTellTarget(tellTarget) is not { } target)
                return false;

            var world = Service.PlayerState.HomeWorld.Value.Name.ExtractText();
            return target == name || target == $"{name}@{world}";
        }

        /// <summary>The client's own chat sanitization (framework thread).</summary>
        internal static unsafe string Sanitize(string text)
        {
            var str = Utf8String.FromString(text);
            try
            {
                str->SanitizeString(ChatEntities, null);
                return str->ToString();
            }
            finally
            {
                str->Dtor(true);
            }
        }

        /// <summary>Equivalent to typing <paramref name="line"/> into the chat box and pressing Enter (framework thread).</summary>
        private static unsafe void SendChatBoxLine(string line)
        {
            var str = Utf8String.FromString(line);
            try
            {
                UIModule.Instance()->ProcessChatBoxEntry(str);
            }
            finally
            {
                str->Dtor(true);
            }
        }
    }
}
