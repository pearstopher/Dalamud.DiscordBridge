using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.DiscordBridge.Model;
using Dalamud.Game.Text;
using Discord;
using Discord.WebSocket;

namespace Dalamud.DiscordBridge
{
    /// <summary>A Discord text command: whether only the owner may run it, and what it does.</summary>
    internal readonly record struct DiscordCommand(bool OwnerOnly, Func<DiscordHandler, SocketMessage, string[], Task> Run);

    /// <summary>
    /// The Discord text commands (#52), one method each, dispatched by name from <see cref="Commands"/> in
    /// <c>SocketClientOnMessageReceived</c>. <c>ping</c>/<c>test</c> are handled before the table (anyone may use them).
    /// To add a command: write a method, add a table row, add a help field (docs/ADDING_FEATURES.md).
    /// </summary>
    public partial class DiscordHandler
    {
        /// <summary>Every command by name (what follows the prefix). Owner-only commands are checked before running.</summary>
        internal static readonly IReadOnlyDictionary<string, DiscordCommand> Commands = new Dictionary<string, DiscordCommand>
        {
            ["setchannel"] = new(true, (h, m, a) => h.SetChannelAsync(m, a)),
            ["unsetchannel"] = new(true, (h, m, a) => h.UnsetChannelAsync(m, a)),
            ["setprefix"] = new(true, (h, m, a) => h.SetPrefixAsync(m, a)),
            ["unsetprefix"] = new(true, (h, m, a) => h.UnsetPrefixAsync(m, a)),
            ["setchattypename"] = new(true, (h, m, a) => h.SetChatTypeNameAsync(m, a)),
            ["unsetchattypename"] = new(true, (h, m, a) => h.UnsetChatTypeNameAsync(m, a)),
            ["setduplicatems"] = new(true, (h, m, a) => h.SetDuplicateMsAsync(m, a)),
            ["toggledf"] = new(true, (h, m, a) => h.ToggleDutyFinderAsync(m, a)),
            ["togglestatus"] = new(true, (h, m, a) => h.ToggleStatusAsync(m, a)),
            ["toggleembed"] = new(true, (h, m, a) => h.ToggleEmbedAsync(m, a)),
            ["toggledefaultnameavatar"] = new(true, (h, m, a) => h.ToggleDefaultNameAvatarAsync(m, a)),
            ["togglesender"] = new(true, (h, m, a) => h.ToggleSenderAsync(m, a)),
            ["setcfprefix"] = new(true, (h, m, a) => h.SetCfPrefixAsync(m, a)),
            ["setinbound"] = new(true, (h, m, a) => h.SetInboundAsync(m, a)),
            ["setup"] = new(true, (h, m, a) => h.SetupAsync(m, a)),
            ["selftesthere"] = new(true, (h, m, a) => h.SelfTestHereAsync(m, a)),
            ["setinboundperm"] = new(true, (h, m, a) => h.SetInboundPermAsync(m, a)),
            ["listchannel"] = new(true, (h, m, a) => h.ListChannelAsync(m, a)),
            ["setavatar"] = new(true, (h, m, a) => h.SetAvatarAsync(m, a)),
            ["unsetavatar"] = new(true, (h, m, a) => h.UnsetAvatarAsync(m, a)),
            ["pause"] = new(true, (h, m, a) => h.PauseAsync(m, true)),
            ["resume"] = new(true, (h, m, a) => h.PauseAsync(m, false)),
            ["help"] = new(false, (h, m, a) => h.HelpAsync(m, a)),
        };

        /// <summary>xl!pause / xl!resume (#54): stop or restart all relaying, kept across restarts.</summary>
        private async Task PauseAsync(SocketMessage message, bool pause)
        {
            if (!this.plugin.SetPaused(pause, "Discord"))
            {
                await SendGenericEmbed(message.Channel,
                    pause ? $"Already paused. ``{this.plugin.Config.DiscordBotPrefix}resume`` to continue." : "Already running.",
                    pause ? "Paused" : "Running", EmbedColorFine);
                return;
            }

            // The change is announced in every bridged channel; answer here only if this channel won't get that post.
            var announcedHere = this.plugin.Config.PostStatusMessages &&
                                StatusAnnouncer.TargetChannels(this.plugin.Config.ChannelConfigs).Contains(message.Channel.Id);
            if (!announcedHere)
                await SendGenericEmbed(message.Channel, PauseStatus.ChangeText(pause, "Discord", this.plugin.Config.DiscordBotPrefix),
                    PauseStatus.ChangeTitle(pause), (uint)(pause ? EmbedColorError : EmbedColorFine));
        }

