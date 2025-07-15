using ResultBoxes;
using Sekiban.Pure.Query;
using System.IO.Compression;
using System.Text.Json;

namespace Sekiban.Pure.Dapr.Serialization;

[Serializable]
public record SerializableListQueryResult
{
    public int? TotalCount { get; init; }
    public int? TotalPages { get; init; }
    public int? CurrentPage { get; init; }
    public int? PageSize { get; init; }
    
    public string RecordTypeName { get; init; } = string.Empty;
    
    public string QueryTypeName { get; init; } = string.Empty;
    
    public byte[] CompressedItemsJson { get; init; } = Array.Empty<byte>();
    
    public byte[] CompressedQueryJson { get; init; } = Array.Empty<byte>();
    
    public string ItemsAssemblyVersion { get; init; } = string.Empty;
    
    public SerializableListQueryResult() { }

    private SerializableListQueryResult(
        int? totalCount,
        int? totalPages,
        int? currentPage,
        int? pageSize,
        string recordTypeName,
        string queryTypeName,
        byte[] compressedItemsJson,
        byte[] compressedQueryJson,
        string itemsAssemblyVersion)
    {
        TotalCount = totalCount;
        TotalPages = totalPages;
        CurrentPage = currentPage;
        PageSize = pageSize;
        RecordTypeName = recordTypeName;
        QueryTypeName = queryTypeName;
        CompressedItemsJson = compressedItemsJson;
        CompressedQueryJson = compressedQueryJson;
        ItemsAssemblyVersion = itemsAssemblyVersion;
    }

    public static async Task<SerializableListQueryResult> CreateFromAsync(
        ListQueryResultGeneral result, 
        JsonSerializerOptions options)
    {
        var queryType = result.Query.GetType();
        string itemsAssemblyVersion = "0.0.0.0";
        
        string recordTypeName = result.RecordType;
        if (result.Items.Any())
        {
            var firstItemType = result.Items.First().GetType();
            itemsAssemblyVersion = firstItemType.Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
            // Use AssemblyQualifiedName for cross-assembly type resolution
            recordTypeName = firstItemType.AssemblyQualifiedName ?? result.RecordType;
        }
        
        byte[] itemsJson;
        try
        {
            itemsJson = JsonSerializer.SerializeToUtf8Bytes(
                result.Items.ToList(), 
                options);
        }
        catch (NotSupportedException)
        {
            itemsJson = JsonSerializer.SerializeToUtf8Bytes(
                result.Items.ToList());
        }
        
        byte[] queryJson;
        try
        {
            queryJson = JsonSerializer.SerializeToUtf8Bytes(
                result.Query,
                queryType,
                options);
        }
        catch (NotSupportedException)
        {
            queryJson = JsonSerializer.SerializeToUtf8Bytes(
                result.Query,
                queryType);
        }
        
        var compressedItemsJson = await CompressAsync(itemsJson);
        var compressedQueryJson = await CompressAsync(queryJson);

        return new SerializableListQueryResult(
            result.TotalCount,
            result.TotalPages,
            result.CurrentPage,
            result.PageSize,
            recordTypeName,
            queryType.AssemblyQualifiedName ?? queryType.FullName ?? queryType.Name,
            compressedItemsJson,
            compressedQueryJson,
            itemsAssemblyVersion
        );
    }

    public static async Task<ResultBox<SerializableListQueryResult>> CreateFromResultBoxAsync(
        ResultBox<IListQueryResult> resultBox,
        IListQueryCommon originalQuery,
        JsonSerializerOptions options)
    {
        if (!resultBox.IsSuccess)
        {
            return ResultBox<SerializableListQueryResult>.FromException(resultBox.GetException());
        }

        var listQueryResult = resultBox.GetValue();
        var listQueryResultGeneral = listQueryResult.ToGeneral(originalQuery);
        var serializable = await CreateFromAsync(listQueryResultGeneral, options);
        
        return ResultBox<SerializableListQueryResult>.FromValue(serializable);
    }

