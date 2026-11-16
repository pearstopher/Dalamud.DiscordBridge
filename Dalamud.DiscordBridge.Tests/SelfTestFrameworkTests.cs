using System.Text.Json;
using System.Text.RegularExpressions;
using Dalamud.DiscordBridge.SelfTests;

namespace Dalamud.DiscordBridge.Tests;

public class SelfTestRunnerTests
{
    private static DelegateSelfTest T(string id, Func<CancellationToken, Task<SelfTestResult>> run, TimeSpan? timeout = null)
        => new(id, "Area", $"desc {id}", run, timeout);

    [Fact]
    public async Task Runs_AllTests_InOrder_AndReportsEachAsItFinishes()
    {
        var seen = new List<string>();
        var report = await SelfTestRunner.RunAsync(
        [
            T("a", _ => Task.FromResult(SelfTestResult.Pass())),
            T("b", _ => Task.FromResult(SelfTestResult.Warn("hmm"))),
            T("c", _ => Task.FromResult(SelfTestResult.Skip("n/a"))),
        ], "build", onResult: r => seen.Add(r.Id));

        Assert.Equal(["a", "b", "c"], report.Results.Select(r => r.Id));
        Assert.Equal(["a", "b", "c"], seen);
        Assert.Equal("1 passed, 1 warnings, 0 failed, 1 skipped", report.Summary);
    }

    [Fact]
    public async Task ThrowingTest_Fails_AndLaterTestsStillRun()
    {
        var report = await SelfTestRunner.RunAsync(
        [
            T("boom", _ => throw new InvalidOperationException("kaput")),
            T("after", _ => Task.FromResult(SelfTestResult.Pass())),
        ], "build");

        Assert.Equal(SelfTestStatus.Fail, report.Results[0].Status);
        Assert.Contains("kaput", report.Results[0].Message);
        Assert.Equal(SelfTestStatus.Pass, report.Results[1].Status);
    }

    [Fact]
    public async Task SlowTest_TimesOut_AsFailure()
    {
        var report = await SelfTestRunner.RunAsync(
        [
            T("slow", async _ => { await Task.Delay(Timeout.Infinite); return SelfTestResult.Pass(); }, TimeSpan.FromMilliseconds(100)),
        ], "build");

        Assert.Equal(SelfTestStatus.Fail, report.Results[0].Status);
        Assert.Contains("timed out", report.Results[0].Message);
    }

    [Fact]
    public async Task Cancelled_StopsBeforeRemainingTests()
    {
        using var cts = new CancellationTokenSource();
        var report = await SelfTestRunner.RunAsync(
        [
            T("first", _ => { cts.Cancel(); return Task.FromResult(SelfTestResult.Pass()); }),
            T("second", _ => Task.FromResult(SelfTestResult.Pass())),
        ], "build", cts.Token);

        Assert.Single(report.Results);
    }

    [Fact]
    public async Task Json_HasStatusNamesAndAllFields()
    {
        var report = await SelfTestRunner.RunAsync([T("x", _ => Task.FromResult(SelfTestResult.Fail("bad")))], "Debug build");

        using var doc = JsonDocument.Parse(SelfTestRunner.ToJson(report));
        var result = doc.RootElement.GetProperty("Results")[0];
        Assert.Equal("Fail", result.GetProperty("Status").GetString());
        Assert.Equal("x", result.GetProperty("Id").GetString());
        Assert.Equal("bad", result.GetProperty("Message").GetString());
        Assert.Equal("Debug build", doc.RootElement.GetProperty("Build").GetString());
    }

    [Fact]
    public async Task Text_ListsEveryResult()
    {
        var report = await SelfTestRunner.RunAsync([T("x", _ => Task.FromResult(SelfTestResult.Pass("fine")))], "b");

        Assert.Contains("[PASS] x: fine", SelfTestRunner.ToText(report));
    }
}

public class SelfTestRegistryTests
{
    // Building the list only creates delegates; nothing touches the game or Dalamud services.
    private static readonly IReadOnlyList<ISelfTest> All = GameSelfTests.Build(null!);

    [Fact]
    public void Ids_AreUnique()
    {
        Assert.Equal(All.Count, All.Select(t => t.Id).Distinct().Count());
    }

    [Fact]
    public void Ids_AreLowercaseDotted()
    {
        foreach (var test in All)
            Assert.Matches(new Regex(@"^[a-z]+(\.[a-z-]+)+$"), test.Id);
    }

    [Fact]
    public void EveryTest_HasAreaDescriptionAndSaneTimeout()
    {
        foreach (var test in All)
        {
            Assert.False(string.IsNullOrWhiteSpace(test.Area), test.Id);
            Assert.False(string.IsNullOrWhiteSpace(test.Description), test.Id);
            Assert.InRange(test.Timeout, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1));
        }
    }

    [Fact]
    public void EveryTest_IsReferencedInTheInGameTestPlan()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dalamud.DiscordBridge.sln")))
            dir = dir.Parent;
        var plan = File.ReadAllText(Path.Combine(dir!.FullName, "docs", "IN_GAME_TEST_PLAN.md"));

        foreach (var test in All)
            Assert.True(plan.Contains($"`{test.Id}`"), $"docs/IN_GAME_TEST_PLAN.md doesn't mention self-test `{test.Id}`");
    }

    [Fact]
    public void ItemIconUrl_PadsFolderAndFile()
    {
        Assert.Equal("https://beta.xivapi.com/api/1/asset?path=ui%2Ficon%2F021000%2F021206_hr1.tex&format=png",
                     DiscordMessageQueue.ItemIconUrl(21206));
    }
}
