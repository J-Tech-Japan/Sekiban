using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Logging;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Events;
using Sekiban.Core.Setting;
using Sekiban.Core.Shared;
using Sekiban.Infrastructure.Cosmos;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Convert011To012;

// ReSharper disable StringCompareToIsCultureSpecific
public class EventsConverter
{
    private const string SourceDatabase = "Default";
    private const string ConvertDestination = "ConvertDestination";
    private readonly CosmosDbFactory _cosmosDbFactory;
    private readonly ILogger<EventsConverter> _logger;
    private readonly ISekibanContext _sekibanContext;
    public EventsConverter(ISekibanContext sekibanContext, CosmosDbFactory cosmosDbFactory, ILogger<EventsConverter> logger)
    {
        _sekibanContext = sekibanContext;
        _cosmosDbFactory = cosmosDbFactory;
        _logger = logger;
    }


    public async Task<int> StartConvert(AggregateContainerGroup containerGroup)
    {
        var count = 0;
        await _sekibanContext.SekibanActionAsync(
            SourceDatabase,
            async () =>
            {
                await Task.CompletedTask;

                var last = await _sekibanContext.SekibanActionAsync(
                    ConvertDestination,
                    async () => await GetLastSortableUniqueIdAsync(containerGroup));

                await GetLastSortableUniqueIdAsync(containerGroup);


                await _cosmosDbFactory.CosmosActionAsync(
                    DocumentType.Event,
                    containerGroup,
                    async container =>
                    {
                        await Task.CompletedTask;
                        var options = new QueryRequestOptions { MaxConcurrency = -1, MaxItemCount = -1, MaxBufferedItemCount = -1 };
                        var query = container.GetItemLinqQueryable<IEvent>().AsQueryable();
                        if (last is not null)
                        {
                            query = query.Where(m => m.SortableUniqueId.CompareTo(last.Value) > 0);
                        }
                        query = query.OrderBy(m => m.SortableUniqueId);
                        var feedIterator = container.GetItemQueryIterator<dynamic>(query.ToQueryDefinition(), null, options);
                        var tasks = new List<Task>();

                        while (feedIterator.HasMoreResults)
                        {
                            var response = await feedIterator.ReadNextAsync();
                            foreach (var item in response)
                            {
                                var sortableUniqueId = SekibanJsonHelper.GetValue<string>(item, nameof(IDocument.SortableUniqueId));
                                // pick out one item
                                if (sortableUniqueId == null) { continue; }

                                var jsonElement = (JsonElement)item;
                                var jsonDocument = JsonNode.Parse(jsonElement.GetRawText());
                                jsonDocument!["RootPartitionKey"] = "default";
                                jsonDocument!["PartitionKey"] = jsonDocument!["RootPartitionKey"] +
                                    "_" +
                                    jsonDocument!["AggregateType"] +
                                    "_" +
                                    jsonDocument!["AggregateId"];
                                tasks.Add(
                                    _sekibanContext.SekibanActionAsync(
                                        ConvertDestination,
                                        async () =>
                                        {
                                            await _cosmosDbFactory.CosmosActionAsync(
                                                DocumentType.Event,
                                                containerGroup,
                                                async containerDest =>
                                                {
                                                    await containerDest.UpsertItemAsync(jsonDocument);
                                                    count++;
                                                    if (count % 1000 == 0)
                                                    {
                                                        _logger.LogInformation(
                                                            "{Count} - {Unknown} - {Now}",
                                                            count,
                                                            jsonDocument["Timestamp"],
                                                            DateTime.Now);
                                                    }
                                                });

                                            await Task.CompletedTask;
                                        }));
                            }
                        }
                        await Task.WhenAll(tasks);
                    });


            });
        return count;
    }

    public async Task<SortableUniqueIdValue?> GetLastSortableUniqueIdAsync(AggregateContainerGroup containerGroup)
    {
        return await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.Event,
            containerGroup,
            async container =>
            {
                await Task.CompletedTask;
                var options = new QueryRequestOptions { MaxConcurrency = -1, MaxItemCount = -1, MaxBufferedItemCount = -1 };
                var query = container.GetItemLinqQueryable<IEvent>();
                query = query.OrderByDescending(m => m.SortableUniqueId);
                var feedIterator = container.GetItemQueryIterator<dynamic>(query.ToQueryDefinition(), null, options);
                while (feedIterator.HasMoreResults)
                {
                    var response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        var sortableUniqueId = SekibanJsonHelper.GetValue<string>(item, nameof(IDocument.SortableUniqueId));

                        if (sortableUniqueId == null) { continue; }
                        var safe = new SortableUniqueIdValue(SortableUniqueIdValue.GetSafeIdFromUtc());
                        var fromContainer = new SortableUniqueIdValue(sortableUniqueId);

                        return safe.IsEarlierThan(fromContainer) ? safe : fromContainer;
                    }
                }
                return null;
            });
    }
}