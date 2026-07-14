using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Configuration;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans.Streams;
namespace Sekiban.Dcb.Orleans;

/// <summary>
///     A single-silo Orleans cluster that runs inside your own process, on your own machine, with no external
///     clustering dependency — the thing that makes "use a distributed-runtime executor everywhere, even locally" a
///     practical instruction rather than a slogan.
///     Local development used to have a choice between a real cluster (needs infrastructure) and an in-memory executor
///     (needs nothing, and behaves nothing like production — no grain placement, no reentrancy, no cluster
///     coordination, no serialization of your payloads). People chose the second one, and one of them shipped it. So
///     this is the first one, made cheap: <c>UseLocalhostClustering</c>, in-memory grain storage and streams, one silo,
///     started and stopped deterministically by the host.
///     What it does NOT do is choose your event store. That stays yours to say out loud — a localhost silo over
///     Postgres is a realistic local environment, and a localhost silo over an in-memory store is a fast one that
///     forgets everything. Both are legitimate; neither should be implicit.
/// </summary>
public static class SekibanDcbLocalhostExtensions
{
    /// <summary>
    ///     Configures this silo as a single-node localhost cluster with in-memory grain storage and streams, and
    ///     registers the Sekiban DCB runtime services on it.
    ///     Call it on the silo builder of any host — a web app, a worker, or a short-lived CLI:
    ///     <code>
    ///     builder.UseOrleans(silo => silo.UseSekibanDcbLocalhost());
    ///     </code>
    ///     You still register your <c>DcbDomainTypes</c>, your <c>IEventStore</c> and your executor yourself: this
    ///     configures the runtime, not your domain and not your storage.
    ///     <b>Not for production.</b> One silo is not a cluster: it does not survive its own process. It is a real
    ///     Orleans runtime, which is the point — your grains are placed, your payloads are serialized, your projections
    ///     go through the same code path they will in production — but the cluster is a cluster of one.
    /// </summary>
    /// <param name="silo">The silo builder from <c>UseOrleans</c>.</param>
    /// <param name="streamProviderName">
    ///     The stream provider name your subscription resolver expects. Defaults to the name the Sekiban templates use.
    /// </param>
    /// <param name="safeWindowMs">
    ///     The multi-projection safe window. The default matches the templates; lower it if your local feedback loop is
    ///     more important to you than mirroring production's tolerance for out-of-order events.
    /// </param>
    public static ISiloBuilder UseSekibanDcbLocalhost(
        this ISiloBuilder silo,
        string streamProviderName = "EventStreamProvider",
        int safeWindowMs = 20_000)
    {
        ArgumentNullException.ThrowIfNull(silo);

        silo.UseLocalhostClustering()
            .Configure<ClusterOptions>(options =>
            {
                options.ClusterId = "sekiban-dcb-localhost";
                options.ServiceId = "sekiban-dcb-localhost";
            })
            .AddMemoryGrainStorageAsDefault()
            .AddMemoryGrainStorage("OrleansStorage")
            .AddMemoryGrainStorage("PubSubStore")
            .AddMemoryStreams(streamProviderName)
            .AddMemoryGrainStorage(streamProviderName);

        silo.ConfigureServices(services =>
        {
            services.TryAddSingleton<IEventSubscriptionResolver>(
                new DefaultOrleansEventSubscriptionResolver(streamProviderName, "AllEvents", Guid.Empty));
            services.TryAddSingleton<IActorObjectAccessor, OrleansActorObjectAccessor>();
            services.TryAddTransient<IMultiProjectionEventStatistics, NoOpMultiProjectionEventStatistics>();
            services.TryAddTransient(_ => new GeneralMultiProjectionActorOptions { SafeWindowMs = safeWindowMs });

            // No IBlobStorageSnapshotAccessor is registered. It is optional, and the volatile one lives in
            // Sekiban.Dcb.Core.Testing, where a runtime package has no business reaching. If you want snapshot
            // offloading locally, register the accessor you actually want — that choice is not ours to make quietly.

            services.AddSekibanDcbNativeRuntime();
        });

        return silo;
    }
}
