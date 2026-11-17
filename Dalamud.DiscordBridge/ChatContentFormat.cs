using System.Text;

namespace Dalamud.DiscordBridge
{
    /// <summary>
    /// Pure formatting of relayed chat lines for Discord (unit tested). The format is parsed back by
    /// <see cref="ContentParser"/> for duplicate detection, so keep the two in sync.
    /// </summary>
    internal static class ChatContentFormat
    {
        /// <summary>
        /// The message body after the slug: "text", "Me: text", "→ Bob＠World: text" or "Me → Bob＠World: text".
        /// </summary>
        internal static string Body(string message, bool senderInMessage, string displayName, string? recipient)
        {
            if (!string.IsNullOrWhiteSpace(recipient))
                return senderInMessage ? $"{displayName} → {recipient}: {message}" : $"→ {recipient}: {message}";

            return senderInMessage ? $"{displayName}: {message}" : message;
        }

        /// <summary>
        /// Full content: "{prefix}[{slug}] {body}", or "{prefix} {body}" for IPC messages (no slug). Plain, not bold: bold
        /// added four characters to push notifications (#56).
        /// </summary>
        internal static string Content(string? prefix, string slug, bool isIpc, string body)
            => isIpc ? $"{prefix} {body}" : $"{prefix}[{slug}] {body}";

        /// <summary>
        /// Clean the raw sender text of an outgoing tell (used when there's no player link): drop the game's
        /// "&gt;&gt;" marker, private-use icons and surrounding whitespace.
        /// </summary>
        internal static string CleanRecipientText(string raw)
        {
            var builder = new StringBuilder(raw.Length);
            foreach (var c in raw)
            {
                if (c >= '' && c <= '')
                    continue;
                builder.Append(c);
            }

            var text = builder.ToString().Trim();
            while (text.StartsWith('>'))
                text = text[1..].TrimStart();
            return text.TrimEnd(':').Trim();
        }
    }
}
