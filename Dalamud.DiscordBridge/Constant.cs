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

        /// <summary>This fork's repository. Help goes here, never to the upstream maintainers (#45).</summary>
        public const string RepoLink = "https://github.com/pearstopher/Dalamud.DiscordBridge";

        /// <summary>User guide: every command, override, reaction and tag.</summary>
        public const string HelpLink = RepoLink + "/blob/master/docs/COMMANDS.md";

        /// <summary>Where the chat kinds are listed.</summary>
        public const string KindListLink = HelpLink;

        /// <summary>Questions and bug reports.</summary>
        public const string IssuesLink = RepoLink + "/issues";
        public const string LogoLink = "https://raw.githubusercontent.com/goatcorp/DalamudAssets/master/UIRes/logo.png";
    }
}
