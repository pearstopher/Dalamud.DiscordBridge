using System;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.DiscordBridge.Model;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Lumina.Excel.Sheets;

namespace Dalamud.DiscordBridge
{
    /// <summary>
    /// Serial, in-order queue of game events waiting to be relayed to Discord.
    /// A single background task drains it; it sleeps on a semaphore when idle.
    /// </summary>
    public class DiscordMessageQueue : SerialWorkQueue<QueuedXivEvent>
    {
        private static IPluginLog? Logger => Service.Logger;

        private readonly DiscordBridgePlugin? plugin;

        public DiscordMessageQueue(DiscordBridgePlugin plugin)
            : base("Discord message queue", _ => Task.CompletedTask)
        {
            this.plugin = plugin;
            this.Processor = this.ProcessEventAsync;
        }

        /// <summary>
        /// Test seam: process events with a custom handler instead of sending to Discord.
        /// </summary>
        internal DiscordMessageQueue(Func<QueuedXivEvent, Task> processor)
            : base("Discord message queue", processor)
        {
        }

        private async Task ProcessEventAsync(QueuedXivEvent queuedEvent)
        {
            switch (queuedEvent)
            {
                case QueuedRetainerItemSaleEvent retainerSaleEvent:
                    await this.ProcessRetainerSaleAsync(retainerSaleEvent);
                    break;
                case QueuedChatEvent chatEvent:
                    await this.ProcessChatAsync(chatEvent);
                    break;
                case QueuedContentFinderEvent cfEvent:
                    await this.plugin!.Discord.SendContentFinderEvent(cfEvent);
                    break;
            }
        }

        /// <summary>
        /// The recipient of an outgoing tell from the message's sender field: "Name＠World" from the player link, or the
        /// cleaned raw text when there's no link. Null if nothing usable.
        /// </summary>
        internal static string? TellRecipient(SeString sender)
        {
            if (sender.Payloads.FirstOrDefault(p => p.Type == PayloadType.Player) is PlayerPayload player)
            {
                var world = player.World.Value.Name.ExtractText();
                return string.IsNullOrEmpty(world) ? player.PlayerName : $"{player.PlayerName}＠{world}";
            }

            var text = ChatContentFormat.CleanRecipientText(sender.TextValue);
            return text.Length > 0 ? text : null;
        }

        private static int fcLoginFormatLogged;

        /// <summary>
        /// Who an FC login/logout line is about (#53): the player link in the message or sender, else the name at the
        /// start of the text; the world is your home world (FC members share it; the link's world is bogus, #55).
        /// </summary>
        /// <param name="flattenedText">The line as relayed text; defaults to the message's plain text.</param>
        internal static (string Name, string World, string Text) ResolveFcLogin(SeString message, SeString sender, string? homeWorld, string? flattenedText = null)
        {
            // Record the real format once per session, so it can be checked in /xllog (Debug).
            if (System.Threading.Interlocked.Exchange(ref fcLoginFormatLogged, 1) == 0)
                Logger?.Debug("FC login line format: message [{0}], sender [{1}]",
                    string.Join(", ", message.Payloads.Select(p => p.Type)), string.Join(", ", sender.Payloads.Select(p => p.Type)));

            var link = message.Payloads.OfType<PlayerPayload>().FirstOrDefault()
                       ?? sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
            string? linkWorld = null;
            if (link != null)
            {
                try
                {
                    linkWorld = link.World.Value.Name.ExtractText();
                }
                catch (Exception ex)
                {
                    Logger?.Debug(ex, "FC login line: couldn't read the player link's world.");
                }
            }

            return FcLoginNotice.Resolve(link?.PlayerName, linkWorld, flattenedText ?? message.TextValue, homeWorld);
        }

        /// <summary>XIVAPI asset URL for an item icon (folder and file are zero-padded to 6 digits).</summary>
        internal static string ItemIconUrl(uint icon)
        {
            var iconFolder = $"{icon / 1000 * 1000}".PadLeft(6, '0');
            var iconFile = $"{icon}".PadLeft(6, '0');
            return $"https://beta.xivapi.com/api/1/asset?path=ui%2Ficon%2F{iconFolder}%2F{iconFile}_hr1.tex&format=png";
        }

        private async Task ProcessRetainerSaleAsync(QueuedRetainerItemSaleEvent retainerSaleEvent)
        {
            if (retainerSaleEvent.Message.Payloads.FirstOrDefault(x => x.Type == PayloadType.Item) is not ItemPayload itemLink)
            {
                Logger?.Error("Retainer sale message had no item link; skipping. Msg: {0}", BitConverter.ToString(retainerSaleEvent.Message.Encode()));
                return;
            }

            var itemRow = Service.Data.GetExcelSheet<Item>().GetRow(itemLink.Item.RowId);

            var avatarUrl = ItemIconUrl(itemRow.Icon);

            await this.plugin!.Discord.SendItemSaleEvent(itemRow.Name, avatarUrl, itemLink.Item.RowId, retainerSaleEvent.Message.TextValue, retainerSaleEvent.ChatType);
        }

