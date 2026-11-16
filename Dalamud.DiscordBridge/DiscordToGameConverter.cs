using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.DiscordBridge.Model;

namespace Dalamud.DiscordBridge
{
    /// <summary>
    /// Makes text safe for the game: in tests a passthrough. In game, #16 plugs in the client's Utf8String.SanitizeString.
    /// </summary>
    public interface IGameTextSanitizer
    {
        string Sanitize(string text);
    }

    internal sealed class PassthroughSanitizer : IGameTextSanitizer
    {
        public static readonly PassthroughSanitizer Instance = new();

        public string Sanitize(string text) => text;
    }

    /// <summary>
    /// Converts a Discord message into a single line the FFXIV chat log can show (and later send).
    /// Pure: no Discord.Net client or Dalamud services.
    /// A leading '/' is kept: printing never runs commands, and the send path (<see cref="GameChatCommand.TryBuild"/>)
    /// rejects text that starts with '/'.
    /// </summary>
    internal sealed class DiscordToGameConverter
    {
        /// <summary>The game rejects chat messages longer than this many UTF-8 bytes.</summary>
        internal const int GameMaxBytes = 500;

        /// <summary>Longest sender label shown in game.</summary>
        internal const int MaxNameLength = 32;

        internal const string Ellipsis = "...";

        private readonly IGameTextSanitizer sanitizer;

        internal DiscordToGameConverter(IGameTextSanitizer? sanitizer = null)
        {
            this.sanitizer = sanitizer ?? PassthroughSanitizer.Instance;
        }

        /// <summary>
        /// Build the game text for a message, at most <paramref name="byteBudget"/> UTF-8 bytes.
        /// Returns an empty string if nothing displayable is left.
        /// </summary>
        internal string Convert(InboundDiscordMessage message, int byteBudget = GameMaxBytes)
        {
            var source = string.IsNullOrEmpty(message.CleanContent) ? message.Content : message.CleanContent;
            var text = ConvertText(source);

            var extras = Placeholders(message);
            if (extras.Length > 0)
                text = text.Length > 0 ? $"{text} {extras}" : extras;

            if (text.Length == 0)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(message.ReplyToAuthor))
                text = $"re @{ConvertName(message.ReplyToAuthor)}: {text}";

            text = this.sanitizer.Sanitize(text);

            return TruncateUtf8(text, byteBudget);
        }

        /// <summary>Clean a Discord display name for use as the in-game sender label.</summary>
        internal static string ConvertName(string? name)
        {
            var cleaned = CollapseWhitespace(StripUnsupported(ReplaceEmoji(name ?? string.Empty)));
            if (cleaned.Length == 0)
                return "Discord user";

            return cleaned.Length <= MaxNameLength ? cleaned : cleaned[..MaxNameLength].TrimEnd();
        }

        /// <summary>The text pipeline without placeholders, reply prefix or truncation.</summary>
        internal static string ConvertText(string? content)
        {
            if (string.IsNullOrEmpty(content))
                return string.Empty;

            var text = content;
            text = CustomEmoji.Replace(text, ":$1:");
            text = Timestamp.Replace(text, "[time]");
            text = CodeFence.Replace(text, m => m.Groups[1].Value);
            text = InlineCode.Replace(text, "$1");
            text = Spoiler.Replace(text, "[spoiler]");
            text = MaskedLink.Replace(text, "$1 ($2)");
            text = SuppressedLink.Replace(text, "$1");
            text = LinePrefix.Replace(text, string.Empty);
            text = Bold.Replace(text, "$1");
            text = Underline.Replace(text, "$1");
            text = Strike.Replace(text, "$1");
            text = Italic.Replace(text, "$1");
            text = ReplaceEmoji(text);
            text = StripUnsupported(text);
            return CollapseWhitespace(text);
        }

        /// <summary>"[image]", "[2 images] [file] [sticker: Wave]" etc.</summary>
        internal static string Placeholders(InboundDiscordMessage message)
        {
            var parts = new List<string>();
            var images = message.Attachments.Count(a => a.IsImage);
            var files = message.Attachments.Count - images;

            if (images > 0)
                parts.Add(images == 1 ? "[image]" : $"[{images} images]");
            if (files > 0)
                parts.Add(files == 1 ? "[file]" : $"[{files} files]");
            foreach (var sticker in message.Stickers)
                parts.Add($"[sticker: {ConvertName(sticker)}]");

            return string.Join(" ", parts);
        }

        /// <summary>Cut to at most <paramref name="maxBytes"/> UTF-8 bytes on a character boundary, adding "...".</summary>
        internal static string TruncateUtf8(string text, int maxBytes)
        {
            if (Encoding.UTF8.GetByteCount(text) <= maxBytes)
                return text;

            var budget = maxBytes - Encoding.UTF8.GetByteCount(Ellipsis);
            if (budget <= 0)
                return string.Empty;

            var builder = new StringBuilder();
            var used = 0;
            var elements = StringInfo.GetTextElementEnumerator(text);
            while (elements.MoveNext())
            {
                var element = (string)elements.Current;
                var size = Encoding.UTF8.GetByteCount(element);
                if (used + size > budget)
                    break;
                builder.Append(element);
                used += size;
            }

            return builder.ToString().TrimEnd() + Ellipsis;
        }

