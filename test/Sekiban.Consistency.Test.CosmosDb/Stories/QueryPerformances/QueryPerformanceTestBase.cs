using Customer.Domain.Aggregates.Branches;
using Customer.Domain.Aggregates.Branches.Commands;
using Customer.Domain.Aggregates.Clients;
using Customer.Domain.Aggregates.Clients.Commands;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Dependency;
using Sekiban.Core.Document;
using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Core.Query.SingleAggregate;
using Sekiban.Infrastructure.Cosmos;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
namespace SampleProjectStoryXTest.Stories.QueryPerformances;

public abstract class QueryPerformanceTestBase : TestBase
{
    protected readonly IAggregateCommandExecutor _aggregateCommandExecutor;
    protected readonly ISingleAggregateService _aggregateService;
    protected readonly CosmosDbFactory _cosmosDbFactory;
    protected readonly IDocumentPersistentRepository _documentPersistentRepository;
    protected readonly HybridStoreManager _hybridStoreManager;
    protected readonly InMemoryDocumentStore _inMemoryDocumentStore;
    protected readonly IMultipleAggregateProjectionService _multipleAggregateProjectionService;
    protected readonly ITestOutputHelper _testOutputHelper;
    protected QueryPerformanceTestBase(
        SekibanTestFixture sekibanTestFixture,
        ITestOutputHelper testOutputHelper,
        ServiceCollectionExtensions.MultipleProjectionType multipleProjectionType) : base(sekibanTestFixture, false, multipleProjectionType)
    {
        _testOutputHelper = testOutputHelper;
        _cosmosDbFactory = GetService<CosmosDbFactory>();
        _aggregateCommandExecutor = GetService<IAggregateCommandExecutor>();
        _aggregateService = GetService<ISingleAggregateService>();
        _documentPersistentRepository = GetService<IDocumentPersistentRepository>();
        _inMemoryDocumentStore = GetService<InMemoryDocumentStore>();
        _hybridStoreManager = GetService<HybridStoreManager>();
        _multipleAggregateProjectionService = GetService<IMultipleAggregateProjectionService>();
    }
    [Fact]
    public void TestQuery1()
    {
        // 先に全データを削除する
        _cosmosDbFactory.DeleteAllFromAggregateEventContainer(AggregateContainerGroup.Default).Wait();
        _cosmosDbFactory.DeleteAllFromAggregateEventContainer(AggregateContainerGroup.Dissolvable).Wait();
        _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.AggregateCommand, AggregateContainerGroup.Dissolvable).Wait();
        _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.AggregateCommand).Wait();
    }

    [Theory]
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
            var branchList = await _multipleAggregateProjectionService.GetAggregateList<Branch>();
            _testOutputHelper.WriteLine($"create branch {branchList.Count}");

            var firstcount = branchList.Count;
            var (branchResult, events)
                = await _aggregateCommandExecutor.ExecCreateCommandAsync<Branch, CreateBranch>(new CreateBranch($"Branch {i}"));
            var aggregateCommandDocument = branchResult.CommandId;
            if (aggregateCommandDocument == null)
            {
                continue;
            }
            var branchId = branchResult.AggregateId;
            Assert.NotNull(branchResult);
            Assert.NotNull(branchResult.AggregateId);
            branchList = await _multipleAggregateProjectionService.GetAggregateList<Branch>();
            _testOutputHelper.WriteLine($"branch created {branchList.Count}");
            Assert.Equal(firstcount + 1, branchList.Count);
            var branchFromList = branchList.First(m => m.AggregateId == branchId);
            Assert.NotNull(branchFromList);
            for (var j = 0; j < numberOfClient; j++)
            {
                var clientList = await _multipleAggregateProjectionService.GetAggregateList<Client>();
                _testOutputHelper.WriteLine($"create client {clientList.Count}");
                var firstClientCount = clientList.Count;
                var (clientCreateResult, events2) = await _aggregateCommandExecutor.ExecCreateCommandAsync<Client, CreateClient>(
                    new CreateClient(branchId!.Value, $"clientname {i}-{j}", $"test{i}.{j}.{id}@example.com"));
                clientList = await _multipleAggregateProjectionService.GetAggregateList<Client>();
                _testOutputHelper.WriteLine($"client created {clientList.Count}");
                Assert.Equal(firstClientCount + 1, clientList.Count);
                for (var k = 0; k < changeNameCount; k++)
                {
                    _testOutputHelper.WriteLine($"client change name {k + 1}");
                    var aggregate = await _aggregateService.GetAggregateStateAsync<Client>(clientCreateResult.AggregateId!.Value);
                    _testOutputHelper.WriteLine($"aggregate.version = {aggregate?.Version}");
                    await _aggregateCommandExecutor.ExecChangeCommandAsync<Client, ChangeClientName>(
                        new ChangeClientName(clientCreateResult.AggregateId!.Value, $"change{i}-{j}-{k}")
                        {
                            ReferenceVersion = aggregate?.Version ?? 0
                        });
                    clientList = await _multipleAggregateProjectionService.GetAggregateList<Client>();
                    _testOutputHelper.WriteLine($"client name changed {k + 1} - {clientList.Count}");
                    Assert.Equal(firstClientCount + 1, clientList.Count);
                }
            }
        }
    }
}