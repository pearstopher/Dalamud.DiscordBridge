using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Dalamud.DiscordBridge.SelfTests
{
    public enum SelfTestStatus
    {
        Pass,
        Warn,
        Fail,
        Skip,
    }

    /// <summary>What a single self-test returns.</summary>
    public readonly record struct SelfTestResult(SelfTestStatus Status, string Message)
    {
        public static SelfTestResult Pass(string message = "OK") => new(SelfTestStatus.Pass, message);
        public static SelfTestResult Warn(string message) => new(SelfTestStatus.Warn, message);
        public static SelfTestResult Fail(string message) => new(SelfTestStatus.Fail, message);
        public static SelfTestResult Skip(string message) => new(SelfTestStatus.Skip, message);
    }

    /// <summary>A recorded result, as shown in the UI and written to selftest-results.json.</summary>
    public sealed record SelfTestRecord(
        string Id,
        string Area,
        string Description,
        [property: JsonConverter(typeof(JsonStringEnumConverter))] SelfTestStatus Status,
        string Message,
        long DurationMs);

    /// <summary>A full run.</summary>
    public sealed record SelfTestReport(DateTimeOffset StartedAt, string Build, IReadOnlyList<SelfTestRecord> Results)
    {
        public int Count(SelfTestStatus status) => this.Results.Count(r => r.Status == status);

        public string Summary =>
            $"{this.Count(SelfTestStatus.Pass)} passed, {this.Count(SelfTestStatus.Warn)} warnings, " +
            $"{this.Count(SelfTestStatus.Fail)} failed, {this.Count(SelfTestStatus.Skip)} skipped";
    }

    /// <summary>One in-game self-test. Keep each test independent and safe to run repeatedly.</summary>
    public interface ISelfTest
    {
        /// <summary>Stable id, e.g. "inbound.display.loop-guard". Referenced from docs/IN_GAME_TEST_PLAN.md.</summary>
        string Id { get; }

        string Area { get; }

        string Description { get; }

        /// <summary>Per-test time limit.</summary>
        TimeSpan Timeout { get; }

        Task<SelfTestResult> RunAsync(CancellationToken token);
    }

    /// <summary>A self-test defined by a delegate.</summary>
    public sealed class DelegateSelfTest(string id, string area, string description, Func<CancellationToken, Task<SelfTestResult>> run, TimeSpan? timeout = null)
        : ISelfTest
    {
        public string Id { get; } = id;
        public string Area { get; } = area;
        public string Description { get; } = description;
        public TimeSpan Timeout { get; } = timeout ?? TimeSpan.FromSeconds(15);

        public Task<SelfTestResult> RunAsync(CancellationToken token) => run(token);
    }

    /// <summary>
    /// Runs self-tests one after another. A test that throws fails; one that exceeds its timeout fails. The runner
    /// itself never throws.
    /// </summary>
    public static class SelfTestRunner
    {
        public static async Task<SelfTestReport> RunAsync(IEnumerable<ISelfTest> tests, string build, CancellationToken token = default,
                                                           Action<SelfTestRecord>? onResult = null)
        {
            var started = DateTimeOffset.Now;
            var results = new List<SelfTestRecord>();

            foreach (var test in tests)
            {
                if (token.IsCancellationRequested)
                    break;

                var watch = Stopwatch.StartNew();
                SelfTestResult result;
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(test.Timeout);
                    var run = test.RunAsync(timeout.Token);
                    var finished = await Task.WhenAny(run, Task.Delay(test.Timeout, CancellationToken.None)).ConfigureAwait(false);
                    result = finished == run
                        ? await run.ConfigureAwait(false)
                        : SelfTestResult.Fail($"timed out after {test.Timeout.TotalSeconds:0}s");
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    result = SelfTestResult.Fail($"timed out after {test.Timeout.TotalSeconds:0}s");
                }
                catch (Exception ex)
                {
                    result = SelfTestResult.Fail($"{ex.GetType().Name}: {ex.Message}");
                }

                var record = new SelfTestRecord(test.Id, test.Area, test.Description, result.Status, result.Message, watch.ElapsedMilliseconds);
                results.Add(record);
                onResult?.Invoke(record);
            }

            return new SelfTestReport(started, build, results);
        }

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static string ToJson(SelfTestReport report) => JsonSerializer.Serialize(report, JsonOptions);

        /// <summary>Plain-text report for the clipboard / issue comments.</summary>
        public static string ToText(SelfTestReport report)
        {
            var lines = new List<string>
            {
                $"Discord Chat Pears self-tests: {report.Summary}",
                $"Run {report.StartedAt:yyyy-MM-dd HH:mm:ss} on {report.Build}",
            };
            lines.AddRange(report.Results.Select(r => $"[{r.Status.ToString().ToUpperInvariant()}] {r.Id}: {r.Message} ({r.DurationMs} ms)"));
            return string.Join(Environment.NewLine, lines);
        }

        public static void WriteJson(SelfTestReport report, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, ToJson(report));
        }
    }
}
