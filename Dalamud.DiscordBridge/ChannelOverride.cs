using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge
{
    /// <summary>
    /// Per-message channel override typed in Discord: "!fc hello", "!e note to self", "!t Bob Smith@World hi".
    /// Wins over reply routing and the channel's setinbound default, for that one message. Pure (unit tested).
    /// </summary>
    internal static class ChannelOverride
    {
        internal enum Kind
        {
            /// <summary>Not an override; route the message normally.</summary>
            None,

            /// <summary>A valid override: use <see cref="Result.Target"/> and <see cref="Result.Text"/>.</summary>
            Override,

            /// <summary>Looks like an override but isn't valid (unknown channel, bad tell name). Don't relay; tell the user.</summary>
            Invalid,
        }

        internal readonly record struct Result(Kind Kind, ReplyTarget Target, string Text, string? Error);

        private static readonly Dictionary<string, XivChatType> Aliases = BuildAliases();

        private static readonly HashSet<string> TellAliases = ["t", "tell"];

        // "!word" followed by whitespace or the end. Anything else ("!!!", "! hi", "!") isn't an override.
        private static readonly Regex Command = new(@"^\s*!(?<word>[A-Za-z][A-Za-z0-9]*)(?:\s+(?<rest>[\s\S]*))?$", RegexOptions.Compiled);

        private static readonly Regex TellArgs = new(
            @"^(?<name>[A-Za-z][A-Za-z'\-]* [A-Za-z][A-Za-z'\-]*(?:[@＠][A-Za-z]+)?)(?:\s+(?<text>[\s\S]*))?$", RegexOptions.Compiled);

        /// <summary>Short list for help and error text.</summary>
        internal const string Examples = "!e, !s, !sh, !y, !p, !a, !fc, !l1–!l8, !cwl1–!cwl8, !n, !em, !t First Last@World";

        internal static Result Parse(string? content)
        {
            var match = Command.Match(content ?? string.Empty);
            if (!match.Success)
                return new Result(Kind.None, default, content ?? string.Empty, null);

            var word = match.Groups["word"].Value.ToLowerInvariant();
            var rest = match.Groups["rest"].Success ? match.Groups["rest"].Value.Trim() : string.Empty;

            if (TellAliases.Contains(word))
            {
                var tell = TellArgs.Match(rest);
                if (!tell.Success)
                    return Invalid($"no one to send the tell to. Use `!t First Last@World message`");

                var partner = tell.Groups["name"].Value.Replace('@', '＠');
                var text = tell.Groups["text"].Success ? tell.Groups["text"].Value.Trim() : string.Empty;
                return new Result(Kind.Override, new ReplyTarget(XivChatType.TellOutgoing, partner), text, null);
            }

            if (Aliases.TryGetValue(word, out var type))
                return new Result(Kind.Override, new ReplyTarget(type, null), rest, null);

            return Invalid($"unknown channel `!{word}`. Use {Examples}");
        }

        private static Result Invalid(string error) => new(Kind.Invalid, default, string.Empty, error);

        private static Dictionary<string, XivChatType> BuildAliases()
        {
            var aliases = new Dictionary<string, XivChatType>
            {
                ["e"] = XivChatType.Echo, ["echo"] = XivChatType.Echo,
                ["s"] = XivChatType.Say, ["say"] = XivChatType.Say,
                ["sh"] = XivChatType.Shout, ["shout"] = XivChatType.Shout,
                ["y"] = XivChatType.Yell, ["yell"] = XivChatType.Yell,
                ["p"] = XivChatType.Party, ["party"] = XivChatType.Party,
                ["a"] = XivChatType.Alliance, ["alliance"] = XivChatType.Alliance,
                ["fc"] = XivChatType.FreeCompany,
                ["n"] = XivChatType.NoviceNetwork, ["nn"] = XivChatType.NoviceNetwork,
                ["em"] = XivChatType.CustomEmote, ["customemote"] = XivChatType.CustomEmote,
            };

            XivChatType[] linkshells = [XivChatType.Ls1, XivChatType.Ls2, XivChatType.Ls3, XivChatType.Ls4,
                                        XivChatType.Ls5, XivChatType.Ls6, XivChatType.Ls7, XivChatType.Ls8];
            XivChatType[] crossWorld = [XivChatType.CrossLinkShell1, XivChatType.CrossLinkShell2, XivChatType.CrossLinkShell3, XivChatType.CrossLinkShell4,
                                        XivChatType.CrossLinkShell5, XivChatType.CrossLinkShell6, XivChatType.CrossLinkShell7, XivChatType.CrossLinkShell8];
            for (var i = 0; i < 8; i++)
            {
                aliases[$"l{i + 1}"] = linkshells[i];
                aliases[$"ls{i + 1}"] = linkshells[i];
                aliases[$"cwl{i + 1}"] = crossWorld[i];
                aliases[$"cwls{i + 1}"] = crossWorld[i];
            }

            return aliases;
        }

        /// <summary>For tests: every alias maps to an inbound-allowed chat type.</summary>
        internal static IEnumerable<(string Alias, XivChatType Type)> AllAliases() => Aliases.Select(kv => (kv.Key, kv.Value));
    }
}