        private async Task ProcessChatAsync(QueuedChatEvent chatEvent)
        {
            var plugin = this.plugin!;
            var senderName = (chatEvent.ChatType == XivChatType.TellOutgoing || chatEvent.ChatType == XivChatType.Echo)
                ? plugin.cachedPlayerName
                : chatEvent.Sender.ToString();
            var senderWorld = string.Empty;

            // For outgoing tells the game's "sender" is the person the tell was sent to.
            string? recipient = null;
            if (chatEvent.ChatType == XivChatType.TellOutgoing)
            {
                try
                {
                    recipient = TellRecipient(chatEvent.Sender);
                }
                catch (Exception ex)
                {
                    Logger?.Warning(ex, "Could not read the tell recipient.");
                }
            }

            try
            {
                if (plugin.cachedPlayerName != null)
                {
                    if (chatEvent.Sender.Payloads.FirstOrDefault(x => x.Type == PayloadType.Player) is not PlayerPayload playerLink)
                    {
                        // Chat messages from the local player don't include a player link, just the raw name.
                        // In party/alliance the raw name is prefixed by the party position number.
                        if (chatEvent.Sender.TextValue.EndsWith(plugin.cachedPlayerName))
                        {
                            senderName = plugin.cachedPlayerName;
                        }
                        else
                        {
                            switch (chatEvent.ChatType)
                            {
                                case XivChatType.Debug:
                                case XivChatType.Urgent:
                                case XivChatType.Notice:
                                    break;
                                case XivChatType.TellOutgoing:
                                case XivChatType.Echo:
                                    senderName = plugin.cachedPlayerName;
                                    break;
                                case XivChatType.StandardEmote:
                                    // Use the emoting player's world; cross-world players would otherwise get ours.
                                    playerLink = (PlayerPayload)chatEvent.Message.Payloads.First(x => x.Type == PayloadType.Player);
                                    senderName = playerLink.PlayerName;
                                    senderWorld = playerLink.World.Value.Name.ExtractText();
                                    break;
                                case XivChatType.NPCDialogue:
                                case XivChatType.NPCDialogueAnnouncements:
                                    senderName = chatEvent.Sender.TextValue;
                                    senderWorld = "NPC";
                                    break;
                                default:
                                    // Ignore the many non-chat types that never carry a player link.
                                    var t = (int)chatEvent.ChatType;
                                    if ((t >= 41 && t <= 55) || (t >= 57 && t <= 70) || (t >= 72 && t <= 100) || t > 107)
                                        break;
                                    Logger?.Error($"playerLink was null. ChatType: {chatEvent.ChatType} ({t})");
                                    senderName = chatEvent.Sender.TextValue;
                                    break;
                            }
                        }

                        // GM messages without a player link: don't stamp our own world on the GM's name.
                        if (senderWorld.Equals(string.Empty) && !ReplyRouting.IsGmChannel(chatEvent.ChatType))
                            senderWorld = plugin.cachedPlayerWorld;
                    }
                    else
                    {
                        senderName = chatEvent.ChatType == XivChatType.TellOutgoing
                            ? plugin.cachedPlayerName
                            : playerLink.PlayerName;
                        senderWorld = chatEvent.ChatType == XivChatType.TellOutgoing
                            ? plugin.cachedPlayerWorld
                            : playerLink.World.Value.Name.ExtractText();
                    }
                }
                else
                {
                    senderName = string.Empty;
                    senderWorld = string.Empty;
                }
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "Could not deduce player name.");
            }

            var messageText = "";
            chatEvent.Message.Payloads.ForEach(x =>
            {
                if (x.Type == PayloadType.EmphasisItalic)
                    messageText += "_";
                else if (x.Type == PayloadType.UIGlow)
                    messageText += "**";
                else if (x is ITextProvider provider)
                    messageText += provider.Text;
            });

            // FC member logged in/out (#53): post under the member's name and portrait, with 🟢/⚫.
            if (FcLoginNotice.Is(chatEvent.ChatType))
                (senderName, senderWorld, messageText) = ResolveFcLogin(chatEvent.Message, chatEvent.Sender, plugin.cachedPlayerWorld, messageText);

            await plugin.Discord.SendChatEvent(messageText, senderName ?? "Invalid SenderName", senderWorld ?? string.Empty, chatEvent.ChatType, chatEvent.AvatarUrl, recipient);
        }
    }
}
