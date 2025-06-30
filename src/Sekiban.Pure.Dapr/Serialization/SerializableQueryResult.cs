using ResultBoxes;
using Sekiban.Pure.Query;
using System.IO.Compression;
using System.Text.Json;

namespace Sekiban.Pure.Dapr.Serialization;

[Serializable]
public record SerializableQueryResult
{
    // 結果の型名
    public string ResultTypeName { get; init; } = string.Empty;
    
    // クエリの型名
    public string QueryTypeName { get; init; } = string.Empty;
    
    // 結果をシリアライズした圧縮データ
    public byte[] CompressedResultJson { get; init; } = Array.Empty<byte>();
    
    // クエリをシリアライズした圧縮データ
    public byte[] CompressedQueryJson { get; init; } = Array.Empty<byte>();
    
    // アプリケーションバージョン情報（互換性チェック用）
    public string ResultAssemblyVersion { get; init; } = string.Empty;
    
    // デフォルトコンストラクタ（シリアライザ用）
    public SerializableQueryResult() { }

    // コンストラクタ（直接初期化用）
    private SerializableQueryResult(
        string resultTypeName,
        string queryTypeName,
        byte[] compressedResultJson,
        byte[] compressedQueryJson,
        string resultAssemblyVersion)
    {
        ResultTypeName = resultTypeName;
        QueryTypeName = queryTypeName;
        CompressedResultJson = compressedResultJson;
        CompressedQueryJson = compressedQueryJson;
        ResultAssemblyVersion = resultAssemblyVersion;
    }

    // 変換メソッド：QueryResultGeneral → SerializableQueryResult
    public static async Task<SerializableQueryResult> CreateFromAsync(
        QueryResultGeneral result, 
        JsonSerializerOptions options)
    {
        var resultType = result.Value.GetType();
        var queryType = result.Query.GetType();
        var resultAssemblyVersion = resultType.Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        
        var resultJson = JsonSerializer.SerializeToUtf8Bytes(
            result.Value, 
            resultType, 
            options);
        
        var queryJson = JsonSerializer.SerializeToUtf8Bytes(
            result.Query,
            queryType,
            options);
        
        var compressedResultJson = await CompressAsync(resultJson);
        var compressedQueryJson = await CompressAsync(queryJson);

        return new SerializableQueryResult(
            result.ResultType,
            queryType.FullName ?? queryType.Name,
            compressedResultJson,
            compressedQueryJson,
            resultAssemblyVersion
        );
    }

    // 変換メソッド：ResultBox<object> → SerializableQueryResult (成功時のみ)
    public static async Task<ResultBox<SerializableQueryResult>> CreateFromResultBoxAsync(
        ResultBox<object> resultBox,
        IQueryCommon originalQuery,
        JsonSerializerOptions options)
    {
        if (resultBox.IsFailure)
        {
            return ResultBox<SerializableQueryResult>.FromException(resultBox.GetException());
        }

        var value = resultBox.GetValue();
        var resultType = value.GetType();
        var queryResultGeneral = new QueryResultGeneral(value, resultType.FullName ?? resultType.Name, originalQuery);
        var serializable = await CreateFromAsync(queryResultGeneral, options);
        
        return ResultBox<SerializableQueryResult>.FromValue(serializable);
    }

    // 変換メソッド：SerializableQueryResult → QueryResultGeneral
    public async Task<ResultBox<QueryResultGeneral>> ToQueryResultAsync(
        SekibanDomainTypes domainTypes)
    {
        try
        {
            // 結果型を取得
            Type? resultType = null;
            try
            {
                resultType = domainTypes.GetTypeByFullName(ResultTypeName);
                if (resultType == null)
                {
                    return ResultBox<QueryResultGeneral>.FromException(
                        new InvalidOperationException($"Result type not found: {ResultTypeName}"));
                }
            }
            catch (Exception ex)
            {
                return ResultBox<QueryResultGeneral>.FromException(
                    new InvalidOperationException($"Failed to get result type: {ResultTypeName}", ex));
            }

            // クエリ型を取得
            Type? queryType = null;
            try
            {
                queryType = domainTypes.QueryTypes.GetTypeByFullName(QueryTypeName);
                if (queryType == null)
                {
                    return ResultBox<QueryResultGeneral>.FromException(
                        new InvalidOperationException($"Query type not found: {QueryTypeName}"));
                }
            }
            catch (Exception ex)
            {
                return ResultBox<QueryResultGeneral>.FromException(
                    new InvalidOperationException($"Failed to get query type: {QueryTypeName}", ex));
            }

            // 結果をデシリアライズ
            object? result = null;
            if (CompressedResultJson.Length > 0)
            {
                var decompressedJson = await DecompressAsync(CompressedResultJson);
                result = JsonSerializer.Deserialize(
                    decompressedJson, 
                    resultType, 
                    domainTypes.JsonSerializerOptions);
                
                if (result == null)
                {
                    return ResultBox<QueryResultGeneral>.FromException(
                        new InvalidOperationException($"Failed to deserialize result: {ResultTypeName}"));
                }
            }
            else
            {
                return ResultBox<QueryResultGeneral>.FromException(
                    new InvalidOperationException("No result data to deserialize"));
            }

            // クエリをデシリアライズ
            IQueryCommon? query = null;
            if (CompressedQueryJson.Length > 0)
            {
                var decompressedQueryJson = await DecompressAsync(CompressedQueryJson);
                query = JsonSerializer.Deserialize(
                    decompressedQueryJson,
                    queryType,
                    domainTypes.JsonSerializerOptions) as IQueryCommon;
                
                if (query == null)
                {
                    return ResultBox<QueryResultGeneral>.FromException(
                        new InvalidOperationException($"Failed to deserialize query: {QueryTypeName}"));
                }
            }
            else
            {
                return ResultBox<QueryResultGeneral>.FromException(
                    new InvalidOperationException("No query data to deserialize"));
            }

            var queryResultGeneral = new QueryResultGeneral(result, ResultTypeName, query);
            return ResultBox<QueryResultGeneral>.FromValue(queryResultGeneral);
        }
        catch (Exception ex)
        {
            return ResultBox<QueryResultGeneral>.FromException(
                new InvalidOperationException($"Failed to convert to query result: {ResultTypeName}", ex));
        }
    }

    // 変換メソッド：SerializableQueryResult → ResultBox<object>
    public async Task<ResultBox<object>> ToResultBoxAsync(
        SekibanDomainTypes domainTypes)
    {
        var queryResultBox = await ToQueryResultAsync(domainTypes);
        return queryResultBox.Conveyor(qr => ResultBox<object>.FromValue(qr.Value));
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