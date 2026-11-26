using Microsoft.Extensions.Logging.Abstractions;
using PhoneBox.Agent;
using Xunit;

/// <summary>Fork: the Claude quota comes from the rate_limits that Claude Code's status line saves, never the token.</summary>
public sealed class StatusLineLimitsTests
{
    private const long Now = 1_764_150_000_000; // ms

    [Fact]
    public void ReadsBothWindowsWithResetTimesInMilliseconds()
    {
        var limits = QuotaMonitor.ParseStatusLineLimits(
            """{"five_hour":{"used_percentage":42.5,"resets_at":1764153600},"seven_day":{"used_percentage":81,"resets_at":1764500000}}""", Now);

        Assert.Equal(2, limits.Count);
        Assert.Equal("5-hour session", limits[0].Label);
        Assert.Equal(42.5, limits[0].Percent);
        Assert.Equal(1_764_153_600_000, limits[0].ResetsAt);
        Assert.Equal(18000, limits[0].WindowSec);
        Assert.Equal("normal", limits[0].Severity);
        Assert.Equal("Weekly", limits[1].Label);
        Assert.Equal("warning", limits[1].Severity);
    }

    [Fact]
    public void AWindowWhoseResetHasPassedShowsAsReset()
    {
        var limits = QuotaMonitor.ParseStatusLineLimits("""{"five_hour":{"used_percentage":97,"resets_at":1764140000}}""", Now);

        var only = Assert.Single(limits);
        Assert.Equal(0, only.Percent);
        Assert.Null(only.ResetsAt);
        Assert.Equal("normal", only.Severity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{\"five_hour\":{\"used_percen")] // half-written
    [InlineData("[]")]
    [InlineData("{\"spend_limit\":{\"used_percentage\":10}}")] // gateway-only window, not shown
    public void NothingUsableGivesNoLimits(string? json) =>
        Assert.Empty(QuotaMonitor.ParseStatusLineLimits(json, Now));

    [Fact]
    public async Task TheTickReportsTheFileOrAskForTheStatusLine()
    {
        var dir = Directory.CreateTempSubdirectory("pb-statusline-").FullName;
        try
        {
            var path = Path.Combine(dir, "rate-limits.json");
            var monitor = new QuotaMonitor(NullLogger<QuotaMonitor>.Instance) { StatusLineLimitsPath = path };

            await monitor.ClaudeStatusLineTickAsync(Now, CancellationToken.None);
            var missing = Assert.Single(monitor.Snapshot(Now), p => p.Id == "claude");
            Assert.Equal(QuotaMonitor.NoStatusLineLimits, missing.Error);

            File.WriteAllText(path, """{"five_hour":{"used_percentage":12,"resets_at":1764153600}}""");
            await monitor.ClaudeStatusLineTickAsync(Now, CancellationToken.None);
            var read = Assert.Single(monitor.Snapshot(Now), p => p.Id == "claude");
            Assert.Null(read.Error);
            Assert.Equal(12, Assert.Single(read.Limits).Percent);
        }
        finally { Directory.Delete(dir, true); }
    }
}
