namespace Dalamud.DiscordBridge.Tests;

public class BuildInfoTests
{
    [Fact]
    public void Describe_FormatsConfigurationTimeAndPath()
    {
        Assert.Equal(
            @"Debug build, built 2026-11-16 10:36:02, from C:\x\Plugin.dll",
            BuildInfo.Describe("Debug", "2026-11-16 10:36:02", @"C:\x\Plugin.dll"));
    }

    [Fact]
    public void Describe_MissingFile_SaysUnknownTime()
    {
        Assert.Contains("unknown time", BuildInfo.Describe(@"C:\definitely\not\here.dll"));
    }
}
