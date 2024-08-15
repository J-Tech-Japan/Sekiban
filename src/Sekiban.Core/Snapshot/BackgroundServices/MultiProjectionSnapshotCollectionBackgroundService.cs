using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sekiban.Core.Shared;
namespace Sekiban.Core.Snapshot.BackgroundServices;

/// <summary>
///     Background service for generating snapshots of multi-projection.
///     This class can be used as a background service. Or use AddSekibanMultiProjectionSnapshotBackgroundService
///     extension method.
/// </summary>
/// <typeparam name="TSettings"></typeparam>
// ReSharper disable once ClassNeverInstantiated.Global
public class MultiProjectionSnapshotCollectionBackgroundService<TSettings> : BackgroundService
    where TSettings : IMultiProjectionsSnapshotGenerateSetting
{
    private readonly IServiceProvider _services;

    public MultiProjectionSnapshotCollectionBackgroundService(IServiceProvider services) => _services = services;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = _services.CreateScope();
        var logger = scope.ServiceProvider
            .GetService<ILogger<MultiProjectionSnapshotCollectionBackgroundService<TSettings>>>();
        var dateUtil = scope.ServiceProvider.GetService<ISekibanDateProducer>();
        var configuration = scope.ServiceProvider.GetService<IConfiguration>();
        if (configuration is null) { return; }
        logger?.LogInformation("Starting Background Task - MultiProjectionSnapshotCollectionBackgroundService");

        var multiProjectionCollectionGenerator = scope.ServiceProvider.GetService<MultiProjectionCollectionGenerator>();
        if (multiProjectionCollectionGenerator is null) { return; }

        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); // 10秒待機

        while (!stoppingToken.IsCancellationRequested)
        {
            logger?.LogInformation(
                "Starting to Make Snapshots... UTCTime:{DateTime}",
                dateUtil?.UtcNow.ToString("yyyy/MM/dd HH:mm:ss"));

            var setting = GetSetting(configuration);
            if (setting is null) { break; }
            await multiProjectionCollectionGenerator.GenerateAsync(setting);

            logger?.LogInformation(
                "End Making Snapshots UTCTime:{DateTime}",
                dateUtil?.UtcNow.ToString("yyyy/MM/dd HH:mm:ss"));

            logger?.LogInformation(
                "Waiting for next execution {Seconds} Seconds...",
                setting.GetExecuteIntervalSeconds());

            await Task.Delay(TimeSpan.FromSeconds(setting.GetExecuteIntervalSeconds()), stoppingToken);

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
        logger?.LogInformation("Ending Background Task - MultiProjectionSnapshotCollectionBackgroundService");
    }

    public static IMultiProjectionsSnapshotGenerateSetting? GetSetting(IConfiguration configuration)
    {
        var type = typeof(TSettings);

        var constructors = type.GetConstructors();
        foreach (var constructor in constructors)
        {
            if (constructor.GetParameters().Length == 0)
            {
                var instance = Activator.CreateInstance<TSettings>();
                if (instance is IMultiProjectionsSnapshotGenerateSetting setting)
                {
                    return setting;
                }
            }
            if (constructor.GetParameters().Length == 1)
            {
                var parameterType = constructor.GetParameters().First().ParameterType;
                if (parameterType == typeof(IConfiguration))
                {
                    var instance = Activator.CreateInstance(type, configuration);
                    if (instance is IMultiProjectionsSnapshotGenerateSetting setting)
                    {
                        return setting;
                    }
                }
            }
        }
        return null;
    }
}
