using System.Reflection;
using Sekiban.Dcb.Orleans.Grains;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

public class MultiProjectionGrainCatchUpDiagnosticsTests
{
    [Fact]
    public void BuildCatchUpEventTypeSummary_ShouldReturnTopTypesInStableOrder()
    {
        var result = Assert.IsType<string>(
            typeof(MultiProjectionGrain)
                .GetMethod("BuildCatchUpEventTypeSummary", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [new[] { "Beta", "Alpha", "Alpha", "Gamma", "Beta", "Alpha", "Gamma", "Delta", "Epsilon", "Zeta", "Eta" }]));

        Assert.Equal("Alpha:3, Beta:2, Gamma:2, Delta:1, Epsilon:1", result);
    }

    [Theory]
    [InlineData(1000L, 500, 500, 0, false, true)]
    [InlineData(50L, 500, 200, 0, false, true)]
    [InlineData(50L, 500, 500, 1, false, true)]
    [InlineData(50L, 500, 500, 0, true, true)]
    [InlineData(50L, 500, 500, 0, false, false)]
    public void ShouldLogCatchUpBatchAtInformation_ShouldMatchIssuePolicy(
        long totalElapsedMs,
        int requestedMaxCount,
        int fetchedCount,
        int filteredCount,
        bool persistTriggered,
        bool expected)
    {
        var telemetry = CreateTelemetry(
            totalElapsedMs,
            requestedMaxCount,
            fetchedCount,
            filteredCount,
            persistTriggered);

        var result = Assert.IsType<bool>(
            typeof(MultiProjectionGrain)
                .GetMethod("ShouldLogCatchUpBatchAtInformation", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [telemetry]));

        Assert.Equal(expected, result);
    }

    private static object CreateTelemetry(
        long totalElapsedMs,
        int requestedMaxCount,
        int fetchedCount,
        int filteredCount,
        bool persistTriggered)
    {
        var telemetryType = typeof(MultiProjectionGrain).GetNestedType("CatchUpBatchTelemetry", BindingFlags.NonPublic);
        Assert.NotNull(telemetryType);

        return Activator.CreateInstance(
            telemetryType!,
            1,
            "beginning",
            "current",
            "last",
            "target",
            requestedMaxCount,
            fetchedCount,
            filteredCount,
            Math.Max(0, fetchedCount - filteredCount),
            0,
            0,
            10L,
            20L,
            30L,
            0L,
            totalElapsedMs,
            "hot_only",
            0,
            fetchedCount,
            false,
            0,
            persistTriggered,
            persistTriggered ? "event_count_checkpoint" : "none",
            "Alpha:1")!;
    }
}
