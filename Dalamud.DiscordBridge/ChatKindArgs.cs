using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge
{
    /// <summary>
    /// Parses the chat-kind list that commands like <c>setchannel</c>, <c>setprefix</c> and <c>setavatar</c> take
    /// (#42), and formats their replies. Pure (unit tested).
    /// </summary>
    internal static class ChatKindArgs
    {
        /// <summary>Longest list we put in an embed description (Discord's limit is 4096).</summary>
        internal const int MaxListLength = 3500;

        /// <summary>
        /// Parse <c>"fc,ls1,tell"</c>. Case-insensitive; <c>any</c> = all regular chat, <c>all</c> = every kind, and a slug
        /// shared by several types (<c>tell</c> = incoming + outgoing, <c>p</c> = party + cross-party) selects all of them.
        /// </summary>
        /// <param name="any">The types for <c>any</c> (<see cref="DiscordHandler.DefaultChatTypes"/>).</param>
        /// <param name="all">The types for <c>all</c> (<see cref="DiscordHandler.AllChatTypes"/>).</param>
        internal static bool TryParse(string? text, IEnumerable<XivChatType> any, IEnumerable<XivChatType> all,
                                      out IReadOnlyList<XivChatType> types, out string error)
        {
            types = [];
            error = string.Empty;

            var words = (text ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(w => w.ToLowerInvariant())
                .ToList();
            if (words.Count == 0)
            {
                error = "No chat kinds given. Use a comma-separated list like `fc,ls1,tell`, or `any` / `all`.";
                return false;
            }

            var result = new List<XivChatType>();
            var unknown = new List<string>();
            foreach (var word in words)
            {
                var matched = word switch
                {
                    "any" => any.ToList(),
                    "all" => all.ToList(),
                    _ => XivChatTypeExtensions.TypeInfoDict.Where(kv => kv.Value.Slug == word).Select(kv => kv.Key).ToList(),
                };

                if (matched.Count == 0)
                    unknown.Add(word);

                foreach (var type in matched)
                {
                    if (!result.Contains(type))
                        result.Add(type);
                }
            }

            if (unknown.Count > 0)
            {
                error = $"Unknown chat kind{(unknown.Count > 1 ? "s" : string.Empty)}: {string.Join(", ", unknown.Select(u => $"`{u}`"))}. "
                        + "Examples: `say, shout, tell, p, fc, ls1`–`ls8`, `cwls1`–`cwls8`, `e`, or `any` / `all`.";
                return false;
            }

            types = result;
            return true;
        }

        /// <summary>
        /// A code-block list for a reply, or <paramref name="emptyText"/> when there's nothing. Truncated with
        /// "…and N more" so it always fits in an embed.
        /// </summary>
        internal static string FormatList(IEnumerable<string> lines, string emptyText)
        {
            var all = lines.ToList();
            if (all.Count == 0)
                return emptyText;

            var shown = new List<string>();
            var length = 0;
            foreach (var line in all)
            {
                if (length + line.Length + 1 > MaxListLength)
                    break;
                shown.Add(line);
                length += line.Length + 1;
            }

            var more = all.Count - shown.Count;
            return $"```\n{string.Join("\n", shown)}{(more > 0 ? $"\n…and {more} more" : string.Empty)}```";
        }

        /// <summary>"Free Company - 🍐" lines for a per-kind setting (prefixes, names, avatars), sorted by name.</summary>
        internal static IEnumerable<string> Describe(IReadOnlyDictionary<XivChatType, string> values) =>
            values.Select(kv => $"{kv.Key.GetFancyNameOrType()} - {kv.Value}").OrderBy(l => l, StringComparer.OrdinalIgnoreCase);

        /// <summary>Names of the given kinds, one per line, in their order.</summary>
        internal static IEnumerable<string> Names(IEnumerable<XivChatType> types) => types.Select(t => t.GetFancyNameOrType());

        /// <summary>Avatar URLs must be absolute http(s) links (they're handed to Discord as-is).</summary>
        internal static bool IsValidAvatarUrl(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

        /// <summary><c>setduplicatems</c>: a number of milliseconds ≥ 0, or <c>none</c>/<c>off</c> for 0.</summary>
        internal static bool TryParseDuplicateMs(string? text, out long ms)
        {
            ms = 0;
            if (text is null)
                return false;
            if (text.Equals("none", StringComparison.OrdinalIgnoreCase) || text.Equals("off", StringComparison.OrdinalIgnoreCase))
                return true;
            if (long.TryParse(text, out ms) && ms >= 0)
                return true;
            ms = 0;
            return false;
        }
    }
}
