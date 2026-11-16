using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.DiscordBridge.Model;
using Dalamud.Game.Text;
using Dalamud.Plugin;
using Newtonsoft.Json;

namespace Dalamud.DiscordBridge
{
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; }

        public long DuplicateCheckMS { get; set; } = 5000;

        [JsonIgnore] private IDalamudPluginInterface pluginInterface;

        public string DiscordToken { get; set; } = string.Empty;
        public string DiscordOwnerName { get; set; } = string.Empty;
        public string DiscordBotPrefix { get; set; } = "xl!";

        public string DefaultAvatarURL = Constant.LogoLink;
        public Dictionary<XivChatType, string> ChatTypeAvatarURL { get; set; } = [];

        public Dictionary<ulong, DiscordChannelConfig> ChannelConfigs { get; set; } = [];
        public Dictionary<XivChatType, string> PrefixConfigs { get; set; } = [];

        public Dictionary<XivChatType, string> CustomSlugsConfigs { get; set; } = [];
        public string CFPrefixConfig { get; set; } = "";
        public bool ForceDefaultNameAvatar { get; set; } = false;
        public bool ForceEmbedFallbackMode { get; set; } = false;
        public bool SenderInMessage { get; set; } = false;

        // Discord → game (inbound). Which channels relay in is per channel (DiscordChannelConfig.InboundChatType).

        /// <summary>Global kill switch for showing Discord messages in the chat log.</summary>
        public bool InboundDisplayEnabled { get; set; } = true;

        /// <summary>Print inbound messages as Echo (with the channel in the text) instead of as the target chat type.</summary>
        public bool InboundDisplayAsEcho { get; set; } = false;

        /// <summary>Sender label for inbound messages; <c>{name}</c> is the Discord display name.</summary>
        public string InboundSenderFormat { get; set; } = InboundFormat.DefaultSenderFormat;

        /// <summary>React on Discord to show whether a message reached the game (✅ shown, ⏸️ not logged in).</summary>
        public bool InboundReactions { get; set; } = true;


        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;
        }

        public void Save()
        {
            this.pluginInterface.SavePluginConfig(this);
        }
    }
}
