using Dalamud.DiscordBridge.API;
using Dalamud.DiscordBridge.Attributes;
using Dalamud.DiscordBridge.Model;
using Dalamud.DiscordBridge.SelfTests;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using System;
using System.Threading.Tasks;

namespace Dalamud.DiscordBridge
{
    public class DiscordBridgePlugin : IDalamudPlugin
    {
        public static DiscordBridgePlugin Plugin { get; private set; }
        private readonly PluginCommandManager<DiscordBridgePlugin> commandManager;
        private readonly PluginUI ui;

        public DiscordHandler Discord;
        public Configuration Config;
        public DiscordBridgeProvider DiscordBridgeProvider;

        /// <summary>Swallows the game's echo of lines we print/send for Discord messages, so they aren't relayed back.</summary>
        internal readonly InboundEchoGuard EchoGuard = new();

        /// <summary>
        /// Self-test hook: called for every chat line with (type, text, swallowedByEchoGuard). Null outside self-tests.
        /// </summary>
        internal Action<XivChatType, string, bool>? ChatObserver;

        /// <summary>Converts inbound Discord messages to game-safe text.</summary>
        internal readonly DiscordToGameConverter InboundConverter = new();

        private InboundDisplay inboundDisplay = null!;
        private InboundSender inboundSender = null!;

        internal InboundDisplay InboundDisplayService => this.inboundDisplay;
        internal InboundSender InboundSenderService => this.inboundSender;

        /// <summary>Dev-only in-game self-tests (see DEVELOPMENT.md).</summary>
        internal SelfTestService SelfTests { get; private set; } = null!;

        private static IPluginLog Logger => Service.Logger;

        // public IPlayerCharacter? cachedLocalPlayer;
        public string cachedPlayerName;
        public string cachedPlayerWorld;


        public DiscordBridgePlugin(IDalamudPluginInterface pluginInterface, ICommandManager command)
        {
            Plugin = this;
            pluginInterface.Create<Service>();

            var loadedConfig = pluginInterface.GetPluginConfig() as Configuration;
            var importedLegacyConfig = false;
            if (loadedConfig == null)
            {
                // First run: take over the official Discord Chat Bridge's settings, if it was ever used (read-only).
                loadedConfig = ConfigImport.TryReadLegacy(
                    ConfigImport.LegacyConfigPath(pluginInterface.ConfigFile.FullName),
                    ex => Logger.Warning(ex, "Could not import settings from Discord Chat Bridge."));
                importedLegacyConfig = loadedConfig != null;
            }

            this.Config = loadedConfig ?? new Configuration();
            this.Config.Initialize(pluginInterface);
            if (importedLegacyConfig)
                this.Config.Save();

            pluginInterface.UiBuilder.OpenConfigUi += this.OpenConfigUi;

            // sanity check - ensure there are no invalid types leftover from past versions.
            foreach (DiscordChannelConfig config in this.Config.ChannelConfigs.Values)
            {
                for (int i = 0; i < config.ChatTypes.Count; i++)
                {
                    XivChatType xct = config.ChatTypes[i];
                    if ((int)xct > 127)
                    {
                        config.ChatTypes[i] = (XivChatType)((int)xct & 0x7F);
                        this.Config.Save();
                    }
                    try
                    {
                        xct.GetInfo();
                    }
                    catch (ArgumentException)
                    {
                        Logger.Error($"Removing invalid chat type before it could cause problems ({(int)xct}){xct}.");
                        config.ChatTypes.RemoveAt(i--);
                        this.Config.Save();
                    }
                }
            }

            
            this.DiscordBridgeProvider = new DiscordBridgeProvider(pluginInterface, new DiscordBridgeAPI(this));
            this.inboundDisplay = new InboundDisplay(this.Config, this.EchoGuard);
            this.inboundSender = new InboundSender(this.Config, this.EchoGuard);
            this.SelfTests = new SelfTestService(this);
            this.Discord = new DiscordHandler(this);
            // Task t = this.Discord.Start(); // bot won't start if we just have this


            System.Threading.Tasks.Task.Run(async () => // makes the bot actually start
            {
                await this.Discord.Start();
                await this.Discord.SetIdlePresence();

                await Service.Framework.RunOnFrameworkThread(() => {
                    if (Service.State.IsLoggedIn)
                    {
                        cachedPlayerName = Service.PlayerState.CharacterName ?? "";
                        cachedPlayerWorld = Service.PlayerState.HomeWorld.Value.Name.ToString() ?? "";
                    }
                });
            });
            




            this.ui = new PluginUI(this);
            pluginInterface.UiBuilder.Draw += this.ui.Draw;

            Service.Chat.ChatMessage += ChatOnOnChatMessage;
            Service.State.CfPop += ClientStateOnCfPop;
            Service.State.Login += OnLoginEvent;
            Service.State.Logout += OnLogoutEvent;
            Service.State.TerritoryChanged += OnTerritoryChanged;


            this.commandManager = new PluginCommandManager<DiscordBridgePlugin>(this, command);

            if (importedLegacyConfig)
            {
                Service.Chat.Print($"{Constant.PluginName} imported your settings from Discord Chat Bridge. " +
                                   "Disable Discord Chat Bridge so messages aren't relayed twice.");
            }

            if (string.IsNullOrEmpty(this.Config.DiscordToken))
            {
                Service.Chat.PrintError($"{Constant.PluginName} was installed successfully. " +
                                                              $"Please use the \"{Constant.MainCommand}\" command to set it up.");
            }

            if (SelfTestService.IsDevBuild && this.Config.DevRunSelfTestsOnLoad)
                this.SelfTests.StartWhenReady();

            // Lets a developer confirm in /xllog exactly which build the game is running (see DEVELOPMENT.md).
            Logger.Information($"{pluginInterface.Manifest.Name} loaded ({BuildInfo.Describe(pluginInterface.AssemblyLocation.FullName)})");
        }

