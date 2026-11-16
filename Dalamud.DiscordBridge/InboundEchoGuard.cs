using System;
using System.Collections.Generic;
using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge
{
    /// <summary>
    /// Prevents relay loops: anything the plugin prints into (or sends to) the game because of a Discord message comes
    /// back through IChatGui.ChatMessage, and would otherwise be relayed straight back to Discord. Register each line
    /// before printing/sending it; <see cref="TryConsume"/> then swallows the matching echo once.
    /// </summary>
    /// <summary>Why a line is expected back: we printed it locally, or we sent it through the game.</summary>
    internal enum EchoKind
    {
        Displayed,
        Sent,
    }

    internal sealed class InboundEchoGuard
    {
        internal static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(10);

        private readonly Func<DateTimeOffset> clock;
        private readonly TimeSpan window;
        private readonly object gate = new();
        private readonly List<(int Type, string Text, DateTimeOffset At, EchoKind Kind)> pending = [];

        internal InboundEchoGuard(Func<DateTimeOffset>? clock = null, TimeSpan? window = null)
        {
            this.clock = clock ?? (() => DateTimeOffset.UtcNow);
            this.window = window ?? DefaultWindow;
        }

        /// <summary>Number of echoes still expected (for tests and diagnostics).</summary>
        internal int PendingCount
        {
            get
            {
                lock (this.gate)
                {
                    this.Expire();
                    return this.pending.Count;
                }
            }
        }

        /// <summary>Expect the game to echo <paramref name="text"/> as <paramref name="type"/> shortly.</summary>
        internal void Register(XivChatType type, string text, EchoKind kind = EchoKind.Displayed)
        {
            lock (this.gate)
            {
                this.Expire();
                this.pending.Add((Normalize(type), Normalize(text), this.clock(), kind));
            }
        }

        /// <summary>
        /// If this chat line is an expected echo, forget it and return why it was expected; otherwise null.
        /// </summary>
        internal EchoKind? Consume(XivChatType type, string text)
        {
            var key = (Normalize(type), Normalize(text));
            lock (this.gate)
            {
                this.Expire();
                var index = this.pending.FindIndex(p => p.Type == key.Item1 && p.Text == key.Item2);
                if (index < 0)
                    return null;

                var kind = this.pending[index].Kind;
                this.pending.RemoveAt(index);
                return kind;
            }
        }

        /// <summary>
        /// True if this chat line is an expected echo (and forget it, so a later identical line from a real player
        /// is still relayed).
        /// </summary>
        internal bool TryConsume(XivChatType type, string text) => this.Consume(type, text) != null;

        private void Expire()
        {
            var cutoff = this.clock() - this.window;
            this.pending.RemoveAll(p => p.At < cutoff);
        }

        private static int Normalize(XivChatType type) => (int)type & 0x7F;

        private static string Normalize(string text) => text.Trim();
    }
}
