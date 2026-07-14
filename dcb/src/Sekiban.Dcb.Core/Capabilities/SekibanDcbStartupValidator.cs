using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;
namespace Sekiban.Dcb.Capabilities;

/// <summary>
///     Logs what Sekiban actually resolved, and — when it was registered as a guard — refuses to let a Production host
///     start if what it resolved would lose data.
///     It runs as a hosted service, so it happens once, at startup, after every registration has had its say, and
///     before the host serves anything.
/// </summary>
public sealed class SekibanDcbStartupValidator : IHostedService
{
    private readonly Func<IServiceProvider, object?> _resolveExecutor;
    private readonly bool _enforce;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<SekibanDcbStartupValidator> _logger;
    private readonly SekibanDcbProductionGuardOptions _options;
    private readonly IServiceProvider _services;

    /// <summary>Creates the validator. <paramref name="enforce" /> false means banner only — it never fails the host.</summary>
    public SekibanDcbStartupValidator(
        IServiceProvider services,
        IHostEnvironment environment,
        SekibanDcbProductionGuardOptions options,
        Func<IServiceProvider, object?> resolveExecutor,
        bool enforce,
        ILogger<SekibanDcbStartupValidator> logger)
    {
        _services = services;
        _environment = environment;
        _options = options;
        _resolveExecutor = resolveExecutor;
        _enforce = enforce;
        _logger = logger;
    }

    /// <summary>Resolves, reports, and — if enforcing — stops a Production host that would lose data.</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Resolve inside a scope, not from the root provider. Sekiban's own Cosmos registrations are scoped, and
        // asking the root provider for a scoped service throws under ValidateScopes — so a host with a perfectly
        // supported composition could not even opt in, and the guard would fail the host it was there to protect.
        // The scope lives exactly as long as this check: nothing resolved here is kept.
        using var scope = _services.CreateScope();

        var report = SekibanDcbCapabilityResolver.Resolve(
            scope.ServiceProvider,
            _environment,
            _options,
            _resolveExecutor);

        LogBanner(report);

        if (_enforce)
        {
            Enforce(report);
        }

        return Task.CompletedTask;
    }

    /// <summary>Nothing to stop: the validator does its whole job at startup.</summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Enforce(SekibanDcbCapabilityReport report)
    {
        if (!report.IsProductionEnvironment)
        {
            return;
        }

        var faults = new List<string>();

        if (report.HasTestingOrUnknownExecutor)
        {
            // Deliberately not overridable. See SekibanDcbProductionGuardOptions.
            faults.Add(
                $"the executor is {report.Executor} — Production requires a distributed runtime. "
                + $"Resolved type: {report.ExecutorTypeName ?? "(none)"}. There is no override for this: an "
                + "in-process testing executor in Production is not a configuration, it is an accident.");
        }

        if (report.HasVolatileStorage && !_options.AllowVolatileStorageInProduction)
        {
            faults.Add(
                $"storage is volatile — event store: {report.EventStore}, projection state store: "
                + $"{report.ProjectionStore}. Everything written would be lost when this process ends. If that is "
                + $"genuinely what you want, set {nameof(SekibanDcbProductionGuardOptions.AllowVolatileStorageInProduction)} "
                + "= true; it authorises storage only, and will not authorise a testing executor.");
        }

        if (report.HasUnknownStorage)
        {
            // NOT covered by AllowVolatileStorageInProduction, on purpose. That override says "I looked at a store
            // that declared itself volatile and I meant it". An unidentified store has declared nothing, so there is
            // nothing for an operator to have meant. Making it durable, or making it say so, is the way past this.
            faults.Add(
                $"storage would not identify itself — event store: {report.EventStore}, projection state store: "
                + $"{report.ProjectionStore}. Silence is not a promise of durability. "
                + $"{nameof(SekibanDcbProductionGuardOptions.AllowVolatileStorageInProduction)} does not authorise "
                + "this: it authorises a store that declared itself Volatile, not one that declared nothing. Have the "
                + $"store implement {nameof(IStorageDurabilityDescriptorProvider)} so it can say what it is.");
        }

        if (faults.Count == 0)
        {
            return;
        }

        var message = new StringBuilder()
            .AppendLine(
                $"Sekiban DCB production guard refused to start the host in environment '{report.EnvironmentName}':")
            .AppendJoin(Environment.NewLine, faults.Select(f => $"  - {f}"))
            .ToString();

        _logger.LogCritical(
            "Sekiban DCB production guard FAILED. Environment={Environment} Executor={Executor} EventStore={EventStore} ProjectionStore={ProjectionStore}",
            report.EnvironmentName,
            report.Executor.ToString(),
            report.EventStore.ToString(),
            report.ProjectionStore.ToString());

        throw new SekibanDcbProductionGuardException(message, report);
    }

    private void LogBanner(SekibanDcbCapabilityReport report)
    {
        // Structured, so it can be queried; and no connection strings, ever — a banner that leaks a secret into every
        // log sink is a worse bug than the one it exists to prevent.
        _logger.LogInformation(
            "Sekiban DCB startup. Environment={Environment} IsProduction={IsProduction} ExecutorType={ExecutorType} ExecutorRuntime={ExecutorRuntime} ExecutorRuntimeName={ExecutorRuntimeName} EventStoreProvider={EventStoreProvider} EventStoreDurability={EventStoreDurability} ProjectionStoreProvider={ProjectionStoreProvider} ProjectionStoreDurability={ProjectionStoreDurability} Overrides={Overrides} Enforcing={Enforcing}",
            report.EnvironmentName,
            report.IsProductionEnvironment,
            report.ExecutorTypeName ?? "(none)",
            report.Executor.Runtime,
            report.Executor.RuntimeName,
            report.EventStore.ProviderName,
            report.EventStore.Durability,
            report.ProjectionStore.ProviderName,
            report.ProjectionStore.Durability,
            report.UsedOverrideNames.Count == 0 ? "(none)" : string.Join(",", report.UsedOverrideNames),
            _enforce);

        if (report.UsedOverrideNames.Count > 0)
        {
            _logger.LogWarning(
                "Sekiban DCB safety overrides are ON: {Overrides}. They are named in the log so that nobody has to read the deployment to find out.",
                string.Join(",", report.UsedOverrideNames));
        }

        if (report.HasVolatileOrUnknownStorage)
        {
            _logger.LogWarning(
                "Sekiban DCB DATA LOSS WARNING: storage is not durable (event store: {EventStore}, projection state store: {ProjectionStore}). Everything written is lost when this process ends. This is correct for tests and for local development, and is data loss anywhere else.",
                report.EventStore.ToString(),
                report.ProjectionStore.ToString());
        }

        if (report.HasTestingOrUnknownExecutor)
        {
            _logger.LogWarning(
                "Sekiban DCB executor is {Executor} — not a distributed runtime. Commands run in this process only. This is correct for tests, and is not a production configuration.",
                report.Executor.ToString());
        }
    }
}