        private void OnTerritoryChanged(uint obj)
        {
            cachedPlayerName = Service.PlayerState.CharacterName ?? "";
            cachedPlayerWorld = Service.PlayerState.HomeWorld.Value.Name.ToString() ?? "";
        }

        private async void OnLoginEvent()
        {
            // Since I'm pulling this on Framework updates now, this might not be needed anymore.
            // But I'll keep it for now just in case.
            // cachedLocalPlayer = await Service.Framework.RunOnFrameworkThread(() => Service.ObjectTable.LocalPlayer);

            //cachedLocalPlayer = Service.ObjectTable?.LocalPlayer;
            cachedPlayerName = Service.PlayerState.CharacterName ?? "";
            cachedPlayerWorld = Service.PlayerState.HomeWorld.Value.Name.ToString() ?? "";

            await this.Discord.SetOnlinePresence();

            // Annoyance: Sorry to the users who might be affected if this goes badly. I wanted to make a specific person shut up.
            if (this.Discord.MessageQueue.isStopped())
            {
                this.Discord.MessageQueue.Start();
            }
        }

        private async void OnLogoutEvent(int type, int code)
        {
            // Parameters:
            //   type:
            //     The type of logout.
            //
            //   code:
            //     The success/failure code

            // Annoyance: Sorry to the users who might be affected if this goes badly. I wanted to make a specific person shut up.
            if (!this.Discord.MessageQueue.isStopped())
            {
                this.Discord.MessageQueue.Stop();
                this.Discord.MessageQueue.ClearQueue();
            }


            // cachedLocalPlayer = null;


            await this.Discord.SetIdlePresence();
            // this.Discord.Dispose();
            // this.Discord = new DiscordHandler(this);
        }

        private void ClientStateOnCfPop(ContentFinderCondition e)
        {
            this.Discord.MessageQueue.Enqueue(new QueuedContentFinderEvent
            {
                ContentFinderCondition = e
            });
        }

        private void ChatOnOnChatMessage(IHandleableChatMessage message)
        {
            if (message.IsHandled) return; // don't process a message that's been handled.

            // A line we printed or sent because of a Discord message: don't relay it back to Discord.
            var isOwnEcho = this.EchoGuard.TryConsume(message.LogKind, message.Message.TextValue);
            this.ChatObserver?.Invoke(message.LogKind, message.Message.TextValue, isOwnEcho);
            if (isOwnEcho) return;

            if (message.LogKind == XivChatType.RetainerSale)
            {
                this.Discord.MessageQueue.Enqueue(new QueuedRetainerItemSaleEvent 
                {
                    ChatType = message.LogKind,
                    Message = message.Message,
                    Sender = message.Sender
                });
            }
            else
            {
                this.Discord.MessageQueue.Enqueue(new QueuedChatEvent
                {
                    ChatType = message.LogKind,
                    Message = message.Message,
                    Sender = message.Sender,
                    AvatarUrl = ""
                });
            }
            
        }

