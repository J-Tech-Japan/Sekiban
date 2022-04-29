using CosmosInfrastructure;
using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Aggregates.Branches.Commands;
using CustomerDomainContext.Aggregates.Clients;
using CustomerDomainContext.Aggregates.Clients.Commands;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.AggregateEvents;
using Sekiban.EventSourcing.Documents;
using Sekiban.EventSourcing.Queries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
namespace SampleProjectStoryXTest.Stories;

public class CustomerDbStoryBasic : TestBase
{
    private readonly AggregateCommandExecutor _aggregateCommandExecutor;
    private readonly SingleAggregateService _aggregateService;
    private readonly CosmosDbFactory _cosmosDbFactory;
    public CustomerDbStoryBasic(TestFixture testFixture) : base(testFixture)
    {
        _cosmosDbFactory = GetService<CosmosDbFactory>();
        _aggregateCommandExecutor = GetService<AggregateCommandExecutor>();
        _aggregateService = GetService<SingleAggregateService>();
    }
    [Fact(DisplayName = "CosmosDb ストーリーテスト 集約の機能のテストではなく、CosmosDbと連携して正しく動くかをテストしています。")]
    public async Task CosmosDbStory()
    {
        // 先に全データを削除する
        await _cosmosDbFactory.CosmosActionAsync<IEnumerable<AggregateEvent>?>(
            DocumentType.AggregateEvent,
            async container =>
            {
                var query = container.GetItemLinqQueryable<Document>()
                    .Where(
                        b => true);
                var feedIterator = container.GetItemQueryIterator<dynamic>(
                    query.ToQueryDefinition(),
                    null,
                    null);
                var todelete = new List<Document>(); 
                while (feedIterator.HasMoreResults)
                {
                    var response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        if (item == null) {continue;}
                        if (item is not JObject jobj) { continue; }
                        todelete.Add(jobj.ToObject<Document>() ?? throw new Exception());
                    }
                }
                foreach (var d in todelete)
                {
                    await container.DeleteItemAsync<Document>(d.Id.ToString(), new PartitionKey(d.PartitionKey));
                }
                return null;
            });


        // create list branch
        var branchList = (await _aggregateService.DtoListAsync<Branch, BranchDto>()).ToList();
        Assert.Empty(branchList);
        var branchResult = await _aggregateCommandExecutor.ExecCreateCommandAsync<Branch, BranchDto, CreateBranch>(new CreateBranch("Japan"));
        Assert.NotNull(branchResult);
        Assert.NotNull(branchResult.AggregateDto);
        var branchId = branchResult.AggregateDto!.AggregateId;
        branchList = (await _aggregateService.DtoListAsync<Branch, BranchDto>()).ToList();
        Assert.Single(branchList);
        var branchFromList =
            branchList.First(m => m.AggregateId == branchId);
        Assert.NotNull(branchFromList);

        // create client
        var clientList =  (await _aggregateService.DtoListAsync<Client, ClientDto>()).ToList();
        Assert.Empty(clientList);
        var createClientResult =
            await _aggregateCommandExecutor.ExecCreateCommandAsync<Client, ClientDto, CreateClient>(
                new CreateClient(branchId, "Tanaka Taro", "tanaka@example.com"));
        Assert.NotNull(createClientResult);
        Assert.NotNull(createClientResult.AggregateDto);
        var clientId = createClientResult.AggregateDto!.AggregateId;
        clientList =  (await _aggregateService.DtoListAsync<Client, ClientDto>()).ToList();
        Assert.Single(clientList);
        
    }
}
