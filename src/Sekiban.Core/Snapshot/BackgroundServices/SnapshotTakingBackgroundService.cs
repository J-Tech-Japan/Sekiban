using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sekiban.Core.Events;
using System.Collections.Concurrent;
namespace Sekiban.Core.Snapshot.BackgroundServices;

public class SnapshotTakingBackgroundService : BackgroundService
{
    private readonly BlockingCollection<IEvent> _eventQueue = new();
    public IServiceProvider? ServiceProvider { get; set; } = null;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_eventQueue.TryTake(out var ev))
            {
                if (ServiceProvider is null) { return; }
                using var scope = ServiceProvider.CreateScope();
                var snapshotGenerator = scope.ServiceProvider.GetService<SnapshotGenerator>();
                var logger = scope.ServiceProvider.GetService<ILogger<SnapshotTakingBackgroundService>>();
                if (snapshotGenerator is null) { continue; }
                try
                {
                    await snapshotGenerator.Generate(ev);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Snapshot Generator Error");
                }
            } else
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }

    public void EnqueueTask(IEvent ev)
    {
        _eventQueue.Add(ev);
    }
}
