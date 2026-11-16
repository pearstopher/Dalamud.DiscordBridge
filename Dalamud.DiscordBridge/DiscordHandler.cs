using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.DiscordBridge.Model;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using Discord;
using Discord.Webhook;
using Discord.WebSocket;
using Lumina.Text.ReadOnly;
using NetStone;
using NetStone.Model.Parseables.Character;
using NetStone.Search.Character;

namespace Dalamud.DiscordBridge
{
    public partial class DiscordHandler : IDisposable
    {
        private static IPluginLog Logger => Service.Logger;

        private readonly DuplicateFilter duplicateFilter;
        
        private readonly DiscordSocketClient socketClient;
        private readonly SpecialCharsHandler specialChars;

        public bool IsConnected => this.socketClient.ConnectionState == ConnectionState.Connected;
        public ulong UserId => this.socketClient.CurrentUser.Id;

        /// <summary>Lodestone portraits and misses, shared by every handler this session (#47).</summary>
        private static readonly AvatarCache Avatars = new();

        /// <summary>Longest a single Lodestone lookup may hold up the relay queue (#47).</summary>
        internal static readonly TimeSpan LodestoneTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Defines if the bot has connected and verified that it has the correct permissions
        /// </summary>
        public DiscordState State { get; private set; } = DiscordState.None;

        private readonly DiscordBridgePlugin plugin;

        /// <summary>
        /// Chat types selected by "any": all regular chat ("all" is <see cref="AllChatTypes"/>).
        /// </summary>
        internal static readonly XivChatType[] DefaultChatTypes =
        [
            XivChatType.Say,
            XivChatType.Shout,
            XivChatType.Yell,
            XivChatType.Party,
            XivChatType.CrossParty,
            XivChatType.PvPTeam,
            XivChatType.TellIncoming,
            XivChatType.TellOutgoing,
            XivChatType.Alliance,
            XivChatType.FreeCompany,
            XivChatType.Ls1,
            XivChatType.Ls2,
            XivChatType.Ls3,
            XivChatType.Ls4,
            XivChatType.Ls5,
            XivChatType.Ls6,
            XivChatType.Ls7,
            XivChatType.Ls8,
            XivChatType.NoviceNetwork,
            XivChatType.CustomEmote,
            XivChatType.StandardEmote,
            XivChatType.CrossLinkShell1,
            XivChatType.CrossLinkShell2,
            XivChatType.CrossLinkShell3,
            XivChatType.CrossLinkShell4,
            XivChatType.CrossLinkShell5,
            XivChatType.CrossLinkShell6,
            XivChatType.CrossLinkShell7,
            XivChatType.CrossLinkShell8,
            XivChatType.Echo,
            XivChatType.SystemMessage,
        ];

        /// <summary>
        /// Chat types set by "all": every kind the plugin knows (except "none").
        /// </summary>
        internal static XivChatType[] AllChatTypes =>
            XivChatTypeExtensions.TypeInfoDict.Keys.Where(t => t != XivChatType.None).ToArray();

        /// <summary>
        /// Embed color signalling that everything is fine.
        /// </summary>
        private const int EmbedColorFine = 0x478CFF;

        /// <summary>
        /// Nothing the bot or its webhooks post may ping anyone (#41): relayed game text could contain
        /// <c>&lt;@&amp;role&gt;</c> or <c>&lt;@user&gt;</c> typed by any player. Mentions still show as text.
        /// </summary>
        internal static readonly AllowedMentions RelayMentions = AllowedMentions.None;

        /// <summary>
        /// Mentions for a post that starts with the owner's configured prefix: exactly the users/roles/@everyone written
        /// in that prefix (so <c>setcfprefix @here</c> still pings), never anyone else (#41).
        /// </summary>
        internal static AllowedMentions MentionsForPrefix(string? ownerPrefix)
        {
            var allowed = MentionPolicy.ForOwnerPrefix(ownerPrefix);
            if (allowed.None)
                return RelayMentions;

            return new AllowedMentions(allowed.Everyone ? AllowedMentionTypes.Everyone : AllowedMentionTypes.None)
            {
                UserIds = [.. allowed.Users],
                RoleIds = [.. allowed.Roles],
            };
        }

        /// <summary>
        /// Embed color signalling that everything is bad.
        /// </summary>
        private const int EmbedColorError = 0xD10303;

        /// <summary>
        /// The asynchronous message queue that is responsible for sending messages in order.
        /// </summary>
        public readonly DiscordMessageQueue MessageQueue;

        /// <summary>
        /// Discord messages waiting to be relayed into the game (Discord → FFXIV), processed in order.
        /// The processor is replaced by the inbound display/send pipeline; by default it only logs.
        /// </summary>
        public readonly SerialWorkQueue<InboundDiscordMessage> InboundQueue;


        private LodestoneClient? lodestoneClient;

        /// <summary>One webhook client per channel, reused for every message (#40).</summary>
        private readonly ClientCache<DiscordWebhookClient> webhooks = new();

        /// <summary>Channels already warned about this session (#39).</summary>
        private readonly WarnOnce unreachableChannels = new();