        private async Task SetChannelAsync(SocketMessage message, string[] args)
        {
            if (await this.TryParseKindsAsync(message, args, 2, "setchannel <kind1,kind2,...>") is not { } types)
                return;

            if (!this.plugin.Config.ChannelConfigs.TryGetValue(message.Channel.Id, out var config))
                config = new DiscordChannelConfig();
            config.SetUnique(types);
            this.plugin.Config.SetChannel(message.Channel.Id, config);
            this.plugin.Config.Save();

            await SendGenericEmbed(message.Channel,
                $"OK! This channel has been set to receive the following chat kinds:\n\n{ChatKindArgs.FormatList(ChatKindArgs.Names(config.ChatTypes), "(none)")}",
                "Chat kinds set", EmbedColorFine);
        }

        private async Task UnsetChannelAsync(SocketMessage message, string[] args)
        {
            if (await this.TryParseKindsAsync(message, args, 2, "unsetchannel <kind1,kind2,...>") is not { } types)
                return;

            if (!this.plugin.Config.ChannelConfigs.TryGetValue(message.Channel.Id, out var config))
                config = new DiscordChannelConfig();
            config.UnsetUnique(types);
            this.plugin.Config.SetChannel(message.Channel.Id, config);
            this.plugin.Config.Save();

            await SendGenericEmbed(message.Channel,
                config.ChatTypes.Count == 0
                    ? "OK! All chat kinds have been removed from this channel."
                    : $"OK! This channel will still receive the following chat kinds:\n\n{ChatKindArgs.FormatList(ChatKindArgs.Names(config.ChatTypes), "(none)")}",
                "Chat kinds unset", EmbedColorFine);
        }

        private async Task SetPrefixAsync(SocketMessage message, string[] args)
        {
            if (await this.TryParseKindsAsync(message, args, 3, "setprefix <kind1,kind2,...> <prefix|none>") is not { } types)
                return;

            var prefix = args[2] == "none" ? string.Empty : args[2];
            this.plugin.Config.ChangePrefixes(m =>
            {
                foreach (var type in types)
                    m[type] = prefix;
            });
            this.plugin.Config.Save();

            await SendGenericEmbed(message.Channel,
                $"OK! The following prefixes are set:\n\n{ChatKindArgs.FormatList(ChatKindArgs.Describe(this.plugin.Config.PrefixConfigs), "(none)")}",
                "Prefix set", EmbedColorFine);
        }

        private async Task UnsetPrefixAsync(SocketMessage message, string[] args)
        {
            if (await this.TryParseKindsAsync(message, args, 2, "unsetprefix <kind1,kind2,...>") is not { } types)
                return;

            this.plugin.Config.ChangePrefixes(m =>
            {
                foreach (var type in types)
                    m.Remove(type);
            });
            this.plugin.Config.Save();

            await SendGenericEmbed(message.Channel,
                this.plugin.Config.PrefixConfigs.Count == 0
                    ? "OK! All prefixes have been removed."
                    : $"OK! The following prefixes are still set:\n\n{ChatKindArgs.FormatList(ChatKindArgs.Describe(this.plugin.Config.PrefixConfigs), "(none)")}",
                "Prefix unset", EmbedColorFine);
        }

        private async Task SetChatTypeNameAsync(SocketMessage message, string[] args)
        {
            if (await this.TryParseKindsAsync(message, args, 3, "setchattypename <kind1,kind2,...> <name|none>") is not { } types)
                return;

            var customName = string.Join(" ", args.Skip(2)).Trim().Trim('"');
            this.plugin.Config.ChangeCustomSlugs(m =>
            {
                foreach (var type in types)
                {
                    if (customName == "none" || customName.Length == 0)
                        m.Remove(type);
                    else
                        m[type] = customName;
                }
            });

            this.plugin.Config.Save();

            await SendGenericEmbed(message.Channel,
                $"OK! Custom chat kind names:\n\n{ChatKindArgs.FormatList(ChatKindArgs.Describe(this.plugin.Config.CustomSlugsConfigs), "(none: every kind uses its normal name)")}",
                "Custom chat type set", EmbedColorFine);
        }

