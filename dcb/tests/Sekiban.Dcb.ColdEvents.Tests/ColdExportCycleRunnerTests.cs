using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ResultBoxes;
using Sekiban.Dcb.ServiceId;

namespace Sekiban.Dcb.ColdEvents.Tests;

public class ColdExportCycleRunnerTests
{
    [Fact]
    public async Task RunExportCycleWithBudgetAsync_should_continue_until_exporter_reports_no_more_events()
    {
        // Given
        StubColdEventExporter exporter = new(
        [
            ResultBox.FromValue(new ExportResult(100_000, [], "v1", "exported", ShouldContinueWithinCycle: true)),
            ResultBox.FromValue(new ExportResult(100_000, [], "v2", "exported", ShouldContinueWithinCycle: true)),
            ResultBox.FromValue(new ExportResult(50_000, [], "v3", "exported", ShouldContinueWithinCycle: true)),
            ResultBox.FromValue(new ExportResult(0, [], "v3", "no_events_since_checkpoint"))
        ]);
        ColdExportCycleRunner runner = new(
            exporter,
            Options.Create(new ColdEventStoreOptions
            {
                Enabled = true,
                RunOnStartup = true,
                PullInterval = TimeSpan.FromMinutes(30)
            }),
            new StubServiceIdProvider("default"),
            NullLogger<ColdExportCycleRunner>.Instance);

        // When
        await runner.RunExportCycleWithBudgetAsync(CancellationToken.None);

        // Then
        Assert.Equal(4, exporter.CallCount);
    }

    [Fact]
    public async Task RunExportCycleWithBudgetAsync_should_stop_when_exporter_reports_no_events_immediately()
    {
        // Given
        StubColdEventExporter exporter = new(
        [
            ResultBox.FromValue(new ExportResult(0, [], "v1", "no_events_since_checkpoint"))
        ]);
        ColdExportCycleRunner runner = new(
            exporter,
            Options.Create(new ColdEventStoreOptions
            {
                Enabled = true,
                RunOnStartup = true,
                PullInterval = TimeSpan.FromMinutes(30)
            }),
            new StubServiceIdProvider("default"),
            NullLogger<ColdExportCycleRunner>.Instance);

        // When
        await runner.RunExportCycleWithBudgetAsync(CancellationToken.None);

        // Then
        Assert.Equal(1, exporter.CallCount);
    }

    private sealed class StubColdEventExporter : IColdEventExporter
    {
        private readonly Queue<ResultBox<ExportResult>> _results;

        public StubColdEventExporter(IEnumerable<ResultBox<ExportResult>> results)
        {
            _results = new Queue<ResultBox<ExportResult>>(results);
        }

        public int CallCount { get; private set; }

        public Task<ColdFeatureStatus> GetStatusAsync(CancellationToken ct)
            => Task.FromResult(new ColdFeatureStatus(true, true, "test"));

        public Task<ResultBox<ExportResult>> ExportIncrementalAsync(string serviceId, CancellationToken ct)
        {
            CallCount++;
            if (_results.Count == 0)
            {
                return Task.FromResult(ResultBox.FromValue(
                    new ExportResult(0, [], "done", "no_events_since_checkpoint")));
            }

            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class StubServiceIdProvider : IServiceIdProvider
    {
        private readonly string _serviceId;

        public StubServiceIdProvider(string serviceId)
        {
            _serviceId = serviceId;
        }

        public string GetCurrentServiceId() => _serviceId;
    }
}