        public DiscordHandler(DiscordBridgePlugin plugin)
        {
            this.plugin = plugin;

            this.specialChars = new SpecialCharsHandler();

            this.MessageQueue = new DiscordMessageQueue(this.plugin);
            this.InboundQueue = new SerialWorkQueue<InboundDiscordMessage>("Inbound queue", async inbound =>
            {
                var outcome = await this.plugin.HandleInboundAsync(inbound);
                Logger.Debug("Inbound message {0} from {1} for {2}: {3} {4}", inbound.MessageId, inbound.AuthorName, inbound.TargetType, outcome.Result, outcome.Reason ?? string.Empty);

                if (this.plugin.Config.InboundReactions && InboundRules.ReactionFor(outcome.Result) is { } emoji)
                    await this.ReactAsync(inbound.ChannelId, inbound.MessageId, emoji);

                // Tell the owner why a "send as me" message didn't go out.
                if (outcome.Result is InboundResult.Rejected or InboundResult.Failed && outcome.Reason is { } reason && inbound.IsOwner)
                    await this.ReplyAsync(inbound.ChannelId, inbound.MessageId, $"Not sent to game: {reason}.");
            });

            Logger.Debug("BEFORE DiscordSocketClient");
            this.socketClient = new DiscordSocketClient(new DiscordSocketConfig
            {
                MessageCacheSize = 20, // hold onto the last 20 messages per channel in cache for duplicate checks
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMessages | GatewayIntents.GuildWebhooks | GatewayIntents.MessageContent,
            });
            Logger.Debug("AFTER DiscordSocketClient");
            this.socketClient.Ready += SocketClientOnReady;
            this.socketClient.Connected += () =>
            {
                this.ChangeState(DiscordStateEvent.Connected);
                return Task.CompletedTask;
            };
            this.socketClient.Disconnected += _ =>
            {
                this.ChangeState(DiscordStateEvent.Disconnected);
                return Task.CompletedTask;
            };
            this.socketClient.MessageReceived += SocketClientOnMessageReceived;
            
            this.duplicateFilter = new DuplicateFilter(this.plugin, this.socketClient);
        }

        public async Task Start()
        {

            if (string.IsNullOrEmpty(this.plugin.Config.DiscordToken))
            {
                this.State = DiscordState.TokenInvalid;

                Logger.Error("Token empty, cannot start bot.");
                return;
            }

            try
            {
                await this.socketClient.LoginAsync(TokenType.Bot, this.plugin.Config.DiscordToken);
                await this.socketClient.StartAsync();
            }
            catch (Exception ex)
            {
                // A rejected token shows as such in /dcp; anything else (network) is logged and retried on next Save.
                if (ex is Discord.Net.HttpException { HttpCode: System.Net.HttpStatusCode.Unauthorized })
                {
                    this.ChangeState(DiscordStateEvent.LoginRejected);
                    Logger.Error(ex, "Discord rejected the bot token.");
                }
                else
                {
                    Logger.Error(ex, "Could not log in to Discord.");
                }
            }

            this.MessageQueue.Start();
            this.InboundQueue.Start();

            lodestoneClient = await LodestoneClient.GetClientAsync();

            Logger.Debug("DiscordHandler START!!");
        }

        private void ChangeState(DiscordStateEvent e)
        {
            var before = this.State;
            this.State = DiscordStates.Next(before, e);
            if (this.State != before)
                Logger.Debug("Discord state {0} → {1} ({2}).", before, this.State, e);
        }

        private Task SocketClientOnReady()
        {
            this.ChangeState(DiscordStateEvent.Ready);
            this.specialChars.TryFindEmote(this.socketClient);

            Logger.Verbose("DiscordHandler READY!!");

            // Ready also fires on every gateway reconnect; the plugin-level gate makes this post once per load.
            if (this.plugin.Status.TryBeginOnline() && this.plugin.Config.PostStatusMessages)
                _ = Task.Run(this.AnnounceOnlineAsync);
            
            return Task.CompletedTask;
        }

        public async Task SetOnlinePresence()
        {
            try
            {
                await this.socketClient.SetStatusAsync(UserStatus.Online);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to set online status.");
            }
            
        }

        public async Task SetIdlePresence()
        {
            try
            {
                await this.socketClient.SetStatusAsync(UserStatus.Idle);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to set idle status.");
            }

        }

        /// <summary>Self-test hook: every message the gateway delivers, before any filtering.</summary>
        internal event Action<SocketMessage>? MessageObserved;

        private async Task SocketClientOnMessageReceived(SocketMessage message)
        {
            try
            {
                this.MessageObserved?.Invoke(message);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Self-test message observer failed.");
            }

            if (message.Author.IsBot || message.Author.IsWebhook)
                return;

            try
            {
                if (await TryRouteInboundAsync(message))
                    return;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not route Discord message into the game.");
                return;
            }

            var args = message.Content.Split();

            // if it doesn't start with the bot prefix, ignore it.
            if (!args[0].StartsWith(this.plugin.Config.DiscordBotPrefix))
                return;

            Logger.Verbose("Received command: {0}", args[0]);

            try
            {
                // Connection test (#37): anyone, reaction only, nothing in game. No reaction = plugin not running.
                if (InboundRules.IsPingCommand(args[0], this.plugin.Config.DiscordBotPrefix))
                {
                    await this.AnswerPingAsync(message);
                    return;
                }

                // Every other command comes from the table in DiscordHandler.Commands.cs (#52).
                var name = args[0][this.plugin.Config.DiscordBotPrefix.Length..];
                if (!Commands.TryGetValue(name, out var command))
                    return;
                if (command.OwnerOnly && !await EnsureOwner(message.Author, message.Channel))
                    return;

                await command.Run(this, message, args);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not handle incoming Discord message.");
            }
        }

        private static async Task SendGenericEmbed(IMessageChannel channel, string message, string title, uint color)
        {
            var builder = new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(message)
                .WithColor(new Color(color))
                .WithFooter(footer => {
                    footer
                        .WithText(Constant.PluginName)
                        .WithIconUrl(Constant.LogoLink);
                })
                .WithThumbnailUrl(Constant.LogoLink);
                
            var embed = builder.Build();
            await channel.SendMessageAsync(
                    null,
                    embed: embed,
                    allowedMentions: RelayMentions)
                .ConfigureAwait(false);
        }

        /// <returns>The Discord message ID.</returns>
        private static async Task<ulong> SendPrettyEmbed(IMessageChannel channel, string message, string title, string iconurl, uint color)
        {
            var builder = new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(message)
                .WithColor(new Color(color))
                .WithFooter(footer => {
                    footer
                        .WithText(Constant.PluginName)
                        .WithIconUrl(Constant.LogoLink);
                })
                .WithThumbnailUrl(iconurl);

            var embed = builder.Build();
            var sent = await channel.SendMessageAsync(
                    null,
                    embed: embed,
                    allowedMentions: RelayMentions)
                .ConfigureAwait(false);
            return sent.Id;
        }

