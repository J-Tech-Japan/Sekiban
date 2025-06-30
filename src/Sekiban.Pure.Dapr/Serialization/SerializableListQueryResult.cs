using ResultBoxes;
using Sekiban.Pure.Query;
using System.IO.Compression;
using System.Text.Json;

namespace Sekiban.Pure.Dapr.Serialization;

[Serializable]
public record SerializableListQueryResult
{
    // ページング情報
    public int? TotalCount { get; init; }
    public int? TotalPages { get; init; }
    public int? CurrentPage { get; init; }
    public int? PageSize { get; init; }
    
    // レコードの型名
    public string RecordTypeName { get; init; } = string.Empty;
    
    // クエリの型名
    public string QueryTypeName { get; init; } = string.Empty;
    
    // アイテムをシリアライズした圧縮データ
    public byte[] CompressedItemsJson { get; init; } = Array.Empty<byte>();
    
    // クエリをシリアライズした圧縮データ
    public byte[] CompressedQueryJson { get; init; } = Array.Empty<byte>();
    
    // アプリケーションバージョン情報（互換性チェック用）
    public string ItemsAssemblyVersion { get; init; } = string.Empty;
    
    // デフォルトコンストラクタ（シリアライザ用）
    public SerializableListQueryResult() { }

    // コンストラクタ（直接初期化用）
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

    // 変換メソッド：ListQueryResultGeneral → SerializableListQueryResult
    public static async Task<SerializableListQueryResult> CreateFromAsync(
        ListQueryResultGeneral result, 
        JsonSerializerOptions options)
    {
        var queryType = result.Query.GetType();
        string itemsAssemblyVersion = "0.0.0.0";
        
        // アイテムの型情報を取得（アイテムがある場合）
        if (result.Items.Any())
        {
            var firstItemType = result.Items.First().GetType();
            itemsAssemblyVersion = firstItemType.Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        }
        
        // アイテムリストをシリアライズ
        var itemsJson = JsonSerializer.SerializeToUtf8Bytes(
            result.Items.ToList(), 
            options);
        
        var queryJson = JsonSerializer.SerializeToUtf8Bytes(
            result.Query,
            queryType,
            options);
        
        var compressedItemsJson = await CompressAsync(itemsJson);
        var compressedQueryJson = await CompressAsync(queryJson);

        return new SerializableListQueryResult(
            result.TotalCount,
            result.TotalPages,
            result.CurrentPage,
            result.PageSize,
            result.RecordType,
            queryType.FullName ?? queryType.Name,
            compressedItemsJson,
            compressedQueryJson,
            itemsAssemblyVersion
        );
    }

    // 変換メソッド：ResultBox<IListQueryResult> → SerializableListQueryResult (成功時のみ)
    public static async Task<ResultBox<SerializableListQueryResult>> CreateFromResultBoxAsync(
        ResultBox<IListQueryResult> resultBox,
        IListQueryCommon originalQuery,
        JsonSerializerOptions options)
    {
        if (resultBox.IsFailure)
        {
            return ResultBox<SerializableListQueryResult>.FromException(resultBox.GetException());
        }

        var listQueryResult = resultBox.GetValue();
        var listQueryResultGeneral = listQueryResult.ToGeneral(originalQuery);
        var serializable = await CreateFromAsync(listQueryResultGeneral, options);
        
        return ResultBox<SerializableListQueryResult>.FromValue(serializable);
    }

    // 変換メソッド：SerializableListQueryResult → ListQueryResultGeneral
    public async Task<ResultBox<ListQueryResultGeneral>> ToListQueryResultAsync(
        SekibanDomainTypes domainTypes)
    {
        try
        {
            // レコード型を取得
            Type? recordType = null;
            if (!string.IsNullOrEmpty(RecordTypeName))
            {
                try
                {
                    recordType = domainTypes.GetTypeByFullName(RecordTypeName);
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

            // クエリ型を取得
            Type? queryType = null;
            try
            {
                queryType = domainTypes.QueryTypes.GetTypeByFullName(QueryTypeName);
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

            // アイテムをデシリアライズ
            IEnumerable<object> items = Array.Empty<object>();
            if (CompressedItemsJson.Length > 0 && recordType != null)
            {
                var decompressedJson = await DecompressAsync(CompressedItemsJson);
                var listType = typeof(List<>).MakeGenericType(recordType);
                var itemsList = JsonSerializer.Deserialize(
                    decompressedJson, 
                    listType, 
                    domainTypes.JsonSerializerOptions) as IEnumerable<object>;
                
                if (itemsList == null)
                {
                    return ResultBox<ListQueryResultGeneral>.FromException(
                        new InvalidOperationException($"Failed to deserialize items: {RecordTypeName}"));
                }
                
                items = itemsList;
            }

            // クエリをデシリアライズ
            IListQueryCommon? query = null;
            if (CompressedQueryJson.Length > 0)
            {
                var decompressedQueryJson = await DecompressAsync(CompressedQueryJson);
                query = JsonSerializer.Deserialize(
                    decompressedQueryJson,
                    queryType,
                    domainTypes.JsonSerializerOptions) as IListQueryCommon;
                
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

    // 変換メソッド：SerializableListQueryResult → ResultBox<IListQueryResult>
    public async Task<ResultBox<IListQueryResult>> ToResultBoxAsync(
        SekibanDomainTypes domainTypes)
    {
        var listQueryResultBox = await ToListQueryResultAsync(domainTypes);
        return listQueryResultBox.Conveyor<IListQueryResult>(lqr => ResultBox<IListQueryResult>.FromValue(lqr));
    }

    // GZip圧縮ヘルパーメソッド
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

    // GZip解凍ヘルパーメソッド
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