        private void OpenConfigUi()
        {
            this.ui.Show();
        }

        
        [Command(Constant.MainCommand)]
        [Aliases("/discordpears")]
        [HelpMessage("Show settings for the discord bridge plugin.")]
        public void OpenSettingsCommand(string command, string args)
        {
            this.ui.Show();
        }

        
        [Command("/dcpdebug")]
        [HelpMessage("Send a debug message using a particular chat type")]
        [DoNotShowInHelp]
        public void DebugCommand(string command, string args)
        {
            string[] commandArgs = args.Split(' ');
            this.Discord.MessageQueue.Enqueue(new QueuedChatEvent
            {
                ChatType = XivChatTypeExtensions.GetBySlug(commandArgs?[0] ?? "e"),
                Message = new SeString(new Payload[]{new TextPayload("Test Message"), }),
                Sender = new SeString(new Payload[]{new TextPayload("Test Sender"), }),
                AvatarUrl = Plugin.Config.DefaultAvatarURL
            });
        }
        
        [Command("/dcpsaledebug")]
        [HelpMessage("Send a fake item sale chat payload.")]
        [DoNotShowInHelp]
        public void SaleDebugCommand(string command, string args)
        {
            // make a sample sale message. This is using Titanium Ore for an item
            Item sampleitem = Service.Data.GetExcelSheet<Item>().GetRow(12537);
            SeString sameplesale = new(new Payload[] { new TextPayload("The "), new ItemPayload(sampleitem.RowId, true), new TextPayload(sampleitem.Name.ExtractText()), new TextPayload(" you put up for sale in the Crystarium markets has sold for 777 gil (after fees).") });

            // Logger.Information($"Trying to make a fake sale: {sameplesale.TextValue}");

            this.Discord.MessageQueue.Enqueue(new QueuedRetainerItemSaleEvent
            {
                ChatType = XivChatType.RetainerSale,
                Message = sameplesale,
                Sender = new SeString(new Payload[] { new TextPayload("Test Sender"), }),
            });

            Service.Chat.Print(new XivChatEntry
            {
                Message = sameplesale,
                Type = XivChatType.Echo
            });

        }

        
        /// <summary>
        /// Handle a converted Discord message destined for the game. Called from the Discord handler's inbound queue
        /// (and by /dcpin). Lives on the plugin so it survives the Discord handler being recreated from the settings window.
        /// </summary>
        /// <param name="forceSend">Use the send path regardless of channel setting/owner (for /dcpsend).</param>
        internal async Task<InboundOutcome> HandleInboundAsync(InboundDiscordMessage message, bool forceSend = false)
        {
            var channelSends = this.Config.ChannelConfigs.TryGetValue(message.ChannelId, out var channel) && channel.InboundSendAsOwner;

            if (forceSend || InboundRules.ShouldSendAsOwner(message.IsOwner, this.Config.AllowSendAsCharacter, channelSends))
            {
                if (!this.Config.AllowSendAsCharacter)
                    return new InboundOutcome(InboundResult.Rejected, $"sending as your character is turned off in {Constant.MainCommand}");

                // Untruncated: an over-long message is rejected rather than sent cut off.
                var full = this.InboundConverter.Convert(message, int.MaxValue);
                return await this.inboundSender.SendAsync(message, full);
            }

            var text = this.InboundConverter.Convert(message);
            return new InboundOutcome(await this.inboundDisplay.ShowAsync(message, text));
        }

        [Command("/dcpin")]
        [HelpMessage("Simulate a Discord message arriving in game: /dcpin <kind> <name> <text…>")]
        [DoNotShowInHelp]
        public void InboundDebugCommand(string command, string args)
        {
            var parts = args.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                Service.Chat.PrintError($"Usage: /dcpin <kind> <name> <text>. Kinds: {InboundRules.AllowedSlugList}");
                return;
            }