        private async Task UnsetChatTypeNameAsync(SocketMessage message, string[] args)
        {
            if (await this.TryParseKindsAsync(message, args, 2, "unsetchattypename <kind1,kind2,...>") is not { } types)
                return;

            this.plugin.Config.ChangeCustomSlugs(m =>
            {
                foreach (var type in types)
                    m.Remove(type);
            });
            this.plugin.Config.Save();

            await SendGenericEmbed(message.Channel,
                $"OK! Custom chat kind names:\n\n{ChatKindArgs.FormatList(ChatKindArgs.Describe(this.plugin.Config.CustomSlugsConfigs), "(none: every kind uses its normal name)")}",
                "Custom chat type unset", EmbedColorFine);
        }

        private async Task SetDuplicateMsAsync(SocketMessage message, string[] args)
        {
            if (args.Length != 2 || !ChatKindArgs.TryParseDuplicateMs(args[1], out var newDelay))
            {
                await SendGenericEmbed(message.Channel,
                    $"You need to specify a number of milliseconds (0 or `none` turns the feature off).\nFormat: ``{this.plugin.Config.DiscordBotPrefix}setduplicatems 5000``",
                    "Error", EmbedColorError);
                return;
            }

            this.plugin.Config.DuplicateCheckMS = newDelay;
            this.plugin.Config.Save();

            await SendGenericEmbed(message.Channel,
                newDelay == 0
                    ? "OK! The duplicate chat removal feature has been disabled."
                    : $"OK! Any messages with the same content within the last **{newDelay}** milliseconds will be skipped, preventing duplicate posts.",
                "Duplicate Message Check", EmbedColorFine);
        }

        private async Task ToggleDutyFinderAsync(SocketMessage message, string[] args)
        {
            if (!this.plugin.Config.ChannelConfigs.TryGetValue(message.Channel.Id, out var config))
                config = new DiscordChannelConfig();

            config.IsContentFinder = !config.IsContentFinder;

            this.plugin.Config.SetChannel(message.Channel.Id, config);
            this.plugin.Config.Save();

            await SendGenericEmbed(message.Channel,
                $"OK! This channel has been {(config.IsContentFinder ? "enabled" : "disabled")} from receiving Duty Finder notifications.",
                "Duty Finder set", EmbedColorFine);
        }

        private async Task ToggleStatusAsync(SocketMessage message, string[] args)
        {
            this.plugin.Config.PostStatusMessages = !this.plugin.Config.PostStatusMessages;
            this.plugin.Config.Save();

            await SendGenericEmbed(message.Channel,
                this.plugin.Config.PostStatusMessages
                    ? "OK! The bot will post 🟢 online / 🔴 offline in every bridged channel when the plugin starts and stops."
                    : "OK! The bot won't post online / offline messages any more.",
                "Status messages", EmbedColorFine);
        }

        private async Task ToggleEmbedAsync(SocketMessage message, string[] args)
        {
            this.plugin.Config.ForceEmbedFallbackMode = !this.plugin.Config.ForceEmbedFallbackMode;
            this.plugin.Config.Save();

            await SendGenericEmbed(message.Channel,
                $"OK! Sending relayed messages in embed fallback mode have been {(this.plugin.Config.ForceEmbedFallbackMode ? "**enabled**." : $"**disabled**.\n\nPlease make sure {Constant.PluginName} is allowed to manage webhooks in your channels as needed.")}",
                "Embed Fallback Mode set", EmbedColorFine);
        }

