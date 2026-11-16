using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.DiscordBridge.Model;
using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge
{
    /// <summary>
    /// Pure decision logic for Discord → game relay: which chat types can be targets, who may relay, and which
    /// Discord messages qualify. No Discord.Net or Dalamud runtime dependencies, so it's unit tested.
    /// </summary>
    internal static class InboundRules
    {
        /// <summary>Chat types a Discord channel can be relayed into.</summary>
        internal static readonly IReadOnlyList<XivChatType> AllowedTargets =
        [
            XivChatType.Say, XivChatType.Shout, XivChatType.Yell,
            XivChatType.Party, XivChatType.Alliance, XivChatType.FreeCompany,
            XivChatType.Ls1, XivChatType.Ls2, XivChatType.Ls3, XivChatType.Ls4,
            XivChatType.Ls5, XivChatType.Ls6, XivChatType.Ls7, XivChatType.Ls8,
            XivChatType.CrossLinkShell1, XivChatType.CrossLinkShell2, XivChatType.CrossLinkShell3, XivChatType.CrossLinkShell4,
            XivChatType.CrossLinkShell5, XivChatType.CrossLinkShell6, XivChatType.CrossLinkShell7, XivChatType.CrossLinkShell8,
            XivChatType.NoviceNetwork, XivChatType.Echo, XivChatType.CustomEmote,
        ];

        /// <summary>Slugs users can type for <see cref="AllowedTargets"/>, e.g. "fc, ls1, cwls1, p, e".</summary>
        internal static string AllowedSlugList => string.Join(", ", AllowedTargets.Select(t => t.GetSlug()));

        private static readonly Regex RoleMention = new(@"^<@&(\d+)>$");

        /// <summary>
        /// Parse the argument of <c>setinbound</c>. "off"/"none" turns inbound off (target = null).
        /// </summary>
        internal static bool TryParseTarget(string? arg, out XivChatType? target, out string error)
        {
            target = null;
            error = string.Empty;
            var slug = arg?.Trim().ToLowerInvariant();

            if (string.IsNullOrEmpty(slug))
            {
                error = $"Specify a chat kind ({AllowedSlugList}) or `off`.";
                return false;
            }

            if (slug is "off" or "none")
                return true;

            foreach (var type in AllowedTargets)
            {
                if (type.GetSlug() == slug)
                {
                    target = type;
                    return true;
                }
            }

            error = $"`{slug}` can't receive Discord messages. Use one of: {AllowedSlugList}, or `off`.";
            return false;
        }

        /// <summary>
        /// Parse the arguments of <c>setinboundperm</c>: <c>owner</c>, <c>everyone</c>, or <c>role &lt;@role|id&gt;</c>.
        /// </summary>
        internal static bool TryParsePermission(IReadOnlyList<string> args, out InboundPermission permission, out ulong? roleId, out string error)
        {
            permission = InboundPermission.OwnerOnly;
            roleId = null;
            error = string.Empty;

            var mode = args.Count > 0 ? args[0].Trim().ToLowerInvariant() : string.Empty;
            switch (mode)
            {
                case "owner":
                    return true;
                case "everyone":
                    permission = InboundPermission.Everyone;
                    return true;
                case "role":
                    var raw = args.Count > 1 ? args[1].Trim() : string.Empty;
                    var match = RoleMention.Match(raw);
                    var idText = match.Success ? match.Groups[1].Value : raw;
                    if (ulong.TryParse(idText, out var id))
                    {
                        permission = InboundPermission.Role;
                        roleId = id;
                        return true;
                    }

                    error = "Mention the role (or give its ID): `role @Members`.";
                    return false;
                default:
                    error = "Use `owner`, `everyone`, or `role @SomeRole`.";
                    return false;
            }
        }

        /// <summary>
        /// Whether a Discord user is the configured owner. <paramref name="configuredOwner"/> may be
        /// <c>username#1234</c>, a bare username (new-style accounts), or a numeric user ID.
        /// </summary>
        internal static bool IsOwner(string username, string discriminator, ulong userId, string configuredOwner)
        {
            if (string.IsNullOrWhiteSpace(configuredOwner))
                return false;

            if ($"{username}#{discriminator}" == configuredOwner)
                return true;

            // New-style usernames have no discriminator (Discord.Net reports "0000").
            if (username == configuredOwner && discriminator == "0000")
                return true;

            return ulong.TryParse(configuredOwner, out var id) && id == userId;
        }

        /// <summary>
        /// Whether an author may relay into the game under <paramref name="permission"/>. The owner always may.
        /// </summary>
        internal static bool IsAllowedSender(InboundPermission permission, bool isOwner, IEnumerable<ulong> authorRoleIds, ulong? requiredRoleId)
        {
            if (isOwner)
                return true;

            return permission switch
            {
                InboundPermission.Everyone => true,
                InboundPermission.Role => requiredRoleId.HasValue && authorRoleIds.Contains(requiredRoleId.Value),
                _ => false,
            };
        }

        /// <summary>
        /// Whether a Discord message should be relayed into the game at all (before permissions):
        /// not from a bot or webhook (our own relays are webhooks), not a bot command, and the channel has a target.
        /// </summary>
        internal static bool ShouldRelay(bool authorIsBot, bool authorIsWebhook, string content, string commandPrefix, XivChatType? inboundTarget)
        {
            if (authorIsBot || authorIsWebhook || inboundTarget == null)
                return false;

            return string.IsNullOrEmpty(commandPrefix) || !content.TrimStart().StartsWith(commandPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// "Send as me" applies only to the owner, only with the global switch on, and only in channels that opted in.
        /// Everyone else (and the owner elsewhere) gets local display.
        /// </summary>
        internal static bool ShouldSendAsOwner(bool isOwner, bool globalAllowed, bool channelAllowed)
            => isOwner && globalAllowed && channelAllowed;

        /// <summary>Reaction added to the Discord message for each outcome, or null for none.</summary>
        internal static string? ReactionFor(InboundResult result) => result switch
        {
            InboundResult.Shown or InboundResult.Sent => "✅",
            InboundResult.Queued => "⏳",
            InboundResult.NotLoggedIn => "⏸️",
            InboundResult.Rejected => "❌",
            InboundResult.Failed => "⚠️",
            _ => null,
        };

        /// <summary>One-line description of a channel's inbound setting, for <c>listchannel</c>/<c>setinbound</c> replies.</summary>
        internal static string Describe(DiscordChannelConfig config)
        {
            if (config.InboundChatType is not { } target)
                return "Inbound (Discord → game): off";

            var who = config.InboundPermission switch
            {
                InboundPermission.Everyone => "everyone",
                InboundPermission.Role => config.InboundRoleId is { } r ? $"owner + <@&{r}>" : "owner (role not set)",
                _ => "owner only",
            };
            var send = config.InboundSendAsOwner ? "; your messages are sent as your character" : string.Empty;
            return $"Inbound (Discord → game): {target.GetFancyName()} ({who}{send})";
        }
    }
}
