using Dcb.Domain;
using Dcb.Domain.Student;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Testing;
using Xunit;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     The precondition for telling anyone that the in-memory executor is test-only.
///     "Use a distributed-runtime executor everywhere, even locally" is not an instruction until there is a localhost
///     composition that actually works, in the three shapes people actually run: a web app, a worker, and a
///     short-lived CLI/batch process that must start, do one thing, and exit. So each of those is built here as a REAL
///     host, really started, really used, and really stopped. If any of them cannot be made to work, the policy this
///     slice enforces would be a slogan, and these tests would say so.
///     Note what the executor reports in every one of them: <c>DistributedRuntime</c>. That is the whole point — a
///     localhost silo is a real Orleans runtime with a cluster of one, not a pretend one.
/// </summary>
[Collection("LocalhostComposition")]
public class LocalhostCompositionTests
{
    /// <summary>The registration every lifecycle shares: the domain, the store you chose, and the executor.</summary>
    private static void Register(IServiceCollection services, IEventStore eventStore)
    {
        services.AddSingleton(DomainType.GetDomainTypes());
        services.AddSingleton(eventStore);
        services.AddSingleton<ISekibanExecutor>(sp => new OrleansDcbExecutor(
            sp.GetRequiredService<IClusterClient>(),
            sp.GetRequiredService<IEventStore>(),
            sp.GetRequiredService<DcbDomainTypes>()));
        services.AddSingleton<IMultiProjectionStateStore, InMemoryMultiProjectionStateStore>();
    }

    /// <summary>
    ///     The store is passed in on purpose, and volatile here only because this is a test. A local silo over Postgres
    ///     is the same composition with a different line — which is exactly what the guide says, and why the store is
    ///     not chosen for you.
    /// </summary>
    private static InMemoryEventStore NewStore() => new(DomainType.GetDomainTypes().EventTypes);

    [Fact]
    public async Task CliOrBatch_StartsASilo_DoesOneThing_AndStopsDeterministically()
    {
        // The shape a migration script or a nightly batch job has: no server, no ports, no waiting for a signal.
        var eventStore = NewStore();
        var builder = Host.CreateApplicationBuilder();
        builder.UseOrleans(silo => silo.UseSekibanDcbLocalhost());
        Register(builder.Services, eventStore);

        using var host = builder.Build();

        await host.StartAsync();

        var executor = host.Services.GetRequiredService<ISekibanExecutor>();
        var studentId = Guid.NewGuid();
        var result = await executor.ExecuteAsync(new CreateStudent(studentId, "Batch Student", 3));

        // Graceful drain: StopAsync returns when the silo has actually shut down, so a batch process can exit knowing
        // its writes are done rather than hoping.
        await host.StopAsync();

        // GetException() throws when the box succeeded, so the failure message has to be built lazily.
        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.GetException().Message);
        Assert.Single(result.GetValue().Events);

        // The events really went to the store we chose, not to somewhere a silo invented.
        var stored = await eventStore.ReadAllSerializableEventsAsync();
        Assert.Single(stored.GetValue());
    }

    [Fact]
    public async Task CliOrBatch_ExecutorReportsDistributedRuntime_NotTestingInProcess()
    {
        // The claim the whole taxonomy rests on: local dev on a localhost silo is a REAL runtime. If this ever
        // reported TestingInProcess, "use Orleans locally" would be advice with no substance behind it.
        var builder = Host.CreateApplicationBuilder();
        builder.UseOrleans(silo => silo.UseSekibanDcbLocalhost());
        Register(builder.Services, NewStore());

        using var host = builder.Build();
        await host.StartAsync();

        var descriptor = ((IExecutorRuntimeDescriptorProvider)host.Services.GetRequiredService<ISekibanExecutor>())
            .DescribeRuntime();

        await host.StopAsync();

        Assert.Equal(ExecutorRuntimeKind.DistributedRuntime, descriptor.Runtime);
        Assert.Equal("Orleans", descriptor.RuntimeName);
    }

    [Fact]
    public async Task Worker_RunsItsBackgroundServiceAgainstTheSilo_AndDrains()
    {
        // The shape a projection worker or an outbox pump has: a BackgroundService that starts after the silo is up
        // and must be able to execute commands from inside it.
        var eventStore = NewStore();
        var worker = new CommandExecutingWorker();

        var builder = Host.CreateApplicationBuilder();
        builder.UseOrleans(silo => silo.UseSekibanDcbLocalhost());
        Register(builder.Services, eventStore);
        builder.Services.AddSingleton<IHostedService>(sp =>
        {
            worker.Executor = sp.GetRequiredService<ISekibanExecutor>();
            return worker;
        });

        using var host = builder.Build();

        await host.StartAsync();
        var executed = await worker.Executed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await host.StopAsync();

        Assert.True(executed, worker.Failure?.Message);
        Assert.True(worker.Stopped, "the worker was not given the chance to drain before the host went away");
    }

    [Fact]
    public async Task Web_ServesARequestThatExecutesACommandOnTheSilo()
    {
        // The shape the templates already ship — but built here from scratch, on a real Kestrel port, so the guide's
        // web section is a thing that was run rather than a thing that was written.
        var eventStore = NewStore();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.UseOrleans(silo => silo.UseSekibanDcbLocalhost());
        Register(builder.Services, eventStore);

        await using var app = builder.Build();

        app.MapPost(
            "/students",
            async (ISekibanExecutor executor) =>
            {
                var result = await executor.ExecuteAsync(new CreateStudent(Guid.NewGuid(), "Web Student", 3));
                return result.IsSuccess
                    ? Results.Ok(result.GetValue().Events.Count())
                    : Results.Problem(detail: result.GetException()?.Message);
            });

        await app.StartAsync();

        var address = app.Urls.First();
        using var client = new HttpClient();
        var response = await client.PostAsync($"{address}/students", null);
        var body = await response.Content.ReadAsStringAsync();

        await app.StopAsync();

        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        Assert.Equal("1", body);

        var stored = await eventStore.ReadAllSerializableEventsAsync();
        Assert.Single(stored.GetValue());
    }

    /// <summary>A worker that does what a real one does: execute a command through the executor, then drain.</summary>
    private sealed class CommandExecutingWorker : IHostedService
    {
        public TaskCompletionSource<bool> Executed { get; } = new();
        public ISekibanExecutor? Executor { get; set; }
        public Exception? Failure { get; private set; }
        public bool Stopped { get; private set; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                var result = await Executor!.ExecuteAsync(
                    new CreateStudent(Guid.NewGuid(), "Worker Student", 3),
                    cancellationToken);

                if (!result.IsSuccess)
                {
                    Failure = result.GetException();
                }

                Executed.TrySetResult(result.IsSuccess);
            }
            catch (Exception ex)
            {
                Failure = ex;
                Executed.TrySetResult(false);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Stopped = true;
            return Task.CompletedTask;
        }
    }
}