        private async Task ToggleDefaultNameAvatarAsync(SocketMessage message, string[] args)
        {
            this.plugin.Config.ForceDefaultNameAvatar = !this.plugin.Config.ForceDefaultNameAvatar;
            this.plugin.Config.Save();

            await SendGenericEmbed(message.Channel,
                $"OK! Posting relays with the bot's own name and default avatar is {(this.plugin.Config.ForceDefaultNameAvatar ? "**enabled**.\n\nPlease make sure you have toggled sender names on, or you won't know who said which chat messages!" : "**disabled**: relays show each player's name and portrait.")}",
                "Default Name and Avatar Mode set", EmbedColorFine);

            if (this.plugin.Config.ForceDefaultNameAvatar && this.socketClient.CurrentUser.Username.ToLower().Contains("discord"))
            {
                await SendGenericEmbed(message.Channel, "Bot username cannot contain Discord. Using a fallback value until this is changed.", "ERROR - Bot username cannot contain Discord", EmbedColorError);
            }
        }

        private async Task ToggleSenderAsync(SocketMessage message, string[] args)
        {
            this.plugin.Config.SenderInMessage = !this.plugin.Config.SenderInMessage;
            this.plugin.Config.Save();

            await SendGenericEmbed(message.Channel,
                $"OK! {Constant.PluginName} **{(this.plugin.Config.SenderInMessage ? "will" : "will not")}** include the sender name in all messages (where possible).",
                "Sender In Message Mode set", EmbedColorFine);
        }

        private async Task SetCfPrefixAsync(SocketMessage message, string[] args)
        {
            // Are there parameters?
            if (args.Length < 2)
            {
                await SendGenericEmbed(message.Channel,
                    $"You need to specify a prefix to use, or type \"none\" if you want to remove it.\nCheck the ``{this.plugin.Config.DiscordBotPrefix}help`` command for more information.",
                    "Error", EmbedColorError);

                return;
            }

            if (args[1] == "none")
                args[1] = string.Empty;

            this.plugin.Config.CFPrefixConfig = args[1];

            this.plugin.Config.Save();


            await SendGenericEmbed(message.Channel,
                $"OK! The following prefix was set:\n\n```\n{this.plugin.Config.CFPrefixConfig}```",
                "Prefix set", EmbedColorFine);
        }

        private async Task SetInboundAsync(SocketMessage message, string[] args)
        {
            if (!InboundRules.TryParseTarget(args.Length > 1 ? args[1] : null, out var target, out var error))
            {
                await SendGenericEmbed(message.Channel, error, "Error", EmbedColorError);
                return;
            }

            if (!this.plugin.Config.ChannelConfigs.TryGetValue(message.Channel.Id, out var config))
                config = new DiscordChannelConfig();

            config.InboundChatType = target;
            this.plugin.Config.SetChannel(message.Channel.Id, config);
            this.plugin.Config.Save();

            await SendGenericEmbed(message.Channel,
                $"OK! {InboundRules.Describe(config)}\n\n" +
                (target == null
                    ? "Messages in this channel won't be shown in game."
                    : $"Messages posted here will be shown in your in-game chat. Use ``{this.plugin.Config.DiscordBotPrefix}setinboundperm`` to choose who can relay."),
                "Inbound relay set", EmbedColorFine);
        }

        private async Task SetupAsync(SocketMessage message, string[] args)
        {
            var summary = SanePreset.Apply(this.plugin.Config, message.Channel.Id);
            this.plugin.Config.Save();

            await SendGenericEmbed(message.Channel,
                string.Join("\n\n", summary) + $"\n\nAdjust anything with the other commands (``{this.plugin.Config.DiscordBotPrefix}help``).",
                "Set up with sane defaults", EmbedColorFine);
        }

        private async Task SelfTestHereAsync(SocketMessage message, string[] args)
        {
            var off = args.Length > 1 && args[1].Equals("off", StringComparison.OrdinalIgnoreCase);
            this.plugin.Config.SelfTestChannelId = off ? null : message.Channel.Id;
            this.plugin.Config.Save();

            await SendGenericEmbed(message.Channel,
                off
                    ? "OK! Self-tests will no longer post to Discord."
                    : $"OK! Developer self-tests ({Constant.MainCommand} → Run self-tests, or /dcptest) will post a test message here and check it comes back. Use a private channel.",
                "Self-test channel", EmbedColorFine);
        }

