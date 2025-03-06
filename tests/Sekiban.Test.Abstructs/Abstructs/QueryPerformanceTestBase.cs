using FeatureCheck.Domain.Aggregates.Branches;
using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Aggregates.Clients;
using FeatureCheck.Domain.Aggregates.Clients.Commands;
using FeatureCheck.Domain.Shared;
using Sekiban.Testing.Shared;
using Sekiban.Testing.Story;
using Xunit.Abstractions;
namespace Sekiban.Test.Abstructs.Abstructs;

public abstract class QueryPerformanceTestBase : TestBase<FeatureCheckDependency>
{
    protected QueryPerformanceTestBase(
        SekibanTestFixture sekibanTestFixture,
        ITestOutputHelper testOutputHelper,
        ISekibanServiceProviderGenerator providerGenerator) : base(
        sekibanTestFixture,
        testOutputHelper,
        providerGenerator)
    {
    }

    [Fact]
    [Trait(SekibanTestConstants.Category, SekibanTestConstants.Categories.Performance)]
    public void TestQuery1()
    {
        RemoveAllFromDefaultAndDissolvable();
    }

    [Theory]
    [Trait(SekibanTestConstants.Category, SekibanTestConstants.Categories.Performance)]
    [InlineData(3, 3, 3, 1)]
    [InlineData(3, 3, 3, 2)]
    [InlineData(3, 3, 3, 3)]
    [InlineData(3, 3, 3, 4)]
    [InlineData(3, 3, 3, 5)]
    [InlineData(3, 3, 3, 6)]
    [InlineData(3, 3, 3, 7)]
    [InlineData(3, 3, 3, 8)]
    [InlineData(3, 3, 3, 9)]
    [InlineData(3, 3, 3, 10)]
    [InlineData(1, 1, 100, 11)]
    [InlineData(1, 1, 100, 12)]
    [InlineData(1, 1, 100, 13)]
    [InlineData(1, 1, 100, 14)]
    [InlineData(1, 1, 100, 15)]
    [InlineData(1, 1, 100, 16)]
    [InlineData(1, 1, 100, 17)]
    [InlineData(1, 1, 100, 18)]
    [InlineData(1, 1, 100, 19)]
    [InlineData(10, 10, 10, 20)]
    public async Task TestQuery2(int numberOfBranch, int numberOfClient, int changeNameCount, int id)
    {
        for (var i = 0; i < numberOfBranch; i++)
        {
            // create list branch
            var branchList = await multiProjectionService.GetAggregateList<Branch>();
            TestOutputHelper.WriteLine($"create branch {branchList.Count}");

            var firstcount = branchList.Count;
            var branchResult = await commandExecutor.ExecCommandAsync(new CreateBranch($"CreateBranch {i}"));
            var commandDocument = branchResult.CommandId;
            if (commandDocument == null)
            {
                continue;
            }
            var branchId = branchResult.AggregateId;
            Assert.NotNull(branchResult);
            Assert.NotNull(branchResult.AggregateId);
            branchList = await multiProjectionService.GetAggregateList<Branch>();
            TestOutputHelper.WriteLine($"branch created {branchList.Count}");
            Assert.Equal(firstcount + 1, branchList.Count);
            var branchFromList = branchList.First(m => m.AggregateId == branchId);
            Assert.NotNull(branchFromList);
            for (var j = 0; j < numberOfClient; j++)
            {
                var clientList = await multiProjectionService.GetAggregateList<Client>();
                TestOutputHelper.WriteLine($"create client {clientList.Count}");
                var firstClientCount = clientList.Count;
                var clientCreateResult = await commandExecutor.ExecCommandAsync(
                    new CreateClient(branchId!.Value, $"clientname {i}-{j}", $"test{i}.{j}.{id}@example.com"));
                clientList = await multiProjectionService.GetAggregateList<Client>();
                TestOutputHelper.WriteLine($"client created {clientList.Count}");
                Assert.Equal(firstClientCount + 1, clientList.Count);
                for (var k = 0; k < changeNameCount; k++)
                {
                    TestOutputHelper.WriteLine($"client change name {k + 1}");
                    var aggregate
                        = await aggregateLoader.AsDefaultStateAsync<Client>(clientCreateResult.AggregateId!.Value);
                    TestOutputHelper.WriteLine($"aggregate.version = {aggregate?.Version}");
                    await commandExecutor.ExecCommandAsync(
                        new ChangeClientName(clientCreateResult.AggregateId!.Value, $"change{i}-{j}-{k}")
                        {
                            ReferenceVersion = aggregate?.Version ?? 0
                        });
                    clientList = await multiProjectionService.GetAggregateList<Client>();
                    TestOutputHelper.WriteLine($"client name changed {k + 1} - {clientList.Count}");
                    Assert.Equal(firstClientCount + 1, clientList.Count);
                }
            }
        }
    }
}
