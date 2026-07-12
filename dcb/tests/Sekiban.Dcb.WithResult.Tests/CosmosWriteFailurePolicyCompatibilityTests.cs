using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb.CosmosDb;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     Upgrade compatibility: a consumer that takes this package's new version without opting in must keep
///     the behavior it deployed. These reproduce the construction patterns known downstream repos actually
///     use (SekibanAsAService, SekibanWasmRuntime) rather than testing the defaults in the abstract.
/// </summary>
public class CosmosWriteFailurePolicyCompatibilityTests
{
    [Fact]
    public void Manually_Constructed_Options_Should_Keep_Pre_Upgrade_Behavior()
    {
        // Exactly how downstream repos construct the options today: container names, nothing else.
        var options = new CosmosDbEventStoreOptions
        {
            EventsContainerName = "events",
            TagsContainerName = "tags"
        };

        Assert.Equal(CosmosWriteFailurePolicy.Compatible, options.WriteFailurePolicy);
#pragma warning disable CS0618 // Asserting the obsolete option's default is the point of this test.
        Assert.True(options.TryRollbackOnFailure);
#pragma warning restore CS0618
    }

    [Fact]
    public void AddSekibanDcbCosmosDb_Without_Opt_In_Should_Keep_Pre_Upgrade_Behavior()
    {
        var services = new ServiceCollection();
        services.AddSekibanDcbCosmosDb("AccountEndpoint=https://localhost:8081/;AccountKey=key==", "testdb");

        var options = services.BuildServiceProvider().GetRequiredService<CosmosDbEventStoreOptions>();

        Assert.Equal(CosmosWriteFailurePolicy.Compatible, options.WriteFailurePolicy);
#pragma warning disable CS0618
        Assert.True(options.TryRollbackOnFailure);
#pragma warning restore CS0618
    }

    [Fact]
    public void AddSekibanDcbCosmosDb_Should_Let_A_Consumer_Opt_Into_Roll_Forward()
    {
        var services = new ServiceCollection();
        services.AddSekibanDcbCosmosDb(
            "AccountEndpoint=https://localhost:8081/;AccountKey=key==",
            "testdb",
            options => options.WriteFailurePolicy = CosmosWriteFailurePolicy.RollForward);

        var options = services.BuildServiceProvider().GetRequiredService<CosmosDbEventStoreOptions>();

        Assert.Equal(CosmosWriteFailurePolicy.RollForward, options.WriteFailurePolicy);
    }

    [Fact]
    public void Retry_Options_Should_Not_Apply_Under_The_Compatible_Default()
    {
        // The retry knobs exist on a fresh options instance, but the compatible policy never consults them,
        // so their presence cannot change an existing deployment's behavior.
        var options = new CosmosDbEventStoreOptions();

        Assert.Equal(CosmosWriteFailurePolicy.Compatible, options.WriteFailurePolicy);
        Assert.NotNull(options.TagWriteRetry);
        Assert.Equal(5, options.TagWriteRetry.MaxAttempts);
    }

    [Fact]
    public void CreateForCompatibility_Should_Keep_The_Compatible_Write_Failure_Policy()
    {
        var options = CosmosDbEventStoreOptions.CreateForCompatibility();

        Assert.Equal(CosmosWriteFailurePolicy.Compatible, options.WriteFailurePolicy);
    }
}
