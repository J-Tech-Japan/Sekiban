using ResultBoxes;
using Sekiban.Pure.Query;
using System.IO.Compression;
using System.Text.Json;

namespace Sekiban.Pure.Dapr.Serialization;

[Serializable]
public record SerializableListQuery
{
    // クエリの型名
    public string QueryTypeName { get; init; } = string.Empty;
    
    // IListQueryCommonをシリアライズした圧縮データ
    public byte[] CompressedQueryJson { get; init; } = Array.Empty<byte>();
    
    // アプリケーションバージョン情報（互換性チェック用）
    public string QueryAssemblyVersion { get; init; } = string.Empty;
    
    // デフォルトコンストラクタ（シリアライザ用）
    public SerializableListQuery() { }

    // コンストラクタ（直接初期化用）
    private SerializableListQuery(
        string queryTypeName,
        byte[] compressedQueryJson,
        string queryAssemblyVersion)
    {
        QueryTypeName = queryTypeName;
        CompressedQueryJson = compressedQueryJson;
        QueryAssemblyVersion = queryAssemblyVersion;
    }

    // 変換メソッド：IListQueryCommon → SerializableListQuery
    public static async Task<SerializableListQuery> CreateFromAsync(
        IListQueryCommon query, 
        JsonSerializerOptions options)
    {
        var queryType = query.GetType();
        var queryAssemblyVersion = queryType.Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        
        // Use default options if the provided options don't support the type
        byte[] queryJson;
        try
        {
            queryJson = JsonSerializer.SerializeToUtf8Bytes(
                query, 
                queryType, 
                options);
        }
        catch (NotSupportedException)
        {
            // Fallback to default serialization options for types not in source generation
            queryJson = JsonSerializer.SerializeToUtf8Bytes(
                query, 
                queryType);
        }
        
        var compressedQueryJson = await CompressAsync(queryJson);

        return new SerializableListQuery(
            queryType.AssemblyQualifiedName ?? queryType.FullName ?? queryType.Name,
            compressedQueryJson,
            queryAssemblyVersion
        );
    }

    // 変換メソッド：SerializableListQuery → IListQueryCommon
    public async Task<ResultBox<IListQueryCommon>> ToListQueryAsync(
        SekibanDomainTypes domainTypes)
    {
        try
        {
            // クエリ型を取得
            Type? queryType = null;
            try
            {
                // Use GetPayloadTypeByName from QueryTypes for better type resolution
                queryType = domainTypes.QueryTypes.GetPayloadTypeByName(QueryTypeName);
                
                if (queryType == null)
                {
                    return ResultBox<IListQueryCommon>.FromException(
                        new InvalidOperationException($"List query type not found: {QueryTypeName}"));
                }
            }
            catch (Exception ex)
            {
                return ResultBox<IListQueryCommon>.FromException(
                    new InvalidOperationException($"Failed to get list query type: {QueryTypeName}", ex));
            }

            // クエリをデシリアライズ
            if (CompressedQueryJson.Length > 0)
            {
                var decompressedJson = await DecompressAsync(CompressedQueryJson);
                IListQueryCommon? query;
                try
                {
                    query = JsonSerializer.Deserialize(
                        decompressedJson, 
                        queryType, 
                        domainTypes.JsonSerializerOptions) as IListQueryCommon;
                }
                catch (NotSupportedException)
                {
                    // Fallback to default options
                    query = JsonSerializer.Deserialize(
                        decompressedJson, 
                        queryType) as IListQueryCommon;
                }
                
                if (query == null)
                {
                    return ResultBox<IListQueryCommon>.FromException(
                        new InvalidOperationException($"Failed to deserialize list query: {QueryTypeName}"));
                }

                return ResultBox<IListQueryCommon>.FromValue(query);
            }
            
            return ResultBox<IListQueryCommon>.FromException(
                new InvalidOperationException("No list query data to deserialize"));
        }
        catch (Exception ex)
        {
            return ResultBox<IListQueryCommon>.FromException(
                new InvalidOperationException($"Failed to convert to list query: {QueryTypeName}", ex));
        }
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