using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Cache;
using Sekiban.Core.Documents;
using Sekiban.Core.Events;
using Sekiban.Core.Setting;
using Document = Amazon.DynamoDBv2.DocumentModel.Document;
namespace Sekiban.Infrastructure.Dynamo;

/// <summary>
///     Create DynamoDb session for Sekiban
/// </summary>
public class DynamoDbFactory(SekibanDynamoDbOptions dbOptions, IMemoryCacheAccessor memoryCache, IServiceProvider serviceProvider)
{
    private string SekibanContextIdentifier()
    {
        var sekibanContext = serviceProvider.GetService<ISekibanContext>();
        return sekibanContext?.SettingGroupIdentifier ?? SekibanContext.Default;
    }
    private SekibanAwsOption GetSekibanDynamoDbOption()
    {
        return dbOptions.Contexts.Find(m => m.Context == SekibanContextIdentifier()) ?? new SekibanAwsOption();
    }

    private string GetTableId(DocumentType documentType, AggregateContainerGroup containerGroup)
    {
        var dbOption = GetSekibanDynamoDbOption();
        return (documentType, containerGroup) switch
        {
            (DocumentType.Event, AggregateContainerGroup.Default) => dbOption.EventsTableId,
            (DocumentType.Event, AggregateContainerGroup.Dissolvable) => dbOption.EventsTableIdDissolvable,
            (_, AggregateContainerGroup.Default) => dbOption.ItemsTableId,
            _ => dbOption.ItemsTableIdDissolvable
        };
    }

    private static string GetMemoryCacheTableKey(DocumentType documentType, string tableId, string sekibanContextIdentifier) =>
        $"{(documentType == DocumentType.Event ? "event." : "")}dynamo.table.{tableId}.{sekibanContextIdentifier}";
    private static string GetMemoryCacheClientKey(DocumentType documentType, string sekibanContextIdentifier) =>
        $"{(documentType == DocumentType.Event ? "event." : "")}dynamo.client.{sekibanContextIdentifier}";

    private string GetAwsAccessKeyId(DocumentType documentType)
    {
        var dbOption = GetSekibanDynamoDbOption();
        return dbOption.AwsAccessKeyId ?? string.Empty;
    }
    private string GetAwsAccessKey(DocumentType documentType)
    {
        var dbOption = GetSekibanDynamoDbOption();
        return dbOption.AwsAccessKey ?? string.Empty;
    }
    private RegionEndpoint GetDynamoDbRegion()
    {
        var dbOption = GetSekibanDynamoDbOption();
        var region = dbOption.DynamoDbRegion ?? string.Empty;
        return RegionEndpoint.EnumerableAllRegions.FirstOrDefault(m => m.SystemName == region) ??
            throw new Exception("CosmosDbEndPointUrl not found");
    }

    private async Task<Table> GetTableAsync(DocumentType documentType, AggregateContainerGroup containerGroup)
    {
        var tableId = GetTableId(documentType, containerGroup);
        var tableFromCache = (Table?)memoryCache.Cache.Get(GetMemoryCacheTableKey(documentType, tableId, SekibanContextIdentifier()));

        if (tableFromCache is not null)
        {
            return tableFromCache;
        }

        var awsAccessKeyId = GetAwsAccessKeyId(documentType);
        var awsAccessKey = GetAwsAccessKey(documentType);
        var region = GetDynamoDbRegion();
        var client = (AmazonDynamoDBClient?)memoryCache.Cache.Get(GetMemoryCacheClientKey(documentType, SekibanContextIdentifier()));
        if (client is null)
        {
            client = new AmazonDynamoDBClient(awsAccessKeyId, awsAccessKey, region);
            memoryCache.Cache.Set(GetMemoryCacheClientKey(documentType, SekibanContextIdentifier()), client);
        }
        var table = Table.LoadTable(client, tableId);

        memoryCache.Cache.Set(GetMemoryCacheTableKey(documentType, tableId, SekibanContextIdentifier()), table, new MemoryCacheEntryOptions());
        await Task.CompletedTask;
        return table;
    }

    public async Task DeleteAllFromAggregateFromContainerIncludes(
        DocumentType documentType,
        AggregateContainerGroup containerGroup = AggregateContainerGroup.Default)
    {
        await DynamoActionAsync<IEnumerable<IEvent>?>(
            documentType,
            containerGroup,
            async table =>
            {

                var search = table.Scan(new ScanFilter());

                do
                {
                    var batchWriter = table.CreateBatchWrite();
                    List<Document> items = await search.GetNextSetAsync();
                    foreach (var item in items)
                    {
                        batchWriter.AddItemToDelete(item);
                    }
                    await batchWriter.ExecuteAsync();
                } while (!search.IsDone);

                return null;
            });
    }



    public async Task DeleteAllFromEventContainer(AggregateContainerGroup containerGroup)
    {
        await DeleteAllFromAggregateFromContainerIncludes(DocumentType.Event, containerGroup);
    }
    public async Task DeleteAllFromItemsContainer(AggregateContainerGroup containerGroup)
    {
        await DeleteAllFromAggregateFromContainerIncludes(DocumentType.Command, containerGroup);
    }
    public async Task<T> DynamoActionAsync<T>(DocumentType documentType, AggregateContainerGroup containerGroup, Func<Table, Task<T>> dynamoAction)
    {
        try
        {
            var result = await dynamoAction(await GetTableAsync(documentType, containerGroup));
            return result;
        }
        catch
        {
            ResetMemoryCache(documentType, containerGroup);
            throw;
        }
    }

    private void ResetMemoryCache(DocumentType documentType, AggregateContainerGroup containerGroup)
    {
        var containerId = GetTableId(documentType, containerGroup);
        // There may be a network error, so initialize the container.
        // This allows reconnection when recovered next time.
        memoryCache.Cache.Remove(GetMemoryCacheClientKey(documentType, SekibanContextIdentifier()));
        memoryCache.Cache.Remove(GetMemoryCacheTableKey(documentType, containerId, SekibanContextIdentifier()));
    }

    public async Task DynamoActionAsync(DocumentType documentType, AggregateContainerGroup containerGroup, Func<Table, Task> dynamoAction)
    {
        try
        {
            await dynamoAction(await GetTableAsync(documentType, containerGroup));
        }
        catch
        {
            // There may be a network error, so initialize the container.
            // This allows reconnection when recovered next time.
            ResetMemoryCache(documentType, containerGroup);
            throw;
        }
    }
}
