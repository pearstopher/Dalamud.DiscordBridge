using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dalamud.DiscordBridge
{
    /// <summary>
    /// Who a relay may ping (#41). Game text never pings anyone. The owner's own configured prefix (<c>setprefix</c>,
    /// <c>setcfprefix</c>) may: <c>xl!setcfprefix @here</c> or a <c>&lt;@me&gt;</c> prefix on tells keeps working.
    /// Only mentions written in the prefix are allowed, so a player can't ping anyone the owner didn't choose. Pure.
    /// </summary>
    internal static class MentionPolicy
    {
        internal readonly record struct Allowed(bool Everyone, IReadOnlyList<ulong> Users, IReadOnlyList<ulong> Roles)
        {
            internal bool None => !this.Everyone && this.Users.Count == 0 && this.Roles.Count == 0;
        }

        private static readonly Regex UserMention = new(@"<@!?(\d{1,20})>", RegexOptions.Compiled);
        private static readonly Regex RoleMention = new(@"<@&(\d{1,20})>", RegexOptions.Compiled);

        /// <summary>The mentions the owner wrote in <paramref name="ownerPrefix"/>; nothing for game text.</summary>
        internal static Allowed ForOwnerPrefix(string? ownerPrefix)
        {
            if (string.IsNullOrEmpty(ownerPrefix))
                return new Allowed(false, [], []);

            static List<ulong> Ids(Regex regex, string text) =>
                regex.Matches(text).Select(m => ulong.TryParse(m.Groups[1].Value, out var id) ? id : 0).Where(id => id != 0).Distinct().ToList();

            return new Allowed(
                ownerPrefix.Contains("@everyone") || ownerPrefix.Contains("@here"),
                Ids(UserMention, ownerPrefix),
                Ids(RoleMention, ownerPrefix));
        }
    }
}
