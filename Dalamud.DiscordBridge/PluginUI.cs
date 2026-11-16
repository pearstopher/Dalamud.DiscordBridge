using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Numerics;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;

namespace Dalamud.DiscordBridge
{
    public class PluginUI(DiscordBridgePlugin plugin)
    {
        private static IPluginLog Logger => Service.Logger;
        private readonly DiscordBridgePlugin Plugin = plugin;
        private bool isVisible;

        private string token = string.Empty;
        private string username = string.Empty;
        private bool showToken;

        /// <summary>One reconnect at a time, off the game thread (#44).</summary>
        private readonly SwapGate reconnect = new();

        private static Vector4 errorColor = new(1f, 0f, 0f, 1f);
        private static Vector4 fineColor = new(0.337f, 1f, 0.019f, 1f);

        public void Show()
        {
            this.token = this.Plugin.Config.DiscordToken;
            this.username = this.Plugin.Config.DiscordOwnerName;

            this.isVisible = true;
        }

        private static Vector4 warnColor = new(1f, 0.8f, 0.2f, 1f);
        private static Vector4 skipColor = new(0.6f, 0.6f, 0.6f, 1f);

        /// <summary>Developer builds only: run the in-game self-tests and show the results.</summary>
        private void DrawSelfTests()
        {
            var tests = this.Plugin.SelfTests;
            var config = this.Plugin.Config;

            ImGui.Dummy(new Vector2(10, 10));
            ImGui.Separator();
            ImGui.TextColored(warnColor, "Developer: self-tests");

            if (tests.IsRunning)
            {
                ImGui.Text($"Running… {tests.Snapshot().Count}/{tests.TotalTests}");
            }
            else if (ImGui.Button("Run self-tests"))
            {
                tests.Start();
            }

            var results = tests.Snapshot();
            if (!tests.IsRunning && tests.LastReport is { } report)
            {
                ImGui.SameLine();
                if (ImGui.Button("Copy results"))
                    ImGui.SetClipboardText(SelfTests.SelfTestRunner.ToText(report));
                ImGui.SameLine();
                ImGui.Text($"{report.Summary} ({report.StartedAt:HH:mm:ss})");
            }

            var autoRun = config.DevRunSelfTestsOnLoad;
            if (ImGui.Checkbox("Run self-tests after each load", ref autoRun))
            {
                config.DevRunSelfTestsOnLoad = autoRun;
                config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("With Dalamud's automatic reloading, every dotnet build reloads the plugin and runs the tests once you're logged in.");

            var tellSelf = config.DevSelfTestTellSelf;
            if (ImGui.Checkbox("Self-tests may send a tell to myself", ref tellSelf))
            {
                config.DevSelfTestTellSelf = tellSelf;
                config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Enables send.tell-self: a real /tell to your own character (goes through the server, only to you).");

            ImGui.TextDisabled($"Results file: {SelfTests.SelfTestService.ResultsPath}");
            if (config.SelfTestChannelId == null)
                ImGui.TextDisabled($"Discord round trip: run {config.DiscordBotPrefix}selftesthere in a private channel to enable.");

            if (results.Count == 0)
                return;

            if (ImGui.BeginTable("selftests", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Result");
                ImGui.TableSetupColumn("Test");
                ImGui.TableSetupColumn("Details");
                ImGui.TableHeadersRow();

                foreach (var r in results)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    var color = r.Status switch
                    {
                        SelfTests.SelfTestStatus.Pass => fineColor,
                        SelfTests.SelfTestStatus.Fail => errorColor,
                        SelfTests.SelfTestStatus.Warn => warnColor,
                        _ => skipColor,
                    };
                    ImGui.TextColored(color, r.Status.ToString().ToUpperInvariant());
                    ImGui.TableNextColumn();
                    ImGui.Text(r.Id);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(r.Description);
                    ImGui.TableNextColumn();
                    ImGui.TextWrapped($"{r.Message} ({r.DurationMs} ms)");
                }

                ImGui.EndTable();
            }
        }

        public void Draw()
        {
            if (!this.isVisible)
                return;

            ImGui.Begin($"{Constant.PluginName} Setup", ref this.isVisible);

            ImGui.Text("In this window, you can set up " + Constant.PluginName + ".\n\n" +
                       "To begin, enter your discord bot token and username or user ID number below, then click \"Save\".\n" +
                       "As soon as the red text says \"connected\", click the \"Join my server\" button and add the bot to one of your personal servers.\n" +
                       $"You can then use the {this.Plugin.Config.DiscordBotPrefix}help command in your discord server to specify channels.");

            ImGui.Dummy(new Vector2(10, 10));

            // Hidden by default so it isn't exposed when streaming or screen sharing (#44).
            ImGui.InputText("Enter your bot token", ref this.token, 100, this.showToken ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password);
            ImGui.SameLine();
            ImGui.Checkbox("Show", ref this.showToken);
            ImGui.InputText("Enter your Username(e.g. mydiscorduser)", ref this.username, 50);

            ImGui.Dummy(new Vector2(10, 10));

            ImGui.Text("Status: ");
            ImGui.SameLine();

            var message = this.reconnect.IsBusy ? "Reconnecting…" : this.Plugin.Discord.State switch
            {
                DiscordState.None => "Not started",
                DiscordState.Ready => "Connected!",
                DiscordState.TokenInvalid => "Token empty or invalid.",
                DiscordState.Disconnected => "Connection lost, reconnecting…",
                _ => "Unknown"
            };

            ImGui.TextColored(this.Plugin.Discord.State == DiscordState.Ready ? fineColor : errorColor, message);
            if (this.Plugin.Discord.State == DiscordState.Ready && ImGui.Button("Join my server"))
            {
                Process.Start(
                    new ProcessStartInfo { 
                        FileName = $"https://discordapp.com/oauth2/authorize?client_id={this.Plugin.Discord.UserId}&scope=bot&permissions=2684742720", UseShellExecute = true 
                    } 
                );
            }

            ImGui.Dummy(new Vector2(10, 10));

            ImGui.Text("Discord → game");
            var config = this.Plugin.Config;

            var showInbound = config.InboundDisplayEnabled;
            if (ImGui.Checkbox("Show Discord messages in game chat", ref showInbound))
            {
                config.InboundDisplayEnabled = showInbound;
                config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Only for channels set up with {config.DiscordBotPrefix}setinbound. Shown in your chat log only; nobody else sees them.");

            var asEcho = config.InboundDisplayAsEcho;
            if (ImGui.Checkbox("Show them as Echo instead of the channel's own colour", ref asEcho))
            {
                config.InboundDisplayAsEcho = asEcho;
                config.Save();
            }

            var reactions = config.InboundReactions;
            if (ImGui.Checkbox("React on Discord when a message reaches the game", ref reactions))
            {
                config.InboundReactions = reactions;
                config.Save();
            }

            var allowSend = config.AllowSendAsCharacter;
            if (ImGui.Checkbox("Allow sending as my character", ref allowSend))
            {
                config.AllowSendAsCharacter = allowSend;
                config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("When ticked, your own Discord messages in channels with inbound relay are really sent as your character (shown as [S]); otherwise only shown to you ([D]). Only this checkbox controls it.");
            var markSent = config.MarkSentInGame;
            if (ImGui.Checkbox($"Tag messages sent by the plugin with {SentMarker.Tag(config.SentTag)} on my screen", ref markSent))
            {
                config.MarkSentInGame = markSent;
                config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Only your screen changes; other players see your normal message. [D] = shown only to you, [S] = really sent.");

            if (config.AllowSendAsCharacter)
                ImGui.TextColored(errorColor, "Outside Dalamud's plugin rules: the game server receives chat you didn't type in game. Use at your own risk.");

            if (SelfTests.SelfTestService.IsDevBuild)
                this.DrawSelfTests();

            ImGui.Dummy(new Vector2(10, 10));

            if (ImGui.Button("How does this work?"))
            {
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = Constant.HelpLink,
                        UseShellExecute = true
                    } 
                );
            }

            ImGui.SameLine();

            if (this.reconnect.IsBusy)
            {
                ImGui.BeginDisabled();
                ImGui.Button("Reconnecting…");
                ImGui.EndDisabled();
            }
            else if (ImGui.Button("Save") && this.reconnect.TryBegin())
            {
                Logger.Verbose("Reloading Discord...");
                this.Plugin.Config.DiscordToken = this.token;
                this.Plugin.Config.DiscordOwnerName = this.username;
                this.Plugin.Config.Save();

                // Disposing waits for the queues and the logout: never on the game thread (#44).
                var old = this.Plugin.Discord;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        old.Dispose();
                        var fresh = new DiscordHandler(this.Plugin);
                        this.Plugin.Discord = fresh;
                        await fresh.Start();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Could not reconnect to Discord.");
                    }
                    finally
                    {
                        this.reconnect.End();
                    }
                });
            }

            ImGui.End();
        }
    }
}
