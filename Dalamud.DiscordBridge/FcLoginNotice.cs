using System.Text.RegularExpressions;
using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge
{
    /// <summary>
    /// Free Company member login/logout lines (#53), chat type 70 (slug <c>fclogin</c>): who it is and how to show it.
    /// The game's own line is kept, with 🟢 (logged in) or ⚫ (logged out) in front. Pure (unit tested).
    /// </summary>
    internal static class FcLoginNotice
    {
        /// <summary>The game's chat type for FC member login/logout lines.</summary>
        internal const XivChatType ChatType = (XivChatType)70;

        /// <summary>Shown as the sender when the member's name can't be found.</summary>
        internal const string UnknownSender = "Free Company";

        internal enum Change
        {
            LoggedIn,
            LoggedOut,
            Unknown,
        }

        // "First Last" at the start of the line (English, German, French: "Bob Smith has logged in.").
        private static readonly Regex LeadingName = new(@"^\W*(?<name>\p{Lu}[\p{L}'\-]+ \p{Lu}[\p{L}'\-]+)(?=\s|が|$)", RegexOptions.Compiled);

        // Japanese: "Bob Smithがログインしました。"
        private static readonly Regex JapaneseName = new(@"^\W*(?<name>[\p{L}'\-]+ [\p{L}'\-]+)が", RegexOptions.Compiled);

        internal static bool Is(XivChatType type) => ((int)type & 0x7F) == (int)ChatType;

        /// <summary>Logged in or out, from the game's text (English, Japanese, German, French).</summary>
        internal static Change Classify(string text)
        {
            var t = text.ToLowerInvariant();
            // "logged out" / "déconnecté" must be checked before "logged in" / "connecté".
            if (t.Contains("logged out") || t.Contains("ログアウト") || t.Contains("ausgeloggt") || t.Contains("abgemeldet") || t.Contains("déconnecté"))
                return Change.LoggedOut;
            if (t.Contains("logged in") || t.Contains("ログイン") || t.Contains("eingeloggt") || t.Contains("angemeldet") || t.Contains("connecté"))
                return Change.LoggedIn;
            return Change.Unknown;
        }

        internal static string Emoji(Change change) => change switch
        {
            Change.LoggedIn => "🟢",
            Change.LoggedOut => "⚫",
            _ => "🔔",
        };

        /// <summary>The member's "First Last" from the line's text, or null.</summary>
        internal static string? NameFromText(string text)
        {
            var match = LeadingName.Match(text);
            if (!match.Success)
                match = JapaneseName.Match(text);
            return match.Success ? match.Groups["name"].Value : null;
        }

        /// <summary>
        /// Sender name, world and text to post. The player link's name wins; otherwise the name comes from the text. FC
        /// members always share the FC's home world, so <paramref name="homeWorld"/> (yours) is used whenever it's known:
        /// the link's world in real login lines is bogus (shown as "Dev", #55) and is only a last resort.
        /// </summary>
        internal static (string Name, string World, string Text) Resolve(string? linkName, string? linkWorld, string text, string? homeWorld)
        {
            var body = $"{Emoji(Classify(text))} {text.Trim()}";
            var name = !string.IsNullOrWhiteSpace(linkName) ? linkName : NameFromText(text);
            if (string.IsNullOrWhiteSpace(name))
                return (UnknownSender, string.Empty, body);

            var world = !string.IsNullOrWhiteSpace(homeWorld) ? homeWorld : linkWorld ?? string.Empty;
            return (name, world, body);
        }
    }
}