        private async Task SetInboundPermAsync(SocketMessage message, string[] args)
        {
            if (!InboundRules.TryParsePermission(args.Skip(1).ToArray(), out var permission, out var roleId, out var error))
            {
                await SendGenericEmbed(message.Channel, error, "Error", EmbedColorError);
                return;
            }

            if (!this.plugin.Config.ChannelConfigs.TryGetValue(message.Channel.Id, out var config))
                config = new DiscordChannelConfig();

            config.InboundPermission = permission;
            config.InboundRoleId = roleId;
            this.plugin.Config.SetChannel(message.Channel.Id, config);
            this.plugin.Config.Save();

            await SendGenericEmbed(message.Channel, $"OK! {InboundRules.Describe(config)}", "Inbound permission set", EmbedColorFine);
        }

        private async Task ListChannelAsync(SocketMessage message, string[] args)
        {
            if (!this.plugin.Config.ChannelConfigs.TryGetValue(message.Channel.Id, out var config))
            {
                await SendGenericEmbed(message.Channel,
                    $"You didn't set up any channel kinds for this channel yet.\nPlease use the ``{this.plugin.Config.DiscordBotPrefix}setchannel`` command to do this.",
                    "Error", EmbedColorError);
                return;
            }

            var inboundLine = InboundRules.Describe(config);

            if (config.ChatTypes.Count == 0)
            {
                await SendGenericEmbed(message.Channel,
                    $"There are no channel kinds set for this channel right now.\nPlease use the ``{this.plugin.Config.DiscordBotPrefix}setchannel`` command to do this.\n\n{inboundLine}",
                    "Error", EmbedColorFine);
                return;
            }

            await SendGenericEmbed(message.Channel,
                $"OK! This channel has been set to receive the following chat kinds:\n\n{ChatKindArgs.FormatList(ChatKindArgs.Names(config.ChatTypes), "(none)")}\n{inboundLine}",
                "Chat kinds set", EmbedColorFine);
        }

        private async Task SetAvatarAsync(SocketMessage message, string[] args)
        {
            if (args.Length != 3)
            {
                await SendGenericEmbed(message.Channel,
                    $"Format: ``{this.plugin.Config.DiscordBotPrefix}setavatar <kind1,kind2,...|default> <url|none>``",
                    "Error", EmbedColorError);
                return;
            }

            var avatarURL = args[2].Replace("<", "").Replace(">", "").Trim();
            var reset = avatarURL == "none";
            if (!reset && !ChatKindArgs.IsValidAvatarUrl(avatarURL))
            {
                await SendGenericEmbed(message.Channel, "The avatar must be a full `https://` link to an image, or `none`.", "Error", EmbedColorError);
                return;
            }

            if (args[1] == "default")
            {
                this.plugin.Config.DefaultAvatarURL = reset ? Constant.LogoLink : avatarURL;
                this.plugin.Config.Save();
                await SendGenericEmbed(message.Channel,
                    reset ? "OK! The default/fallback avatar has been reset." : $"OK! The default/fallback avatar has been set to ``{avatarURL}``",
                    reset ? "Custom fallback avatar reset" : "Custom fallback avatar set", EmbedColorFine);
                return;
            }

            if (await this.TryParseKindsAsync(message, args, 3, "setavatar <kind1,kind2,...|default> <url|none>") is not { } types)
                return;

            this.plugin.Config.ChangeAvatars(m =>
            {
                foreach (var type in types)
                {
                    if (reset)
                        m.Remove(type);
                    else
                        m[type] = avatarURL;
                }
            });

            this.plugin.Config.Save();

            await SendGenericEmbed(message.Channel,
                $"OK! Custom avatars:\n\n{ChatKindArgs.FormatList(ChatKindArgs.Describe(this.plugin.Config.ChatTypeAvatarURL), "(none)")}",
                "Custom avatar set", EmbedColorFine);
        }

