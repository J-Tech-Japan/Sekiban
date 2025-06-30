using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Aggregates.Clients.Commands;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Commands;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Consts;
using FeatureCheck.Domain.Shared;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Shared;
using Sekiban.Testing.Story;
using Xunit.Abstractions;

namespace Sekiban.Test.Abstructs.Abstructs;

public abstract class CommandOrderSpec(
    TestBase<FeatureCheckDependency>.SekibanTestFixture sekibanTestFixture,
    ITestOutputHelper output,
    ISekibanServiceProviderGenerator providerGenerator)
    : TestBase<FeatureCheckDependency>(sekibanTestFixture, output, providerGenerator)
{
    [Fact]
    public async Task CommandOrderTest()
    {
        RemoveAllFromDefaultAndDissolvable();

        var branchResponse = await commandExecutor.ExecCommandAsync(new CreateBranch("Test"));
        var branchId = branchResponse.AggregateId!.Value;
        var clientResponse
            = await commandExecutor.ExecCommandAsync(new CreateClient(branchId, "Test Name", "test@example.com"));
        var clientId = clientResponse.AggregateId!.Value;

        var refVersion = clientResponse.Version;
        foreach (var i in Enumerable.Range(1, 100))
        {
            sekibanTestFixture.TestOutputHelper?.WriteLine($"i:{i}");
            var response = await commandExecutor.ExecCommandAsync(
                new AddLoyaltyPoint(
                    clientId,
                    DateTime.UtcNow,
                    LoyaltyPointReceiveTypeKeys.CreditcardUsage,
                    i * 100,
                    $"test{i}")
                { ReferenceVersion = refVersion });
            refVersion = response.Version;
        }

        var repository = serviceProvider.GetRequiredService<IDocumentRepository>();


        // check if all command order is correct
        var allCommands = new List<CommandDocumentForJsonExport>();
        await repository.GetAllCommandStringsForAggregateIdAsync(
            clientId,
            typeof(LoyaltyPoint),
            null,
            IDocument.DefaultRootPartitionKey,
            (commands) => allCommands.AddRange(commands
                .Select(x => SekibanJsonHelper.Deserialize<CommandDocumentForJsonExport>(x)!))
        );

        Assert.Equal(101, allCommands.Count);

        var prevSortableUniqueId = new SortableUniqueIdValue(allCommands[0].SortableUniqueId);
        foreach (var command in allCommands[1..])
        {
            var sortableUniqueId = new SortableUniqueIdValue(command.SortableUniqueId);
            Assert.True(sortableUniqueId.IsLaterThanOrEqual(prevSortableUniqueId));
            prevSortableUniqueId = sortableUniqueId;
        }


        // check if `sinceSortableUniqueId` boundary is correct
        var sortableUniqueIdLowerBound = new SortableUniqueIdValue(allCommands[0].SortableUniqueId);
        allCommands = [];
        await repository.GetAllCommandStringsForAggregateIdAsync(
            clientId,
            typeof(LoyaltyPoint),
            sortableUniqueIdLowerBound,
            IDocument.DefaultRootPartitionKey,
            (commands) => allCommands.AddRange(commands
                .Select(x => SekibanJsonHelper.Deserialize<CommandDocumentForJsonExport>(x)!))
        );

        Assert.Equal(100, allCommands.Count);
    }
}
