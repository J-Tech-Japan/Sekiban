using FeatureCheck.Domain.Projections.ClientLoyaltyPointLists;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sekiban.Core.Dependency;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.MultiProjections.Projections;
using Sekiban.Core.Snapshot.BackgroundServices;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FeatureCheck.Test;

public class MultiProjectionSnapshotBackgroundServiceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(25);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenerationFailure_IsLoggedAndRetriedWithoutStoppingHost(bool unrelatedCancellation)
    {
        var failure = unrelatedCancellation
            ? (Exception)new OperationCanceledException("Storage request timed out")
            : new InvalidOperationException("Storage unavailable");
        var recovered = NewSignal();
        var attempts = 0;
        long failedAt = 0;
        long recoveredAt = 0;
        var generator = new TestGenerator(() =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                failedAt = Stopwatch.GetTimestamp();
                return Task.FromException(failure);
            }
            recoveredAt = Stopwatch.GetTimestamp();
            recovered.TrySetResult();
            return Task.CompletedTask;
        });
        var logger = new TestLogger();
        using var host = CreateHost<RetrySettings>(generator, logger);
        Assert.Equal(BackgroundServiceExceptionBehavior.StopHost,
            host.Services.GetRequiredService<IOptions<HostOptions>>().Value.BackgroundServiceExceptionBehavior);

        await host.StartAsync();
        try
        {
            await recovered.Task.WaitAsync(Timeout);
            Assert.False(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
            Assert.False(GetService<RetrySettings>(host).ExecuteTask!.IsCompleted);
            Assert.True(Stopwatch.GetElapsedTime(failedAt, recoveredAt) >= TimeSpan.FromSeconds(1));
            Assert.Contains(logger.Errors, error => ReferenceEquals(error, failure));
        }
        finally
        {
            await host.StopAsync().WaitAsync(Timeout);
        }
        Assert.True(GetService<RetrySettings>(host).ExecuteTask!.IsCompletedSuccessfully);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shutdown_DuringInitialOrRecurringDelay_CompletesNormally(bool waitForGeneration)
    {
        var generated = NewSignal();
        var attempts = 0;
        var generator = new TestGenerator(() =>
        {
            Interlocked.Increment(ref attempts);
            generated.TrySetResult();
            return Task.CompletedTask;
        });
        var logger = new TestLogger();
        using var host = CreateHost<SlowSettings>(generator, logger);
        await host.StartAsync();
        try
        {
            if (waitForGeneration) await generated.Task.WaitAsync(Timeout);
        }
        finally
        {
            await host.StopAsync().WaitAsync(Timeout);
        }
        Assert.Equal(waitForGeneration ? 1 : 0, attempts);
        Assert.True(GetService<SlowSettings>(host).ExecuteTask!.IsCompletedSuccessfully);
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public async Task Shutdown_DuringGenerationCancellation_CompletesNormallyWithoutLoggingFailure()
    {
        var started = NewSignal();
        var generation = NewSignal();
        var generator = new TestGenerator(() =>
        {
            started.TrySetResult();
            return generation.Task;
        });
        var logger = new TestLogger();
        using var host = CreateHost<SlowSettings>(generator, logger);
        await host.StartAsync();
        try
        {
            await started.Task.WaitAsync(Timeout);
        }
        finally
        {
            // StopAsync cancels the service token before awaiting the in-flight generation.
            var stop = host.StopAsync();
            generation.TrySetCanceled();
            await stop.WaitAsync(Timeout);
        }
        Assert.True(GetService<SlowSettings>(host).ExecuteTask!.IsCompletedSuccessfully);
        Assert.Empty(logger.Errors);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static IHost CreateHost<TSettings>(TestGenerator generator, TestLogger logger)
        where TSettings : IMultiProjectionsSnapshotGenerateSetting
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddSingleton<IMultiProjectionSnapshotGenerator>(generator);
        builder.Services.AddTransient<MultiProjectionCollectionGenerator>();
        builder.Services.AddLogging(logging => logging.AddProvider(logger));
        builder.Services.AddSekibanMultiProjectionSnapshotBackgroundService<TSettings>();
        return builder.Build();
    }

    private static BackgroundService GetService<TSettings>(IHost host)
        where TSettings : IMultiProjectionsSnapshotGenerateSetting =>
        host.Services.GetServices<IHostedService>()
            .OfType<MultiProjectionSnapshotCollectionBackgroundService<TSettings>>().Single();

    public class RetrySettings : MultiProjectionSnapshotGenerateSettingAbstract
    {
        public override void Define()
        {
            AddMultiProjectionsSnapshotType<ClientLoyaltyPointListProjection>();
            SetExecuteIntervalSeconds(1);
        }
    }

    public class SlowSettings : RetrySettings
    {
        public override void Define()
        {
            base.Define();
            SetExecuteIntervalSeconds(3600);
        }
    }

    private sealed class TestGenerator(Func<Task> generate) : IMultiProjectionSnapshotGenerator
    {
        public Action<int>? VersionNotification { get; set; }

        public async Task<MultiProjectionState<TPayload>> GenerateMultiProjectionSnapshotAsync<TProjection, TPayload>(
            int minimumNumberOfEventsToGenerateSnapshot,
            string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions)
            where TProjection : IMultiProjector<TPayload>, new()
            where TPayload : IMultiProjectionPayloadCommon
        {
            await generate();
            return new MultiProjectionState<TPayload>();
        }

        public Task<MultiProjectionState<TPayload>> GetCurrentStateAsync<TPayload>(string rootPartitionKey)
            where TPayload : IMultiProjectionPayloadCommon => throw new NotSupportedException();
    }

    private sealed class TestLogger : ILogger, ILoggerProvider
    {
        public ConcurrentQueue<Exception> Errors { get; } = new();
        public ILogger CreateLogger(string categoryName) => this;
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error && exception is not null) Errors.Enqueue(exception);
        }
    }
}
