namespace Dalamud.DiscordBridge
{
    static class Constant
    {
        /// <summary>Display name of the plugin (embed footers, UI, chat messages).</summary>
        public const string PluginName = "Discord Chat Pears";

        /// <summary>
        /// Name used for webhook posts made as the plugin itself. Discord rejects webhook usernames containing "discord".
        /// </summary>
        public const string WebhookName = "Chat Pears";

        /// <summary>Main in-game command (settings window).</summary>
        public const string MainCommand = "/dcp";

        /// <summary>InternalName of the official plugin this fork is based on; its config can be imported once.</summary>
        public const string LegacyInternalName = "Dalamud.DiscordBridge";

        public const string HelpLink = "https://github.com/reiichi001/Dalamud.DiscordBridge/wiki/Setup-Guide";
        public const string KindListLink = "https://github.com/reiichi001/Dalamud.DiscordBridge/wiki/Chat-kinds";
        public const string DiscordJoinLink = "https://discord.gg/3NMcUV5";
        public const string LogoLink = "https://raw.githubusercontent.com/goatcorp/DalamudAssets/master/UIRes/logo.png";
    }
}