        // A small set of common emoji with readable stand-ins; every other emoji is dropped.
        private static readonly Dictionary<string, string> EmojiNames = new()
        {
            ["😀"] = ":D", ["😃"] = ":D", ["😄"] = ":D", ["😁"] = ":D", ["😆"] = "XD", ["😂"] = ":'D", ["🤣"] = ":'D",
            ["🙂"] = ":)", ["😊"] = ":)", ["😉"] = ";)", ["😛"] = ":P", ["😜"] = ";P", ["😮"] = ":O", ["😢"] = ":'(",
            ["😭"] = ":'(", ["🙁"] = ":(", ["☹"] = ":(", ["😠"] = ">:(", ["😍"] = "<3", ["❤"] = "<3", ["💕"] = "<3",
            ["👍"] = "(+1)", ["👎"] = "(-1)", ["🎉"] = "(yay)", ["🙏"] = "(thanks)", ["👋"] = "(wave)", ["🔥"] = "(fire)",
            ["😅"] = "^^;", ["🤔"] = "(hmm)", ["😎"] = "B)", ["🍐"] = "(pear)",
        };

        private static string ReplaceEmoji(string text)
        {
            var builder = new StringBuilder(text.Length);
            var elements = StringInfo.GetTextElementEnumerator(text);
            while (elements.MoveNext())
            {
                var element = (string)elements.Current;
                if (!IsEmoji(element))
                {
                    builder.Append(element);
                    continue;
                }

                var key = BaseEmoji(element);
                if (EmojiNames.TryGetValue(key, out var name))
                    builder.Append(' ').Append(name).Append(' ');
            }

            return builder.ToString();
        }

        private static bool IsEmoji(string element)
        {
            foreach (var rune in element.EnumerateRunes())
            {
                var v = rune.Value;
                if (v >= 0x1F000 || v == 0xFE0F || v == 0x200D || v == 0x20E3 || v == 0x2764 || v == 0x2639)
                    return true;
            }

            return false;
        }

        /// <summary>The emoji without variation selectors and skin-tone modifiers.</summary>
        private static string BaseEmoji(string element)
        {
            var builder = new StringBuilder();
            foreach (var rune in element.EnumerateRunes())
            {
                if (rune.Value == 0xFE0F || (rune.Value >= 0x1F3FB && rune.Value <= 0x1F3FF))
                    continue;
                builder.Append(rune.ToString());
            }

            return builder.ToString();
        }

        /// <summary>Remove control characters and the game's private-use glyph range (icons, auto-translate markers).</summary>
        private static string StripUnsupported(string text)
        {
            var builder = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                if (c is '\n' or '\r' or '\t')
                    builder.Append(' ');
                else if (char.IsControl(c) || (c >= '\uE000' && c <= '\uF8FF') || c is '\u200B' or '\u200C' or '\u200D' or '\uFEFF')
                    continue;
                else
                    builder.Append(c);
            }

            return builder.ToString();
        }

        private static string CollapseWhitespace(string text) => Whitespace.Replace(text, " ").Trim();

        private static readonly Regex CustomEmoji = new(@"<a?:(\w+):\d+>", RegexOptions.Compiled);
        private static readonly Regex Timestamp = new(@"<t:\d+(?::[tTdDfFR])?>", RegexOptions.Compiled);
        private static readonly Regex CodeFence = new(@"```(?:[\w+-]*\n)?([\s\S]*?)```", RegexOptions.Compiled);
        private static readonly Regex InlineCode = new(@"`([^`]+)`", RegexOptions.Compiled);
        private static readonly Regex Spoiler = new(@"\|\|[\s\S]+?\|\|", RegexOptions.Compiled);
        private static readonly Regex MaskedLink = new(@"\[([^\]]+)\]\(<?(https?://[^)>\s]+)>?\)", RegexOptions.Compiled);
        private static readonly Regex SuppressedLink = new(@"<(https?://[^>\s]+)>", RegexOptions.Compiled);
        private static readonly Regex LinePrefix = new(@"(?m)^(?:\s*(?:#{1,3}|-#|>>>|>)\s+)", RegexOptions.Compiled);
        private static readonly Regex Bold = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
        private static readonly Regex Underline = new(@"__(.+?)__", RegexOptions.Compiled);
        private static readonly Regex Strike = new(@"~~(.+?)~~", RegexOptions.Compiled);
        private static readonly Regex Italic = new(@"(?<![\w*])\*(?!\s)([^*\n]+?)(?<!\s)\*(?![\w*])", RegexOptions.Compiled);
        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    }
}
