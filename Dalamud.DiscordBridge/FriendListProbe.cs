using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;
using OnlineStatus = FFXIVClientStructs.FFXIV.Client.UI.Info.InfoProxyCommonList.CharacterData.OnlineStatus;

namespace Dalamud.DiscordBridge
{
    /// <summary>
    /// Research for #36 (friends list): a read-only look at what the client already holds in its friend list.
    /// Sends nothing to the server and never reads ContentId or AccountId (Dalamud rule R3).
    /// </summary>
    internal static class FriendListProbe
    {
        /// <summary>One friend as the client currently knows it.</summary>
        internal readonly record struct Friend(string Name, string HomeWorld, string CurrentWorld, ulong State, bool OtherServer, bool Pending);

        /// <summary>Snapshot of the friend list (framework thread only).</summary>
        internal static unsafe List<Friend> Read()
        {
            var result = new List<Friend>();
            var proxy = InfoProxyFriendList.Instance();
            if (proxy == null)
                return result;

            var worlds = Service.Data.GetExcelSheet<World>();
            foreach (var entry in proxy->CharDataSpan)
            {
                result.Add(new Friend(
                    entry.NameString,
                    worlds.GetRowOrDefault(entry.HomeWorld)?.Name.ExtractText() ?? $"#{entry.HomeWorld}",
                    worlds.GetRowOrDefault(entry.CurrentWorld)?.Name.ExtractText() ?? $"#{entry.CurrentWorld}",
                    (ulong)entry.State,
                    entry.IsOtherServer,
                    entry.WaitingForFriendListApproval));
            }

            return result;
        }

        /// <summary>Best-effort status from the raw flags: what the client shows, not a guarantee.</summary>
        internal static string StatusOf(ulong state, bool otherServer)
        {
            var flags = (OnlineStatus)state;
            if (flags.HasFlag(OnlineStatus.Online))
                return "online";
            if (otherServer)
                return "unknown (other server)";
            return "offline";
        }

        /// <summary>One line per friend for the local chat log / Discord (pure, unit tested).</summary>
        internal static string Describe(int number, Friend friend)
        {
            var where = friend.CurrentWorld != friend.HomeWorld && !string.IsNullOrEmpty(friend.CurrentWorld) && friend.CurrentWorld != "#0"
                ? $" (on {friend.CurrentWorld})"
                : string.Empty;
            var pending = friend.Pending ? " [request pending]" : string.Empty;
            return $"{number}. {friend.Name}@{friend.HomeWorld}{where}: {StatusOf(friend.State, friend.OtherServer)}{pending} (flags 0x{friend.State:X})";
        }
    }
}