    public async Task<ResultBox<ListQueryResultGeneral>> ToListQueryResultAsync(
        SekibanDomainTypes domainTypes)
    {
        try
        {
            Type? recordType = null;
            if (!string.IsNullOrEmpty(RecordTypeName))
            {
                try
                {
                    // Use GetPayloadTypeByName from QueryTypes for better type resolution
                    recordType = domainTypes.QueryTypes.GetPayloadTypeByName(RecordTypeName);
                    
                    if (recordType == null)
                    {
                        return ResultBox<ListQueryResultGeneral>.FromException(
                            new InvalidOperationException($"Record type not found: {RecordTypeName}"));
                    }
                }
                catch (Exception ex)
                {
                    return ResultBox<ListQueryResultGeneral>.FromException(
                        new InvalidOperationException($"Failed to get record type: {RecordTypeName}", ex));
                }
            }

            Type? queryType = null;
            try
            {
                // Use GetPayloadTypeByName from QueryTypes for better type resolution
                queryType = domainTypes.QueryTypes.GetPayloadTypeByName(QueryTypeName);
                
                if (queryType == null)
                {
                    return ResultBox<ListQueryResultGeneral>.FromException(
                        new InvalidOperationException($"Query type not found: {QueryTypeName}"));
                }
            }
            catch (Exception ex)
            {
                return ResultBox<ListQueryResultGeneral>.FromException(
                    new InvalidOperationException($"Failed to get query type: {QueryTypeName}", ex));
            }

            IEnumerable<object> items = Array.Empty<object>();
            if (CompressedItemsJson.Length > 0 && recordType != null)
            {
                var decompressedJson = await DecompressAsync(CompressedItemsJson);
                var listType = typeof(List<>).MakeGenericType(recordType);
                IEnumerable<object>? itemsList;
                try
                {
                    itemsList = JsonSerializer.Deserialize(
                        decompressedJson, 
                        listType, 
                        domainTypes.JsonSerializerOptions) as IEnumerable<object>;
                }
                catch (NotSupportedException)
                {
                    // Fallback to default options
                    itemsList = JsonSerializer.Deserialize(
                        decompressedJson, 
                        listType) as IEnumerable<object>;
                }
                
                if (itemsList == null)
                {
                    return ResultBox<ListQueryResultGeneral>.FromException(
                        new InvalidOperationException($"Failed to deserialize items: {RecordTypeName}"));
                }
                
                items = itemsList;
            }

            IListQueryCommon? query = null;
            if (CompressedQueryJson.Length > 0)
            {
                var decompressedQueryJson = await DecompressAsync(CompressedQueryJson);
                try
                {
                    query = JsonSerializer.Deserialize(
                        decompressedQueryJson,
                        queryType,
                        domainTypes.JsonSerializerOptions) as IListQueryCommon;
                }
                catch (NotSupportedException)
                {
                    // Fallback to default options
                    query = JsonSerializer.Deserialize(
                        decompressedQueryJson,
                        queryType) as IListQueryCommon;
                }
                
                if (query == null)
                {
                    return ResultBox<ListQueryResultGeneral>.FromException(
                        new InvalidOperationException($"Failed to deserialize query: {QueryTypeName}"));
                }
            }
            else
            {
                return ResultBox<ListQueryResultGeneral>.FromException(
                    new InvalidOperationException("No query data to deserialize"));
            }

            var listQueryResultGeneral = new ListQueryResultGeneral(
                TotalCount,
                TotalPages,
                CurrentPage,
                PageSize,
                items,
                RecordTypeName,
                query);
            
            return ResultBox<ListQueryResultGeneral>.FromValue(listQueryResultGeneral);
        }
        catch (Exception ex)
        {
            return ResultBox<ListQueryResultGeneral>.FromException(
                new InvalidOperationException($"Failed to convert to list query result: {RecordTypeName}", ex));
        }
    }

    public async Task<ResultBox<IListQueryResult>> ToResultBoxAsync(
        SekibanDomainTypes domainTypes)
    {
        var listQueryResultBox = await ToListQueryResultAsync(domainTypes);
        if (!listQueryResultBox.IsSuccess)
        {
            return ResultBox<IListQueryResult>.FromException(listQueryResultBox.GetException());
        }
        return ResultBox<IListQueryResult>.FromValue(listQueryResultBox.GetValue());
    }

    private static async Task<byte[]> CompressAsync(byte[] data)
    {
        if (data.Length == 0)
        {
            return Array.Empty<byte>();
        }

        using var memoryStream = new MemoryStream();
        using (var gzipStream = new GZipStream(memoryStream, CompressionLevel.Fastest))
        {
            await gzipStream.WriteAsync(data);
        }
        return memoryStream.ToArray();
    }

    private static async Task<byte[]> DecompressAsync(byte[] compressedData)
    {
        if (compressedData.Length == 0)
        {
            return Array.Empty<byte>();
        }

        using var compressedStream = new MemoryStream(compressedData);
        using var decompressedStream = new MemoryStream();
        using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress))
        {
            await gzipStream.CopyToAsync(decompressedStream);
        }
        return decompressedStream.ToArray();
    }
}
