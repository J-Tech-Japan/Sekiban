using ResultBoxes;
using Sekiban.Pure.Documents;
using System.IO.Compression;
using System.Text.Json;
namespace Sekiban.Pure.Aggregates;

[Serializable]
public record SerializableAggregate
{
    // PartitionKeysの個別プロパティ
    public Guid AggregateId { get; init; } = Guid.Empty;
    public string Group { get; init; } = PartitionKeys.DefaultAggregateGroupName;
    public string RootPartitionKey { get; init; } = PartitionKeys.DefaultRootPartitionKey;
    
    // 元のAggregateから直接コピーする値
    public int Version { get; init; }
    public string LastSortableUniqueId { get; init; } = string.Empty;
    public string ProjectorVersion { get; init; } = string.Empty;
    public string ProjectorTypeName { get; init; } = string.Empty;
    public string PayloadTypeName { get; init; } = string.Empty;
    
    // IAggregatePayloadをシリアライズした圧縮データ
    public byte[] CompressedPayloadJson { get; init; } = Array.Empty<byte>();
    
    // アプリケーションバージョン情報（互換性チェック用）
    public string PayloadAssemblyVersion { get; init; } = string.Empty;
    
    // デフォルトコンストラクタ（シリアライザ用）
    public SerializableAggregate() { }

    // PartitionKeysを取得するメソッド
    public PartitionKeys GetPartitionKeys() => new(AggregateId, Group, RootPartitionKey);

    // コンストラクタ（直接初期化用）
    private SerializableAggregate(
        Guid aggregateId,
        string group,
        string rootPartitionKey,
        int version,
        string lastSortableUniqueId,
        string projectorVersion,
        string projectorTypeName,
        string payloadTypeName,
        byte[] compressedPayloadJson,
        string payloadAssemblyVersion)
    {
        AggregateId = aggregateId;
        Group = group;
        RootPartitionKey = rootPartitionKey;
        Version = version;
        LastSortableUniqueId = lastSortableUniqueId;
        ProjectorVersion = projectorVersion;
        ProjectorTypeName = projectorTypeName;
        PayloadTypeName = payloadTypeName;
        CompressedPayloadJson = compressedPayloadJson;
        PayloadAssemblyVersion = payloadAssemblyVersion;
    }

    // 変換メソッド：Aggregate → SerializableAggregate
    public static async Task<SerializableAggregate> CreateFromAsync(
        Aggregate aggregate, 
        JsonSerializerOptions options)
    {
        // PayloadをJSONシリアライズしてGZip圧縮
        byte[] compressedPayloadJson = Array.Empty<byte>();
        string payloadAssemblyVersion = "0.0.0.0";

        if (aggregate.Payload != null)
        {
            var payloadType = aggregate.Payload.GetType();
            payloadAssemblyVersion = payloadType.Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
            
            var payloadJson = JsonSerializer.SerializeToUtf8Bytes(
                aggregate.Payload, 
                payloadType, 
                options);
            
            compressedPayloadJson = await CompressAsync(payloadJson);
        }

        return new SerializableAggregate(
            aggregate.PartitionKeys.AggregateId,
            aggregate.PartitionKeys.Group,
            aggregate.PartitionKeys.RootPartitionKey,
            aggregate.Version,
            aggregate.LastSortableUniqueId,
            aggregate.ProjectorVersion,
            aggregate.ProjectorTypeName,
            aggregate.PayloadTypeName,
            compressedPayloadJson,
            payloadAssemblyVersion
        );
    }

    // 変換メソッド：SerializableAggregate → Aggregate
    public async Task<OptionalValue<Aggregate>> ToAggregateAsync(
        SekibanDomainTypes domainTypes)
    {
        try
        {
            // EmptyAggregatePayloadの場合は特別処理
            if (PayloadTypeName == typeof(EmptyAggregatePayload).Name)
            {
                // EmptyAggregatePayloadは直接作成可能
                var emptyAggregate = new Aggregate(
                    new EmptyAggregatePayload(),
                    GetPartitionKeys(),
                    Version,
                    LastSortableUniqueId,
                    ProjectorVersion,
                    ProjectorTypeName,
                    PayloadTypeName);

                return new OptionalValue<Aggregate>(emptyAggregate);
            }
            
            // その他のPayload型はドメインタイプから取得
            Type? payloadType = null;
            try
            {
                payloadType = domainTypes.AggregateTypes.GetPayloadTypeByName(PayloadTypeName);
                if (payloadType == null)
                {
                    // 型が見つからない場合は互換性なしと判断
                    return OptionalValue<Aggregate>.Empty;
                }
            }
            catch
            {
                // 例外が発生した場合も互換性なしと判断
                return OptionalValue<Aggregate>.Empty;
            }

            // バージョン互換性チェック（オプション）
            // PayloadAssemblyVersionと現在のバージョンを比較する
            // 異なる場合は互換性がないと判断することもできる
            // 今回は互換性チェックはスキップしますが、必要に応じて以下のようなコードを追加できます
            /*
            var currentVersion = payloadType.Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
            if (currentVersion != PayloadAssemblyVersion)
            {
                return OptionalValue<Aggregate>.Empty;
            }
            */

            // PayloadをJSONデシリアライズ
            IAggregatePayload? payload = null;
            
            if (CompressedPayloadJson.Length > 0)
            {
                var decompressedJson = await DecompressAsync(CompressedPayloadJson);
                payload = (IAggregatePayload?)JsonSerializer.Deserialize(
                    decompressedJson, 
                    payloadType, 
                    domainTypes.JsonSerializerOptions);
                
                if (payload == null)
                {
                    return OptionalValue<Aggregate>.Empty;
                }
            }
            else
            {
                // 圧縮データがない場合はEmptyAggregatePayload
                payload = new EmptyAggregatePayload();
            }

            // Aggregateを再構築
            var aggregate = new Aggregate(
                payload,
                GetPartitionKeys(),
                Version,
                LastSortableUniqueId,
                ProjectorVersion,
                ProjectorTypeName,
                PayloadTypeName);

            return new OptionalValue<Aggregate>(aggregate);
        }
        catch (Exception)
        {
            // 変換中に例外が発生した場合は、互換性なしと判断
            return OptionalValue<Aggregate>.Empty;
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