        private async Task UnsetAvatarAsync(SocketMessage message, string[] args)
        {
            if (args.Length != 2)
            {
                await SendGenericEmbed(message.Channel,
                    $"Format: ``{this.plugin.Config.DiscordBotPrefix}unsetavatar <kind1,kind2,...|default>``",
                    "Error", EmbedColorError);
                return;
            }

            if (args[1] == "default")
            {
                this.plugin.Config.DefaultAvatarURL = Constant.LogoLink;
                this.plugin.Config.Save();
                await SendGenericEmbed(message.Channel, "OK! The default/fallback avatar has been reset.", "Custom fallback avatar reset", EmbedColorFine);
                return;
            }

            if (await this.TryParseKindsAsync(message, args, 2, "unsetavatar <kind1,kind2,...|default>") is not { } types)
                return;

            this.plugin.Config.ChangeAvatars(m =>
            {
                foreach (var type in types)
                    m.Remove(type);
            });
            this.plugin.Config.Save();

            await SendGenericEmbed(message.Channel,
                this.plugin.Config.ChatTypeAvatarURL.Count == 0
                    ? "OK! There are no custom avatar overrides set."
                    : $"OK! Custom avatars still set:\n\n{ChatKindArgs.FormatList(ChatKindArgs.Describe(this.plugin.Config.ChatTypeAvatarURL), "(none)")}",
                "Custom avatar unset", EmbedColorFine);
        }