        /// <summary>
        /// Parse the chat-kind list in <c>args[1]</c> for a command that needs at least <paramref name="minArgs"/> words.
        /// Replies with the usage or the parse error and returns null on failure (#42).
        /// </summary>
        private async Task<IReadOnlyList<XivChatType>?> TryParseKindsAsync(SocketMessage message, string[] args, int minArgs, string usage)
        {
            if (args.Length < minArgs)
            {
                await SendGenericEmbed(message.Channel,
                    $"Format: ``{this.plugin.Config.DiscordBotPrefix}{usage}``\nCheck the ``{this.plugin.Config.DiscordBotPrefix}help`` command for more information.",
                    "Error", EmbedColorError);
                return null;
            }

            if (!ChatKindArgs.TryParse(args[1], DefaultChatTypes, AllChatTypes, out var types, out var error))
            {
                await SendGenericEmbed(message.Channel, error, "Error", EmbedColorError);
                return null;
            }

            return types;
        }

        /// <summary>
        /// Check if the sender of this message is set as the owner of this plugin, and send an error message to the specified channel if not null.
        /// </summary>
        /// <param name="user">User in question.</param>
        /// <param name="errorMessageChannel">Channel for error message.</param>
        /// <returns>True if the user is the owner of this plugin.</returns>
        private async Task<bool> EnsureOwner(IUser user, IMessageChannel? errorMessageChannel = null)
        {
            Logger.Verbose("EnsureOwner: " + user.Username + "#" + user.Discriminator);
            if (IsOwner(user))
                return true;

            if (errorMessageChannel == null)
                return false;

            await SendGenericEmbed(errorMessageChannel, $"You are not allowed to run commands for this bot.\n\nIf this is your bot, please use the \"{Constant.MainCommand}\" command in-game to enter your username or Discord User ID number.", "Error", EmbedColorError);

            return false;
        }

        private bool IsOwner(IUser user)
            => InboundRules.IsOwner(user.Username, user.Discriminator, user.Id, this.plugin.Config.DiscordOwnerName);