            if (!InboundRules.TryParseTarget(parts[0], out var target, out var error) || target == null)
            {
                Service.Chat.PrintError(string.IsNullOrEmpty(error) ? "Pick a chat kind, not off." : error);
                return;
            }

            var fake = new InboundDiscordMessage
            {
                ChannelId = 0,
                MessageId = 0,
                AuthorId = 0,
                AuthorName = parts[1],
                IsOwner = false,
                TargetType = target.Value,
                Content = parts[2],
                CleanContent = parts[2],
            };

            RunInboundDebug("/dcpin", fake, forceSend: false);
        }

        [Command("/dcpsend")]
        [HelpMessage("Test 'send as me': your character says the text in a channel: /dcpsend <kind> <text…>")]
        [DoNotShowInHelp]
        public void SendDebugCommand(string command, string args)
        {
            var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                Service.Chat.PrintError($"Usage: /dcpsend <kind> <text>. Needs \"Allow sending as my character\" in {Constant.MainCommand}.");
                return;
            }

            if (!InboundRules.TryParseTarget(parts[0], out var target, out var error) || target == null)
            {
                Service.Chat.PrintError(string.IsNullOrEmpty(error) ? "Pick a chat kind, not off." : error);
                return;
            }

            var fake = new InboundDiscordMessage
            {
                ChannelId = 0,
                MessageId = 0,
                AuthorId = 0,
                AuthorName = this.cachedPlayerName ?? "you",
                IsOwner = true,
                TargetType = target.Value,
                Content = parts[1],
                CleanContent = parts[1],
            };

            RunInboundDebug("/dcpsend", fake, forceSend: true);
        }

        private void RunInboundDebug(string name, InboundDiscordMessage fake, bool forceSend)
        {
            _ = Task.Run(async () =>
            {
                var outcome = await this.HandleInboundAsync(fake, forceSend);
                Logger.Information("{0}: {1} {2}", name, outcome.Result, outcome.Reason ?? string.Empty);
                if (outcome.Result is not InboundResult.Shown and not InboundResult.Sent)
                    Service.Chat.PrintError($"{name}: {outcome.Result}{(outcome.Reason is { } r ? $" ({r})" : string.Empty)}");
            });
        }

        [Command("/dcptest")]
        [HelpMessage("Developer builds: run the in-game self-tests.")]
        [DoNotShowInHelp]
        public void SelfTestCommand(string command, string args)
        {
            if (!SelfTestService.IsDevBuild)
            {
                Service.Chat.PrintError("Self-tests are only available in developer builds.");
                return;
            }

            if (this.SelfTests.Start())
                Service.Chat.Print($"{Constant.PluginName}: running {this.SelfTests.TotalTests} self-tests…");
            else
                Service.Chat.PrintError("Self-tests are already running.");
        }

        [Command("/dcpprintlist")]
        [HelpMessage("Dump all plugin config to chat box.")]
        [DoNotShowInHelp]
        public void ListCommand(string command, string args)
        {
            foreach (var keyValuePair in XivChatTypeExtensions.TypeInfoDict)
            {
                 Service.Chat.Print($"({(int)keyValuePair.Key}) {keyValuePair.Key.GetSlug()} - {keyValuePair.Key.GetFancyName()}");
            }
        }
        

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;

            // Detach game events first so nothing new is enqueued into the handler being torn down.
            // Every subscription in the constructor must have a matching line here.
            Service.Chat.ChatMessage -= ChatOnOnChatMessage;
            Service.State.CfPop -= this.ClientStateOnCfPop;
            Service.State.Login -= OnLoginEvent;
            Service.State.Logout -= OnLogoutEvent;
            Service.State.TerritoryChanged -= OnTerritoryChanged;

            this.SelfTests?.Dispose();

            this.DiscordBridgeProvider.Dispose();
            
            this.Discord.Dispose();

            this.commandManager.Dispose();

            Service.Interface.UiBuilder.OpenConfigUi -= this.OpenConfigUi;

            Service.Interface.SavePluginConfig(this.Config);

            Service.Interface.UiBuilder.Draw -= this.ui.Draw;

            Logger.Information($"{Service.Interface.Manifest.Name} unloaded");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
