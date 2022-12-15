using FeatureCheck.Domain.Aggregates.Branches;
using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Aggregates.Clients;
using FeatureCheck.Domain.Aggregates.Clients.Commands;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Dependency;
using Sekiban.Core.Document;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Infrastructure.Cosmos;
using Sekiban.Testing.Shared;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
namespace SampleProjectStoryXTest.Stories.QueryPerformances;

public abstract class QueryPerformanceTestBase : TestBase
{
    protected readonly CosmosDbFactory _cosmosDbFactory;
    protected readonly IDocumentPersistentRepository _documentPersistentRepository;
    protected readonly HybridStoreManager _hybridStoreManager;
    protected readonly InMemoryDocumentStore _inMemoryDocumentStore;
    protected readonly ITestOutputHelper _testOutputHelper;
    protected readonly ICommandExecutor CommandExecutor;
    protected readonly IMultiProjectionService MultiProjectionService;
    protected readonly IAggregateLoader ProjectionService;

    protected QueryPerformanceTestBase(
        SekibanTestFixture sekibanTestFixture,
        ITestOutputHelper testOutputHelper,
        ServiceCollectionExtensions.MultiProjectionType multiProjectionType) : base(
        sekibanTestFixture,
        false,
        multiProjectionType)
    {
        _testOutputHelper = testOutputHelper;
        _cosmosDbFactory = GetService<CosmosDbFactory>();
        CommandExecutor = GetService<ICommandExecutor>();
        ProjectionService = GetService<IAggregateLoader>();
        _documentPersistentRepository = GetService<IDocumentPersistentRepository>();
        _inMemoryDocumentStore = GetService<InMemoryDocumentStore>();
        _hybridStoreManager = GetService<HybridStoreManager>();
        MultiProjectionService = GetService<IMultiProjectionService>();
    }

    [Fact]
    [Trait(SekibanTestConstants.Category, SekibanTestConstants.Categories.Performance)]
    public void TestQuery1()
    {
        // 先に全データを削除する
        _cosmosDbFactory.DeleteAllFromEventContainer(AggregateContainerGroup.Default).Wait();
        _cosmosDbFactory.DeleteAllFromEventContainer(AggregateContainerGroup.Dissolvable).Wait();
        _cosmosDbFactory
            .DeleteAllFromAggregateFromContainerIncludes(DocumentType.Command, AggregateContainerGroup.Dissolvable)
            .Wait();
        _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.Command).Wait();
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
    public async Task TestQuery2(int numberOfBranch, int numberOfClient, int changeNameCount, int id)
    {
        for (var i = 0; i < numberOfBranch; i++)
        {
            // create list branch
            var branchList = await MultiProjectionService.GetAggregateList<Branch>();
            _testOutputHelper.WriteLine($"create branch {branchList.Count}");

            var firstcount = branchList.Count;
            var branchResult
                = await CommandExecutor.ExecCommandAsync(
                    new CreateBranch($"CreateBranch {i}"));
            var commandDocument = branchResult.CommandId;
            if (commandDocument == null)
            {
                continue;
            }
            var branchId = branchResult.AggregateId;
            Assert.NotNull(branchResult);
            Assert.NotNull(branchResult.AggregateId);
            branchList = await MultiProjectionService.GetAggregateList<Branch>();
            _testOutputHelper.WriteLine($"branch created {branchList.Count}");
            Assert.Equal(firstcount + 1, branchList.Count);
            var branchFromList = branchList.First(m => m.AggregateId == branchId);
            Assert.NotNull(branchFromList);
            for (var j = 0; j < numberOfClient; j++)
            {
                var clientList = await MultiProjectionService.GetAggregateList<Client>();
                _testOutputHelper.WriteLine($"create client {clientList.Count}");
                var firstClientCount = clientList.Count;
                var clientCreateResult =
                    await CommandExecutor.ExecCommandAsync(
                        new CreateClient(
                            branchId!.Value,
                            $"clientname {i}-{j}",
                            $"test{i}.{j}.{id}@example.com"));
                clientList = await MultiProjectionService.GetAggregateList<Client>();
                _testOutputHelper.WriteLine($"client created {clientList.Count}");
                Assert.Equal(firstClientCount + 1, clientList.Count);
                for (var k = 0; k < changeNameCount; k++)
                {
                    _testOutputHelper.WriteLine($"client change name {k + 1}");
                    var aggregate =
                        await ProjectionService.AsDefaultStateAsync<Client>(clientCreateResult.AggregateId!.Value);
                    _testOutputHelper.WriteLine($"aggregate.version = {aggregate?.Version}");
                    await CommandExecutor.ExecCommandAsync(
                        new ChangeClientName(clientCreateResult.AggregateId!.Value, $"change{i}-{j}-{k}")
                        {
                            ReferenceVersion = aggregate?.Version ?? 0
                        });
                    clientList = await MultiProjectionService.GetAggregateList<Client>();
                    _testOutputHelper.WriteLine($"client name changed {k + 1} - {clientList.Count}");
                    Assert.Equal(firstClientCount + 1, clientList.Count);
                }
            }
        }
    }
}
