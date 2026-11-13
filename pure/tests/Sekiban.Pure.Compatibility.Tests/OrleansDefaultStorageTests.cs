using Orleans;
using Orleans.Runtime;
using Pure.Domain;
using Pure.Domain.Generated;
using Orleans.TestingHost;
using ResultBoxes;
using Sekiban.Pure;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Orleans;
using Sekiban.Pure.Orleans.xUnit;
using Xunit;

namespace CompatibilityDomain;

public class OrleansDefaultStorageTests
{
    [Fact]
    public async Task Default_storage_reads_aggregate_after_forced_deactivation()
    {
        var builder = new TestClusterBuilder(1);
        builder.AddSiloBuilderConfigurator<CompatibilitySiloConfigurator>();
        using var cluster = builder.Build();
        await cluster.DeployAsync();
        try
        {
            var executor = new SekibanOrleansExecutor(cluster.Client, PureDomainDomainTypes.Generate(PureDomainEventsJsonContext.Default.Options),
                new FunctionCommandMetadataProvider(() => "compatibility"));
            var response = await executor.CommandAsync(new RegisterBranch("Compatibility branch")).UnwrapBox();
            Assert.Equal(1, response.Version);

            // Await collection, including OnDeactivateAsync's snapshot write. No timer or load
            // is needed: the next call must activate the projector and read its stored snapshot.
            await cluster.Client.GetGrain<IManagementGrain>(0).ForceActivationCollection(TimeSpan.Zero);

            var aggregate = await executor.LoadAggregateAsync<BranchProjector>(response.PartitionKeys).UnwrapBox();
            Assert.Equal(1, aggregate.Version);
            Assert.Equal(new Branch("Compatibility branch"), aggregate.Payload);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
        }
    }

    // Use the published xUnit base's actual ISiloConfigurator implementation, including
    // default storage and the in-memory event repository, without copying its registrations.
    private sealed class CompatibilitySiloConfigurator : SekibanOrleansTestBase<CompatibilitySiloConfigurator>
    {
        public CompatibilitySiloConfigurator() { }
        public override SekibanDomainTypes GetDomainTypes() =>
            PureDomainDomainTypes.Generate(PureDomainEventsJsonContext.Default.Options);
    }
}