        /// <summary>
        /// If this message is in a channel with inbound relay enabled (and isn't a command), queue it for the game.
        /// </summary>
        /// <returns>True if the message was handled here (queued, or rejected by permissions) and isn't a command.</returns>
        private async Task<bool> TryRouteInboundAsync(SocketMessage message)
        {
            if (!this.plugin.Config.ChannelConfigs.TryGetValue(message.Channel.Id, out var config))
                return false;

            if (!InboundRules.ShouldRelay(message.Author.IsBot, message.Author.IsWebhook, message.Content,
                                          this.plugin.Config.DiscordBotPrefix, config.InboundChatType))
                return false;

            var isOwner = IsOwner(message.Author);
            var roleIds = (message.Author as SocketGuildUser)?.Roles.Select(r => r.Id) ?? [];

            if (!InboundRules.IsAllowedSender(config.InboundPermission, isOwner, roleIds, config.InboundRoleId))
            {
                Logger.Debug("Inbound: {0} isn't allowed to relay from channel {1}.", message.Author.Username, message.Channel.Id);
                return true;
            }

            // Paused (#54): nothing goes into the game; 💤 says why.
            if (this.plugin.Config.Paused)
            {
                if (this.plugin.Config.InboundReactions)
                    await this.ReactAsync(message.Channel.Id, message.Id, PauseStatus.PausedReaction);
                return true;
            }

            var draft = Snapshot(message, config.InboundChatType!.Value, isOwner);

            // "!fc hello", "!e note", "!t Bob Smith@World hi": a one-message override that beats replies and the default.
            var channelOverride = ChannelOverride.Parse(draft.Content);
            if (channelOverride.Kind == ChannelOverride.Kind.Invalid)
            {
                Logger.Debug("Channel override rejected: {0}", channelOverride.Error ?? "");
                if (this.plugin.Config.InboundReactions)
                    await this.ReactAsync(message.Channel.Id, message.Id, "❌");
                await this.ReplyAsync(message.Channel.Id, message.Id, $"Not sent to game: {channelOverride.Error}.");
                return true;
            }

            if (channelOverride.Kind == ChannelOverride.Kind.Override)
            {
                var clean = ChannelOverride.Parse(draft.CleanContent);
                var overridden = draft with
                {
                    TargetType = channelOverride.Target.Type,
                    TellTarget = channelOverride.Target.TellPartner,
                    Content = channelOverride.Text,
                    CleanContent = clean.Kind == ChannelOverride.Kind.Override ? clean.Text : channelOverride.Text,
                    ReplyToAuthor = null,
                };
                Logger.Debug("Channel override: {0} → {1}", draft.MessageId, channelOverride.Target);
                this.InboundQueue.Enqueue(overridden);
                return true;
            }

            // The gateway's copy of the replied-to message can lack its content (or be missing). If we don't have the
            // relay in memory either, fetch it so routing can parse the channel / tell partner from it.
            if (draft.ReplyToMessageId is { } toFetch && this.plugin.RelayIndex.Find(toFetch) == null && string.IsNullOrEmpty(draft.ReplyToContent))
            {
                try
                {
                    if (await message.Channel.GetMessageAsync(toFetch) is { } fetched)
                    {
                        var fields = ReplyFieldsOf(fetched, toFetch);
                        draft = draft with
                        {
                            ReplyToAuthor = fields.Author ?? draft.ReplyToAuthor,
                            ReplyToContent = fields.Content,
                            ReplyToIsRelay = fields.IsRelay,
                        };
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Could not fetch replied-to message {0}.", toFetch);
                }
            }

            // A reply to a relayed message goes back where that message came from (channel, or tell partner).
            var decision = ReplyRouting.Decide(draft, this.plugin.RelayIndex, this.plugin.Config.CustomSlugsConfigs);
            if (draft.ReplyToMessageId is { } replyId)
            {
                Logger.Debug("Reply routing: reply to {0} (relay={1}, indexed={2}, author=\"{3}\", content=\"{4}\") → {5} {6}",
                    replyId, draft.ReplyToIsRelay, this.plugin.RelayIndex.Find(replyId)?.ToString() ?? "no",
                    draft.ReplyToAuthor ?? "", draft.ReplyToContent ?? "(none)",
                    decision.Kind, decision.Target?.ToString() ?? decision.Reason ?? $"default ({draft.TargetType})");
            }

            // Replies to relays from GM channels, retainer sales, system messages… go nowhere (never the default).
            if (decision.Kind == ReplyKind.Blocked)
            {
                if (this.plugin.Config.InboundReactions)
                    await this.ReactAsync(message.Channel.Id, message.Id, "❌");
                await this.ReplyAsync(message.Channel.Id, message.Id,
                    $"Not sent to game: {decision.Reason}. Start your message with a channel ({ChannelOverride.Examples}) to send it somewhere else.");
                return true;
            }

            var inbound = decision is { Kind: ReplyKind.Route, Target: { } r }
                ? draft with { TargetType = r.Type, TellTarget = r.TellPartner, ReplyToAuthor = null }
                : draft;

            this.InboundQueue.Enqueue(inbound);
            return true;
        }

        private void Remember(ulong discordMessageId, ReplyTarget? target)
        {
            if (target is { } t)
                this.plugin.RelayIndex.Record(discordMessageId, t);
        }

        /// <summary>The reply-related fields of an inbound message, from the message it replies to.</summary>
        internal readonly record struct ReplyFields(ulong? MessageId, string? Author, string? Content, bool IsRelay);

        /// <summary>
        /// Extract reply fields from the replied-to message (shared by real replies and the reply-routing self-test).
        /// For relays, the webhook name ("Name＠World") or embed title is the in-game sender.
        /// </summary>
        internal static ReplyFields ReplyFieldsOf(IMessage? referenced, ulong? referenceId)
        {
            if (referenced == null)
                return new ReplyFields(referenceId, null, null, false);

            var embed = referenced.Embeds.FirstOrDefault();
            var isRelay = referenced.Author.IsWebhook || referenced.Author.IsBot;
            var hasContent = !string.IsNullOrEmpty(referenced.Content);
            var author = isRelay
                ? (!hasContent && embed != null ? embed.Title : referenced.Author.Username)
                : DisplayNameOf(referenced.Author);

            return new ReplyFields(referenceId ?? referenced.Id, author, hasContent ? referenced.Content : embed?.Description, isRelay);
        }

        private static InboundDiscordMessage Snapshot(SocketMessage message, XivChatType target, bool isOwner)
        {
            var userMessage = message as SocketUserMessage;
            var reference = userMessage?.Reference;
            var reply = ReplyFieldsOf(userMessage?.ReferencedMessage,
                                      reference != null && reference.MessageId.IsSpecified ? reference.MessageId.Value : null);

            return new InboundDiscordMessage
            {
                ChannelId = message.Channel.Id,
                MessageId = message.Id,
                AuthorId = message.Author.Id,
                AuthorName = DisplayNameOf(message.Author),
                IsOwner = isOwner,
                TargetType = target,
                Content = message.Content ?? string.Empty,
                CleanContent = message.CleanContent ?? string.Empty,
                Attachments = message.Attachments
                    .Select(a => new InboundAttachment(a.Filename, a.ContentType?.StartsWith("image/") ?? false))
                    .ToList(),
                Stickers = message.Stickers.Select(s => s.Name).ToList(),
                ReplyToAuthor = reply.Author,
                ReplyToMessageId = reply.MessageId,
                ReplyToContent = reply.Content,
                ReplyToIsRelay = reply.IsRelay,
            };
        }

        internal async Task AnswerPingAsync(SocketMessage message)
        {
            var loggedIn = await Service.Framework.RunOnFrameworkThread(() => Service.State.IsLoggedIn);
            var paused = this.plugin.Config.Paused;
            try
            {
                await message.AddReactionAsync(new Emoji(PauseStatus.PingReaction(paused, loggedIn)));
            }
            catch (Exception ex)
            {
                // Usually a missing Add Reactions permission: answer in text so the test still works.
                Logger.Debug(ex, "Could not react to ping; replying instead.");
                await message.Channel.SendMessageAsync(PauseStatus.PingText(paused, loggedIn, this.plugin.Config.DiscordBotPrefix),
                    messageReference: new MessageReference(message.Id),
                    allowedMentions: RelayMentions);
            }
        }

        /// <summary>Tell the bridged channels that relaying was paused or resumed (#54), if status posts are on.</summary>
        internal async Task AnnouncePauseChangeAsync(bool paused, string source)
        {
            if (!this.plugin.Config.PostStatusMessages || this.State != DiscordState.Ready)
                return;

            try
            {
                await this.AnnounceStatusAsync(PauseStatus.ChangeTitle(paused),
                    PauseStatus.ChangeText(paused, source, this.plugin.Config.DiscordBotPrefix), (uint)(paused ? EmbedColorError : EmbedColorFine));
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Could not post the pause change.");
            }
        }

        private static string PluginVersion => Service.Interface.Manifest.AssemblyVersion?.ToString() ?? "?";

        private async Task AnnounceOnlineAsync()
        {
            try
            {
                var character = await Service.Framework.RunOnFrameworkThread(() =>
                    Service.State.IsLoggedIn && !string.IsNullOrEmpty(Service.PlayerState.CharacterName)
                        ? $"{Service.PlayerState.CharacterName}＠{Service.PlayerState.HomeWorld.Value.Name}"
                        : null);
                await this.AnnounceStatusAsync(StatusAnnouncer.OnlineTitle,
                    StatusAnnouncer.OnlineText(PluginVersion, character, this.plugin.Config.Paused ? this.plugin.Config.DiscordBotPrefix : null), EmbedColorFine);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Could not post the online status message.");
            }
        }

        /// <summary>
        /// Posts the offline message on plugin unload (#34). Blocks the calling thread for at most
        /// <paramref name="timeout"/>, so unloading never hangs; failures are logged and ignored.
        /// </summary>
        internal void AnnounceOffline(TimeSpan timeout)
        {
            if (this.State != DiscordState.Ready || !this.plugin.Status.TryBeginOffline() || !this.plugin.Config.PostStatusMessages)
                return;

            try
            {
                var text = StatusAnnouncer.OfflineText(PluginVersion);
                // Task.Run: never resume on the framework thread we're blocking.
                if (!Task.Run(() => this.AnnounceStatusAsync(StatusAnnouncer.OfflineTitle, text, EmbedColorError)).Wait(timeout))
                    Logger.Warning("Offline status message timed out after {0} ms.", timeout.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Could not post the offline status message.");
            }
        }

        private async Task AnnounceStatusAsync(string title, string text, uint color)
        {
            var sends = StatusAnnouncer.TargetChannels(this.plugin.Config.ChannelConfigs).Select(async channelId =>
            {
                try
                {
                    var channel = this.socketClient.GetChannel(channelId) as IMessageChannel
                                  ?? await this.socketClient.GetChannelAsync(channelId) as IMessageChannel;
                    if (channel != null)
                        await SendGenericEmbed(channel, text, title, color);
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex, "Status message not posted to channel {0}.", channelId);
                }
            });
            await Task.WhenAll(sends);
        }

        /// <summary>
        /// Add a reaction to a Discord message (inbound status feedback). Failures are logged and ignored.
        /// </summary>
        internal async Task ReactAsync(ulong channelId, ulong messageId, string emoji)
        {
            try
            {
                var channel = this.socketClient.GetChannel(channelId) as IMessageChannel
                              ?? await this.socketClient.GetChannelAsync(channelId) as IMessageChannel;
                if (channel == null)
                    return;

                // The message was just received, so it's normally in the gateway's cache: react without fetching it (#50).
                var message = (channel as ISocketMessageChannel)?.GetCachedMessage(messageId)
                              ?? await channel.GetMessageAsync(messageId);
                if (message != null)
                    await message.AddReactionAsync(new Emoji(emoji));
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Could not react to Discord message {0}.", messageId);
            }
        }

        /// <summary>Reply to a Discord message (inbound status feedback). Failures are logged and ignored.</summary>
        internal async Task ReplyAsync(ulong channelId, ulong messageId, string text)
        {
            try
            {
                var channel = this.socketClient.GetChannel(channelId) as IMessageChannel
                              ?? await this.socketClient.GetChannelAsync(channelId) as IMessageChannel;
                if (channel == null)
                    return;

                await channel.SendMessageAsync(text,
                    messageReference: new MessageReference(messageId),
                    allowedMentions: RelayMentions);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Could not reply to Discord message {0}.", messageId);
            }
        }

        /// <summary>
        /// A character's Lodestone portrait: cached, else searched with a <see cref="LodestoneTimeout"/> limit. Misses and
        /// failures are remembered for a while (#47), so the relay queue never waits on the same slow search twice.
        /// </summary>
        /// <returns>The portrait URL, or null (use the fallback avatar).</returns>
        internal async Task<string?> LookupAvatarAsync(string name, string world)
        {
            var key = $"{name}＠{world}";
            if (Avatars.TryGet(key, DateTimeOffset.UtcNow, out var known))
                return known;

            if (this.lodestoneClient == null)
            {
                Logger.Debug("Lodestone isn't ready yet; no portrait for {0}.", key);
                return null;
            }

            try
            {
                var url = await this.FindLodestoneAvatarAsync(name, world).WaitAsync(LodestoneTimeout);
                if (url != null)
                    Avatars.AddHit(key, url);
                else
                    Avatars.AddMiss(key, DateTimeOffset.UtcNow, AvatarCache.NotFoundTtl);
                return url;
            }
            catch (TimeoutException)
            {
                Logger.Debug("Lodestone lookup for {0} took over {1} s; using the fallback avatar.", key, LodestoneTimeout.TotalSeconds);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Lodestone lookup for {0} failed; using the fallback avatar.", key);
            }

            Avatars.AddMiss(key, DateTimeOffset.UtcNow, AvatarCache.FailedTtl);
            return null;
        }

        /// <summary>Whether the Lodestone client has been created (after <see cref="Start"/>).</summary>
        internal bool LodestoneReady => this.lodestoneClient != null;

        /// <summary>Self-tests: find a character's Lodestone portrait URL, or null if not found.</summary>
        internal async Task<string?> FindLodestoneAvatarAsync(string name, string world)
        {
            if (this.lodestoneClient is not { } client)
                return null;

            var page = await client.SearchCharacter(new CharacterSearchQuery { CharacterName = name, World = world });
            var entry = page?.Results.FirstOrDefault(r => r.Name == name);
            if (entry == null)
                return null;

            var character = await entry.GetCharacter();
            return character?.Avatar?.ToString();
        }

        /// <summary>Self-tests: the bot account's user ID, or 0 before login.</summary>
        internal ulong BotUserId => this.socketClient.CurrentUser?.Id ?? 0;

        /// <summary>Server nickname, else global display name, else username.</summary>
        private static string DisplayNameOf(IUser user)
            => (user as IGuildUser)?.DisplayName ?? user.GlobalName ?? user.Username;

        public async Task SendItemSaleEvent(ReadOnlySeString name, string iconurl, uint itemId, string message, XivChatType chatType)
        {
            var applicableChannels =
                this.plugin.Config.ChannelConfigs.Where(x => ChatTypeGroups.Receives(x.Value, chatType)).ToList(); // snapshot before awaiting (#46)

            if (!applicableChannels.Any())
                return;

            message = this.specialChars.TransformToUnicode(message);

            
            Logger.Information($"Retainer sold itemID: {itemId} with iconurl: {iconurl}");

            this.plugin.Config.PrefixConfigs.TryGetValue(chatType, out var prefix);

            foreach (var channelConfig in applicableChannels)
            {
                var (channel, route, lookup) = await ResolveDeliveryWithLookupAsync(channelConfig.Key, forceEmbed: false);

                if (channel == null)
                {
                    this.HandleUnreachableChannel(channelConfig.Key, lookup);
                    continue;
                }

                switch (route)
                {
                    case DeliveryRoute.DirectMessage:
                    case DeliveryRoute.Embed:
                        await SendPrettyEmbed(channel, message, $"Retainer sold {name}", iconurl, EmbedColorFine);
                        break;
                    case DeliveryRoute.MissingWebhookPermission:
                        Logger.Debug("FALLBACKMODE - Unable to create WebHook - No permission");
                        await SendPrettyEmbed(channel, $"FALLBACKMODE\n\nMissing ManageWebHooks permission.\n\n{message}", $"Retainer sold {name}", iconurl, EmbedColorError);
                        break;
                    case DeliveryRoute.Webhook:
                        var saleSent = await SendViaWebhookAsync((ITextChannel)channel, client => client.SendMessageAsync($"{prefix} {message}",
                            username: $"Retainer sold {name}", avatarUrl: iconurl, allowedMentions: MentionsForPrefix(prefix)));
                        if (saleSent == null)
                        {
                            Logger.Debug("FALLBACKMODE - Unable to create WebHook");
                            await SendPrettyEmbed(channel, $"FALLBACKMODE\nUnable to create WebHook\n\n{message}", $"Retainer sold {name}", iconurl, EmbedColorError);
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// How a relayed message should be delivered to a configured channel.
        /// </summary>
        internal enum DeliveryRoute
        {
            /// <summary>A DM with the bot. Webhooks don't exist there, so post an embed.</summary>
            DirectMessage,

            /// <summary>Post an embed (embed fallback mode, or a channel type without webhooks).</summary>
            Embed,

            /// <summary>Guild text channel, but the bot can't manage webhooks. Post an error embed.</summary>
            MissingWebhookPermission,

            /// <summary>Guild text channel with webhook permission. Post through the channel webhook.</summary>
            Webhook,
        }

        /// <summary>
        /// Pure routing decision, separated out for unit tests.
        /// </summary>
        internal static DeliveryRoute ChooseRoute(bool isDirectMessage, bool isGuildTextChannel, bool forceEmbed, bool hasManageWebhooks)
        {
            if (isDirectMessage)
                return DeliveryRoute.DirectMessage;
            if (forceEmbed || !isGuildTextChannel)
                return DeliveryRoute.Embed;
            return hasManageWebhooks ? DeliveryRoute.Webhook : DeliveryRoute.MissingWebhookPermission;
        }

        /// <summary>
        /// Find a configured channel (from cache, or over REST since DM channels usually aren't cached after a restart)
        /// and decide how to deliver to it. Checks for DMs before any guild-only API is touched.
        /// </summary>
        /// <returns>The channel, or null if it no longer exists or can't be accessed, and the route to use.</returns>
        internal async Task<(IMessageChannel? Channel, DeliveryRoute Route)> ResolveDeliveryAsync(ulong channelId, bool forceEmbed)
        {
            var (channel, route, _) = await this.ResolveDeliveryWithLookupAsync(channelId, forceEmbed);
            return (channel, route);
        }

        /// <summary>Like <see cref="ResolveDeliveryAsync"/>, plus whether a missing channel is definitely gone (#39).</summary>
        internal async Task<(IMessageChannel? Channel, DeliveryRoute Route, ChannelLookupResult Lookup)> ResolveDeliveryWithLookupAsync(ulong channelId, bool forceEmbed)
        {
            IMessageChannel? channel = this.socketClient.GetChannel(channelId) as IMessageChannel;
            var threw = false;
            int? errorCode = null;
            if (channel == null)
            {
                try
                {
                    channel = await this.socketClient.GetChannelAsync(channelId) as IMessageChannel;
                }
                catch (Exception ex)
                {
                    threw = true;
                    errorCode = (int?)(ex as Discord.Net.HttpException)?.DiscordCode;
                    Logger.Debug(ex, "Could not fetch channel {0}.", channelId);
                }
            }

            var lookup = ChannelLookup.Classify(channel != null, threw, errorCode);
            if (channel == null)
                return (null, DeliveryRoute.Embed, lookup);

            this.unreachableChannels.Reset(channelId);

            var isDirectMessage = channel is IDMChannel;
            var hasManageWebhooks = false;

            if (!isDirectMessage && !forceEmbed && channel is ITextChannel guildText)
            {
                var self = await guildText.Guild.GetUserAsync(this.socketClient.CurrentUser.Id);
                hasManageWebhooks = self?.GetPermissions(guildText).Has(ChannelPermission.ManageWebhooks) ?? false;
            }

            return (channel, ChooseRoute(isDirectMessage, channel is ITextChannel, forceEmbed, hasManageWebhooks), lookup);
        }

        /// <summary>
        /// A configured channel couldn't be reached. Forget it only if Discord says it was deleted (#39); otherwise keep
        /// the config and warn once per session, so an outage never wipes anyone's setup.
        /// </summary>
        private void HandleUnreachableChannel(ulong channelId, ChannelLookupResult lookup)
        {
            if (ChannelLookup.ShouldForget(lookup))
            {
                if (this.plugin.Config.RemoveChannel(channelId))
                {
                    Logger.Information("Removed channel {0}'s settings: Discord says the channel no longer exists.", channelId);
                    this.plugin.Config.Save();
                }

                return;
            }

            if (this.unreachableChannels.ShouldWarn(channelId))
                Logger.Warning("Couldn't reach Discord channel {0}; keeping its settings and trying again with the next message.", channelId);
        }

        /// <param name="recipient">For outgoing tells: who the tell was to ("Name＠World"), shown in the message.</param>
        public async Task SendChatEvent(string message, string senderName, string senderWorld, XivChatType chatType, string avatarUrl = "", string? recipient = null)
        {
            // Where a Discord reply to this relay should go (tells: back to the other person). IPC posts aren't replyable.
            ReplyTarget? relayTarget = chatType == XivChatTypeExtensions.IpcChatType
                ? null
                : ReplyRouting.ForRelay(chatType, chatType switch
                {
                    XivChatType.TellOutgoing => recipient,
                    XivChatType.TellIncoming => string.IsNullOrEmpty(senderWorld) ? senderName : $"{senderName}＠{senderWorld}",
                    _ => null,
                });

            // set fields for true chat messages or custom via ipc
            if (chatType != XivChatTypeExtensions.IpcChatType)
            {
                // Special case for outgoing tells, these should be sent under Incoming tells
                if (chatType == XivChatType.TellOutgoing) {
                    chatType = XivChatType.TellIncoming;
                }
            }
            else
            {
                senderWorld = "";
            }

            // default avatar url to logo link if empty
            if (string.IsNullOrEmpty(avatarUrl))
            {
                
                if (!plugin.Config.ChatTypeAvatarURL.TryGetValue(chatType, out avatarUrl))
                {
                    avatarUrl = plugin.Config.DefaultAvatarURL;
                }
                
            }

            var applicableChannels =
                this.plugin.Config.ChannelConfigs.Where(x => ChatTypeGroups.Receives(x.Value, chatType)).ToList(); // snapshot before awaiting (#46)

            if (!applicableChannels.Any())
                return;

            message = this.specialChars.TransformToUnicode(message);

            try
            {
                switch (chatType)
                {
                    case XivChatType.Echo:
                        break;
                    case (XivChatType)61: // npc talk
                        break;
                    case (XivChatType)68: // npc announce
                        break;
                    default:
                        // don't even bother searching if it's gonna be invalid
                        bool doSearch = true;
                        
                        if (string.IsNullOrEmpty(senderName))
                        {
                            Logger.Debug($"Sender Name was null or empty");
                            senderName = $"FFXIV Bridge Worker {plugin.cachedPlayerName ?? "Unknown LocalPlayer"}";
                            senderWorld = "";
                            doSearch = false;
                        }
                        if (string.IsNullOrEmpty(senderWorld))
                        {
                            Logger.Debug($"Sender World was null or empty: {senderWorld}");
                            doSearch = false;
                        }
                        
                        // special cases for things that aren't coming from FFXIV directly.
                        if (senderName == "Sonar")
                        {
                            Logger.Debug($"Sender Name was {senderName}");
                            doSearch = false;
                        }
                        else if (!senderName.Contains(' '))
                        {
                            Logger.Debug($"Sender Name invalid: {senderName}");
                            doSearch = false;
                        }


                        if (doSearch)
                        {
                            if (await this.LookupAvatarAsync(senderName, senderWorld) is { } portrait)
                                avatarUrl = portrait;
                        }
                        
                        break;
                }                    
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(senderName))
                {
                    Logger.Error($"senderName was null or empty. How did we get this far?");
                    senderName = "Bridge Error - sendername";
                }
                else
                {
                    Logger.Error(ex, $"Cannot fetch XIVAPI character search for {senderName} on {senderWorld}");
                }
            }

            var displayName = senderName + (string.IsNullOrEmpty(senderWorld) || string.IsNullOrEmpty(senderName)
                ? ""
                : $"＠{senderWorld}");

            this.plugin.Config.PrefixConfigs.TryGetValue(chatType, out var prefix);

            bool senderInMessage = this.plugin.Config.SenderInMessage;

            var chatTypeText = this.plugin.Config.CustomSlugsConfigs.TryGetValue(chatType, out var x) ? x : chatType.GetSlug();
            

            foreach (var channelConfig in applicableChannels)
            {
                var (channel, route, lookup) = await ResolveDeliveryWithLookupAsync(channelConfig.Key, this.plugin.Config.ForceEmbedFallbackMode);

                if (channel == null)
                {
                    this.HandleUnreachableChannel(channelConfig.Key, lookup);
                    continue;
                }

                var body = ChatContentFormat.Body(message, senderInMessage, displayName, recipient);
                var messageContent = ChatContentFormat.Content(prefix, chatTypeText, chatType == XivChatTypeExtensions.IpcChatType, body);


                if (route == DeliveryRoute.DirectMessage)
                {
                    Remember(await SendPrettyEmbed(channel, messageContent, displayName, avatarUrl, EmbedColorFine), relayTarget);
                    Logger.Debug("SendChatEvent sent to DMs.");
                }
                else if (route == DeliveryRoute.Embed)
                {
                    Remember(await SendPrettyEmbed(channel, $"{messageContent}", $"{displayName}", avatarUrl, EmbedColorFine), relayTarget);
                }
                else if (route == DeliveryRoute.MissingWebhookPermission)
                {
                    Logger.Debug("FALLBACKMODE - Unable to create WebHook - No Permission");
                    await SendPrettyEmbed(channel, $"FALLBACKMODE\n\nMissing ManageWebHooks permission.\n\n{messageContent}", $"{displayName}", avatarUrl, EmbedColorError);
                }
                else
                {
                    // Compare against the body actually posted (it includes the sender/recipient when shown).
                    // Checked before touching the webhook, so a duplicate costs no Discord call (#40).
                    if (duplicateFilter.CheckAlreadySent(channel, slug: chatTypeText, displayName, chatText: body))
                    {
                        continue;
                    }

                    var webhookName = displayName;
                    var webhookAvatar = avatarUrl;
                    if (this.plugin.Config.ForceDefaultNameAvatar)
                    {
                        webhookName = this.socketClient.CurrentUser.Username.ToLower().Contains("discord") ? Constant.WebhookName : this.socketClient.CurrentUser.Username;
                        webhookAvatar = plugin.Config.DefaultAvatarURL;
                    }

                    var relayedId = await SendViaWebhookAsync((ITextChannel)channel, client => client.SendMessageAsync(
                        messageContent, username: webhookName, avatarUrl: webhookAvatar,
                        allowedMentions: MentionsForPrefix(prefix)));
                    if (relayedId is { } sentId)
                    {
                        Remember(sentId, relayTarget);
                        Logger.Debug("SendChatEvent sent to WebHook.");
                    }
                    else
                    {
                        Logger.Debug("FALLBACKMODE - Unable to create WebHook - Unknown failure");
                        await SendPrettyEmbed(channel, $"FALLBACKMODE\n\nUnable to create WebHook\n\n{messageContent}", $"{displayName}", avatarUrl, EmbedColorError);
                    }
                }
            }
        }

        public async Task SendContentFinderEvent(QueuedContentFinderEvent cfEvent)
        {
            var applicableChannels =
                this.plugin.Config.ChannelConfigs.Where(x => x.Value.IsContentFinder).ToList(); // snapshot before awaiting (#46)

            if (!applicableChannels.Any())
                return;

            var iconFolder = cfEvent.ContentFinderCondition.Image / 1000 * 1000;
            var dutyName = cfEvent.ContentFinderCondition.Name.ExtractText();
            var playerName = plugin.cachedPlayerName ?? "Unknown LocalPlayer";

            // A fresh builder per channel, so fallback descriptions don't leak into later channels.
            EmbedBuilder NewEmbed() => new EmbedBuilder()
                .WithCurrentTimestamp()
                .WithColor(0x297c00)
                .WithTitle("Duty is ready: " + dutyName)
                .WithImageUrl("https://xivapi.com" + $"/i/{iconFolder}/{cfEvent.ContentFinderCondition.Image}.png")
                .WithFooter(footer =>
                {
                    footer
                        .WithText("For: " + playerName)
                        .WithIconUrl(Constant.LogoLink);
                });

            var bridgeAuthor = new EmbedAuthorBuilder { Name = Constant.PluginName, IconUrl = Constant.LogoLink };
            var prefix = this.plugin.Config.CFPrefixConfig ?? "";

            foreach (var channelConfig in applicableChannels)
            {
                var (channel, route, lookup) = await ResolveDeliveryWithLookupAsync(channelConfig.Key, forceEmbed: false);

                if (channel == null)
                {
                    this.HandleUnreachableChannel(channelConfig.Key, lookup);
                    continue;
                }

                switch (route)
                {
                    case DeliveryRoute.DirectMessage:
                    case DeliveryRoute.Embed:
                        await channel.SendMessageAsync($"{prefix}", embed: NewEmbed().WithAuthor(bridgeAuthor).Build(), allowedMentions: MentionsForPrefix(prefix));
                        break;
                    case DeliveryRoute.MissingWebhookPermission:
                        Logger.Debug("FALLBACKMODE - Unable to create WebHook - No Permission");
                        await channel.SendMessageAsync($"{prefix}", embed: NewEmbed()
                            .WithAuthor(bridgeAuthor)
                            .WithDescription("FALLBACKMODE - Unable to create WebHook - Missing ManageWebHook permission.")
                            .Build(), allowedMentions: MentionsForPrefix(prefix));
                        break;
                    case DeliveryRoute.Webhook:
                        var cfSent = await SendViaWebhookAsync((ITextChannel)channel, client => client.SendMessageAsync($"{prefix}", embeds: [NewEmbed().Build()],
                            username: Constant.WebhookName, avatarUrl: Constant.LogoLink, allowedMentions: MentionsForPrefix(prefix)));
                        if (cfSent == null)
                        {
                            Logger.Debug("FALLBACKMODE - Unable to create WebHook - Unknown error");
                            await channel.SendMessageAsync($"{prefix}", embed: NewEmbed()
                                .WithAuthor(bridgeAuthor)
                                .WithDescription("FALLBACKMODE - Unable to create WebHook - Unknown failure")
                                .Build(), allowedMentions: MentionsForPrefix(prefix));
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// The channel's webhook client: cached (#40), else the stored webhook, else a new one. Null if none can be made.
        /// </summary>
        /// <param name="channel">The guild text channel to get the webhook for</param>
        internal async Task<DiscordWebhookClient?> GetOrCreateWebhookClient(ITextChannel channel)
        {
            ArgumentNullException.ThrowIfNull(channel);

            if (this.webhooks.TryGet(channel.Id, out var cached) && cached != null)
                return cached;

            if (!this.plugin.Config.ChannelConfigs.TryGetValue(channel.Id, out var channelConfig))
                throw new ArgumentException("No configuration for channel.", nameof(channel));

            IWebhook hook;
            try
            {
                hook = (channelConfig.WebhookId != 0 ? await channel.GetWebhookAsync(channelConfig.WebhookId) : null)
                       ?? await channel.CreateWebhookAsync("FFXIV Bridge Worker");
            }
            catch (Discord.Net.HttpException e)
            {
                Logger.Error(e, "Unable to get or create a webhook for channel {0}.", channel.Id);
                return null;
            }

            if (WebhookRules.ShouldSaveId(channelConfig.WebhookId, hook.Id))
            {
                channelConfig.WebhookId = hook.Id;
                this.plugin.Config.Save();
            }

            Logger.Verbose("Webhook for {0} OK!! {1}", channel.Id, hook.Id);

            return this.webhooks.Add(channel.Id, new DiscordWebhookClient(hook));
        }

        /// <summary>
        /// Send through the channel's webhook. If Discord says the webhook was deleted, make a new one and retry once.
        /// </summary>
        /// <returns>The sent message's ID, or null if no webhook could be used (callers fall back to an embed).</returns>
        private async Task<ulong?> SendViaWebhookAsync(ITextChannel channel, Func<DiscordWebhookClient, Task<ulong>> send)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (await GetOrCreateWebhookClient(channel) is not { } client)
                    return null;

                try
                {
                    return await send(client);
                }
                catch (Discord.Net.HttpException ex) when (attempt == 0 && WebhookRules.IsGone(ex.HttpCode, (int?)ex.DiscordCode))
                {
                    Logger.Information("The webhook for channel {0} was deleted; creating a new one.", channel.Id);
                    this.webhooks.Evict(channel.Id);
                    if (this.plugin.Config.ChannelConfigs.TryGetValue(channel.Id, out var config))
                        config.WebhookId = 0;
                }
            }

            return null;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Logger.Verbose("Discord DISPOSE!!");
                this.MessageQueue?.Stop();
                this.InboundQueue?.Stop();
                // At most 3 s (#48): a stuck gateway must never hang unloading.
                try
                {
                    if (this.socketClient != null && !Task.Run(this.socketClient.LogoutAsync).Wait(TimeSpan.FromSeconds(3)))
                        Logger.Warning("Discord logout timed out after 3 s; continuing.");
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Discord logout failed; continuing.");
                }

                this.socketClient?.Dispose();
                this.webhooks.Dispose();
            }
        }
    }
}
