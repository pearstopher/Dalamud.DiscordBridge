using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace Dalamud.DiscordBridge.SelfTests
{
    /// <summary>
    /// Runs the in-game self-tests for dev builds: from the /dcp button, /dcptest, or automatically after each load.
    /// Results go to the UI, /xllog, chat, and selftest-results.json in the plugin config folder (Claude reads that).
    /// </summary>
    internal sealed class SelfTestService : IDisposable
    {
        private static IPluginLog Logger => Service.Logger;

        internal const string ResultsFileName = "selftest-results.json";

        private readonly DiscordBridgePlugin plugin;
        private readonly CancellationTokenSource lifetime = new();
        private readonly object gate = new();
        private readonly List<SelfTestRecord> live = [];
        private int running;

        internal SelfTestService(DiscordBridgePlugin plugin)
        {
            this.plugin = plugin;
        }

        /// <summary>Dev plugin (loaded from a Dev Plugin Location) or a Debug build.</summary>
        internal static bool IsDevBuild => Service.Interface.IsDev || BuildInfo.Configuration == "Debug";

        internal static string ResultsPath => Path.Combine(Service.Interface.GetPluginConfigDirectory(), ResultsFileName);

        internal bool IsRunning => Volatile.Read(ref this.running) == 1;

        internal SelfTestReport? LastReport { get; private set; }

        internal int TotalTests => GameSelfTests.Build(this.plugin).Count;

        /// <summary>Results so far in the current run (or the last run).</summary>
        internal IReadOnlyList<SelfTestRecord> Snapshot()
        {
            lock (this.gate)
                return this.live.ToList();
        }

        /// <summary>Start a run in the background. Returns false if one is already running or this isn't a dev build.</summary>
        internal bool Start()
        {
            if (!IsDevBuild || Interlocked.Exchange(ref this.running, 1) == 1)
                return false;

            lock (this.gate)
                this.live.Clear();

            _ = Task.Run(this.RunAsync);
            return true;
        }

        /// <summary>Dev setting: wait until logged in and connected (up to 5 minutes), then run once.</summary>
        internal void StartWhenReady()
        {
            if (!IsDevBuild)
                return;

            var token = this.lifetime.Token;
            _ = Task.Run(async () =>
            {
                var deadline = DateTime.UtcNow.AddMinutes(5);
                while (!token.IsCancellationRequested && DateTime.UtcNow < deadline)
                {
                    var loggedIn = await Service.Framework.RunOnFrameworkThread(() => Service.State.IsLoggedIn);
                    if (loggedIn && this.plugin.Discord.State == DiscordState.Ready)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(3), token); // let the login burst of chat settle
                        this.Start();
                        return;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(2), token);
                }
            }, token);
        }

        private async Task RunAsync()
        {
            try
            {
                var build = BuildInfo.Describe(Service.Interface.AssemblyLocation.FullName);
                var report = await SelfTestRunner.RunAsync(GameSelfTests.Build(this.plugin), build, this.lifetime.Token, record =>
                {
                    lock (this.gate)
                        this.live.Add(record);
                });

                this.LastReport = report;

                try
                {
                    SelfTestRunner.WriteJson(report, ResultsPath);
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Could not write {0}.", ResultsPath);
                }

                Logger.Information(SelfTestRunner.ToText(report));
                await Service.Framework.RunOnFrameworkThread(() =>
                {
                    var summary = $"{Constant.PluginName} self-tests: {report.Summary}.";
                    if (report.Count(SelfTestStatus.Fail) > 0)
                    {
                        Service.Chat.PrintError(summary + $" Details in {Constant.MainCommand}.");
                        foreach (var fail in report.Results.Where(r => r.Status == SelfTestStatus.Fail))
                            Service.Chat.PrintError($"  ✗ {fail.Id}: {fail.Message}");
                    }
                    else
                    {
                        Service.Chat.Print(summary);
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Self-test run failed.");
            }
            finally
            {
                Volatile.Write(ref this.running, 0);
            }
        }

        public void Dispose()
        {
            this.lifetime.Cancel();
            this.lifetime.Dispose();
        }
    }
}
