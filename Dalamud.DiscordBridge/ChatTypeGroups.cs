using System.Collections.Generic;
using System.Linq;
using Dalamud.DiscordBridge.Model;
using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge
{
    /// <summary>Pure groupings of chat types (unit tested).</summary>
    internal static class ChatTypeGroups
    {
        /// <summary>GM channel → the player channel it appears in (GM tell → tells, GM FC → FC, GM LS1 → LS1, …).</summary>
        private static readonly Dictionary<int, XivChatType> GmToPlayer = new()
        {
            [80] = XivChatType.TellIncoming,
            [81] = XivChatType.Say,
            [82] = XivChatType.Shout,
            [83] = XivChatType.Yell,
            [84] = XivChatType.Party,
            [85] = XivChatType.FreeCompany,
            [86] = XivChatType.Ls1, [87] = XivChatType.Ls2, [88] = XivChatType.Ls3, [89] = XivChatType.Ls4,
            [90] = XivChatType.Ls5, [91] = XivChatType.Ls6, [92] = XivChatType.Ls7, [93] = XivChatType.Ls8,
            [94] = XivChatType.NoviceNetwork,
        };

        /// <summary>The player channel a GM channel belongs to, or null if <paramref name="type"/> isn't a GM channel.</summary>
        internal static XivChatType? PlayerEquivalentOfGm(XivChatType type)
            => GmToPlayer.TryGetValue((int)type & 0x7F, out var player) ? player : null;

        /// <summary>
        /// Whether a Discord channel should receive a relayed line of <paramref name="type"/>: it's subscribed to that
        /// type, or it's a GM message in a player channel the Discord channel is subscribed to (a GM tell when tells
        /// are on, GM FC when FC is on, …). GM lines keep their GM label, so replies to them stay blocked.
        /// </summary>
        internal static bool Receives(DiscordChannelConfig channel, XivChatType type)
        {
            if (channel.ChatTypes.Contains(type))
                return true;

            if (PlayerEquivalentOfGm(type) is not { } player)
                return false;

            return channel.ChatTypes.Contains(player) ||
                   (player == XivChatType.TellIncoming && channel.ChatTypes.Contains(XivChatType.TellOutgoing)) ||
                   (player == XivChatType.Party && channel.ChatTypes.Contains(XivChatType.CrossParty));
        }

        /// <summary>
        /// Player chat: everything players say or emote, in every channel you can be in. Used by <c>setup</c>. Excludes
        /// Echo, system/battle/loot/retainer/NPC notifications. GM messages in these channels are included via
        /// <see cref="Receives"/>.
        /// </summary>
        internal static readonly IReadOnlyList<XivChatType> PlayerChat =
        [
            XivChatType.Say, XivChatType.Shout, XivChatType.Yell,
            XivChatType.TellIncoming, XivChatType.TellOutgoing,
            XivChatType.Party, XivChatType.CrossParty, XivChatType.Alliance, XivChatType.PvPTeam,
            XivChatType.FreeCompany,
            XivChatType.Ls1, XivChatType.Ls2, XivChatType.Ls3, XivChatType.Ls4,
            XivChatType.Ls5, XivChatType.Ls6, XivChatType.Ls7, XivChatType.Ls8,
            XivChatType.CrossLinkShell1, XivChatType.CrossLinkShell2, XivChatType.CrossLinkShell3, XivChatType.CrossLinkShell4,
            XivChatType.CrossLinkShell5, XivChatType.CrossLinkShell6, XivChatType.CrossLinkShell7, XivChatType.CrossLinkShell8,
            XivChatType.NoviceNetwork,
            XivChatType.CustomEmote, XivChatType.StandardEmote,
        ];
    }
}