        private async Task HelpAsync(SocketMessage message, string[] args)
        {
            Logger.Verbose("Help time");

            var builder = new EmbedBuilder()
                .WithTitle($"{Constant.PluginName} Help")
                .WithDescription("You can use the following commands to set up the Discord bridge.")
                .WithColor(new Color(EmbedColorFine))
                .AddField($"{this.plugin.Config.DiscordBotPrefix}setup", "**Start here.** One command for sane settings in this channel: all player chat both ways, notifications (retainers, system, loot, duty pops) off, Discord → game defaults to Echo, display options reset. Doesn't touch send-as-me, your customizations or other channels.")
                .AddField($"{this.plugin.Config.DiscordBotPrefix}setchannel", "Select, which kinds of chat should arrive in this channel.\n" +
                                         $"Format: ``{this.plugin.Config.DiscordBotPrefix}setchannel <kind1,kind2,...>``\n\n" +
                                         $"See [this link for a list of all available chat kinds]({Constant.KindListLink}). ``any`` = all regular chat (incl. your own tells), ``all`` = **every** kind (system messages too).")
                .AddField($"{this.plugin.Config.DiscordBotPrefix}unsetchannel", "Works like the previous command, but removes kinds of chat from the list of kinds that are sent to this channel.")
                .AddField($"{this.plugin.Config.DiscordBotPrefix}listchannel", "List all chat kinds that are sent to this channel, and its inbound setting.")
                .AddField($"{this.plugin.Config.DiscordBotPrefix}setinbound <kind|off>", "Show messages posted in this channel in your in-game chat, as the given chat kind. "
                    + "**Replying** to a relayed message goes to that message's channel instead (or, for a tell, to that person).\n"
                    + $"Kinds: {InboundRules.AllowedSlugList}\n"
                    + $"Format: ``{this.plugin.Config.DiscordBotPrefix}setinbound fc``")
                .AddField("!channel message (one-off override)", $"Start a message with a channel to send just that message there, ignoring replies and the default: {ChannelOverride.Examples}. "
                    + "Example: ``!e note to self`` or ``!t Bob Smith@Gilgamesh omw``.")
                .AddField($"{this.plugin.Config.DiscordBotPrefix}setinboundperm <owner|everyone|role @role>", "Choose who can relay messages from this channel into the game. Default: owner only.")
                .AddField($"{this.plugin.Config.DiscordBotPrefix}toggledefaultnameavatar","Enable or disable sending webhook messages using your configured bot's name and the default bot avatar.\n**WARNING:**This should be combined with enabling the `togglesender` function or you will be removing any distinction between different player messages.")
                .AddField($"{this.plugin.Config.DiscordBotPrefix}toggledf", "Enable or disable sending duty finder updates to this channel.")
                .AddField($"{this.plugin.Config.DiscordBotPrefix}ping", "Connection test, anyone can use it: the bot reacts ✅ (running, logged in) or ⏸️ (running, not logged in). Nothing appears in game. No reaction = the plugin isn't running.")
                .AddField($"{this.plugin.Config.DiscordBotPrefix}pause / {this.plugin.Config.DiscordBotPrefix}resume", "Stop all relaying (both ways) until resumed, keeping every setting; lasts across restarts. Same as clicking the plugin's icon in the game's server info bar.")
                .AddField($"{this.plugin.Config.DiscordBotPrefix}togglestatus", "Enable or disable the 🟢 online / 🔴 offline posts in every bridged channel when the plugin starts and stops (game opened/closed, plugin loaded/unloaded). On by default. A crash posts nothing.")
                .AddField($"{this.plugin.Config.DiscordBotPrefix}toggleembed", "Enable or disable sending messages as Webhooks (default) or Embeds (Fallback mode)")
                .AddField($"{this.plugin.Config.DiscordBotPrefix}togglesender", "Enable or disable sending messages with the sender name in the message.")
                .AddField($"{this.plugin.Config.DiscordBotPrefix}setduplicatems", "Set time in milliseconds that the bot will check to see if any past messages were the same. Default is 5000 ms.")
                .AddField($"{this.plugin.Config.DiscordBotPrefix}setprefix", "Set a prefix for chat kinds. "
                    + $"This can be an emoji or a string that will be prepended to every chat message that will arrive with this chat kind. "
                    + $"You can also set it to `none` if you want to remove it.\n" 
                    + $"Format: ``{this.plugin.Config.DiscordBotPrefix}setprefix <kind1,kind2,...> <prefix>``")
                .AddField($"{this.plugin.Config.DiscordBotPrefix}setcfprefix", "Set a prefix for duty finder posts. "
                    + $"You can also set it to `none` if you want to remove it.\n" 
                    + $"Format: ``{this.plugin.Config.DiscordBotPrefix}setcfprefix <prefix>``")
                .AddField($"{this.plugin.Config.DiscordBotPrefix}setchattypename ", "Set custom text for chat kinds. "
                    + $"This can be an emoji or a string that will replace the short name of a chat kind for every chat message that will arrive with this chat kind. "
                    + $"You can also set it to `none` if you want to remove it.\n" 
                    + $"Format: ``{this.plugin.Config.DiscordBotPrefix}setchattypename  <kind1,kind2,...> <custom text>``")
                .AddField($"{this.plugin.Config.DiscordBotPrefix}unsetprefix", "Remove prefix set for a chat kind. \n"
                    + $"Format: ``{this.plugin.Config.DiscordBotPrefix}unsetprefix <kind>``")
                .AddField($"{this.plugin.Config.DiscordBotPrefix}unsetchattypename", "Remove custom name for a chat kind. \n"
                    + $"Format: ``{this.plugin.Config.DiscordBotPrefix}unsetchattypename <kind>``")
                .AddField($"{this.plugin.Config.DiscordBotPrefix}setavatar <kind> <url>", "Set custom fallback avatar for a chat kind. "
                    + "Use ``default`` as the fallback for any unconfigured overrides.\n"
                    + "__NOTE__: Upload the icon to Discord first if you don't have a URL already.\n"
                    + $"Format: ``{this.plugin.Config.DiscordBotPrefix}setavatar <kind> <url>``")
                .AddField($"{this.plugin.Config.DiscordBotPrefix}unsetavatar <kind>", "Unset custom fallback avatar for a chat kind. "
                    + "Use ``default`` to reset the fallback for any unconfigured overrides.\n"
                    + $"Format: ``{this.plugin.Config.DiscordBotPrefix}unsetavatar <kind>``")
                .AddField("Need more help?",
                    $"[Read the command guide]({Constant.HelpLink}), or [open an issue]({Constant.IssuesLink}) if something doesn't work.")
                .WithFooter(footer =>
                {
                    footer
                        .WithText(Constant.PluginName)
                        .WithIconUrl(Constant.LogoLink);
                })
                .WithThumbnailUrl(Constant.LogoLink);
            var embed = builder.Build();

            var m = await message.Channel.SendMessageAsync(
                    null,
                    embed: embed,
                    allowedMentions: RelayMentions)
                .ConfigureAwait(false);
            ;
            Logger.Verbose(m.Id.ToString());
        }
    }
}
