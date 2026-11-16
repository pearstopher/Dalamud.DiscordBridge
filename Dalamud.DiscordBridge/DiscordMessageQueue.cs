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

        private async Task ProcessRetainerSaleAsync(QueuedRetainerItemSaleEvent retainerSaleEvent)
        {
            if (retainerSaleEvent.Message.Payloads.FirstOrDefault(x => x.Type == PayloadType.Item) is not ItemPayload itemLink)
            {
                Logger?.Error("Retainer sale message had no item link; skipping. Msg: {0}", BitConverter.ToString(retainerSaleEvent.Message.Encode()));
                return;
            }

            var itemRow = Service.Data.GetExcelSheet<Item>().GetRow(itemLink.Item.RowId);

            // XIVAPI wants these padded with 0s in the front if under 6 digits.
            var iconFolder = $"{itemRow.Icon / 1000 * 1000}".PadLeft(6, '0');
            var iconFile = $"{itemRow.Icon}".PadLeft(6, '0');
            var avatarUrl = $"https://beta.xivapi.com/api/1/asset?path=ui%2Ficon%2F{iconFolder}%2F{iconFile}_hr1.tex&format=png";

            await this.plugin!.Discord.SendItemSaleEvent(itemRow.Name, avatarUrl, itemLink.Item.RowId, retainerSaleEvent.Message.TextValue, retainerSaleEvent.ChatType);
        }

        private async Task ProcessChatAsync(QueuedChatEvent chatEvent)
        {
            var plugin = this.plugin!;
            var senderName = (chatEvent.ChatType == XivChatType.TellOutgoing || chatEvent.ChatType == XivChatType.Echo)
                ? plugin.cachedPlayerName
                : chatEvent.Sender.ToString();
            var senderWorld = string.Empty;

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

                        if (senderWorld.Equals(string.Empty))
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

            await plugin.Discord.SendChatEvent(messageText, senderName ?? "Invalid SenderName", senderWorld ?? string.Empty, chatEvent.ChatType, chatEvent.AvatarUrl);
        }
    }
}
