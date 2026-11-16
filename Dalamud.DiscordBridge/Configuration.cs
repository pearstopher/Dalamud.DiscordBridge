using System.Collections.Concurrent;
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
        // Concurrent (#46): commands (Discord gateway), the relay queue and the UI read and write these at the same time.
        // Same JSON shape as a plain Dictionary, so older configs load unchanged.
        public ConcurrentDictionary<XivChatType, string> ChatTypeAvatarURL { get; set; } = new();

        public ConcurrentDictionary<ulong, DiscordChannelConfig> ChannelConfigs { get; set; } = new();
        public ConcurrentDictionary<XivChatType, string> PrefixConfigs { get; set; } = new();

        public ConcurrentDictionary<XivChatType, string> CustomSlugsConfigs { get; set; } = new();
        public string CFPrefixConfig { get; set; } = "";
        public bool ForceDefaultNameAvatar { get; set; } = false;
        public bool ForceEmbedFallbackMode { get; set; } = false;
        public bool SenderInMessage { get; set; } = false;

        /// <summary>Post "online" / "offline" in every bridged channel when the plugin loads and unloads (#34).</summary>
        public bool PostStatusMessages { get; set; } = true;

        // Discord → game (inbound). Which channels relay in is per channel (DiscordChannelConfig.InboundChatType).

        /// <summary>Global kill switch for showing Discord messages in the chat log.</summary>
        public bool InboundDisplayEnabled { get; set; } = true;

        /// <summary>Print inbound messages as Echo (with the channel in the text) instead of as the target chat type.</summary>
        public bool InboundDisplayAsEcho { get; set; } = false;

        /// <summary>Sender label for inbound messages; <c>{name}</c> is the Discord display name.</summary>
        public string InboundSenderFormat { get; set; } = InboundFormat.DefaultSenderFormat;

        /// <summary>React on Discord to show whether a message reached the game (✅ shown, ⏸️ not logged in).</summary>
        public bool InboundReactions { get; set; } = true;

        /// <summary>
        /// Master switch for "send as me": the owner's Discord messages are said in game by their character in channels
        /// in every channel with inbound relay. Controlled only here (not from Discord). Off by default; outside Dalamud's rules.
        /// </summary>
        public bool AllowSendAsCharacter { get; set; } = false;

        /// <summary>Show messages the plugin sent for real with a tag on your screen (others see the normal message).</summary>
        public bool MarkSentInGame { get; set; } = true;

        /// <summary>Tag shown on your screen for messages the plugin sent for real.</summary>
        public string SentTag { get; set; } = "[S]";

        /// <summary>Minimum time between "send as me" messages.</summary>
        public int SendIntervalMs { get; set; } = 2000;

        /// <summary>How many "send as me" messages may wait for their turn before more are rejected.</summary>
        public int SendQueueLimit { get; set; } = 5;

        // Developer self-tests (dev builds only).

        /// <summary>Discord channel used by the self-test round trip (set with the selftesthere command).</summary>
        public ulong? SelfTestChannelId { get; set; }

        /// <summary>Dev builds: run the self-tests automatically after each (re)load, once logged in and connected.</summary>
        public bool DevRunSelfTestsOnLoad { get; set; } = false;

        /// <summary>Dev builds: allow the send.tell-self self-test to send a real tell to your own character.</summary>
        public bool DevSelfTestTellSelf { get; set; } = false;


        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;
        }

        [JsonIgnore] private readonly object saveLock = new();

        /// <summary>Write the config file. Serialized (#46): two threads saving at once could collide on the file.</summary>
        public void Save()
        {
            lock (this.saveLock)
                this.pluginInterface.SavePluginConfig(this);
        }
    }
}
