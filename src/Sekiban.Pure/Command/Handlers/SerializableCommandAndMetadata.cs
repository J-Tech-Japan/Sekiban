using ResultBoxes;
using System.IO.Compression;
using System.Text.Json;
namespace Sekiban.Pure.Command.Handlers;

[Serializable]
public record SerializableCommandAndMetadata
{
    // CommandMetadataのフラットなプロパティ
    public Guid CommandId { get; init; } = Guid.Empty;
    public string CausationId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public string ExecutedUser { get; init; } = string.Empty;
    
    // Commandの情報
    public string CommandTypeName { get; init; } = string.Empty;
    public string ProjectorTypeName { get; init; } = string.Empty;
    public string AggregatePayloadTypeName { get; init; } = string.Empty;
    
    // ICommandWithHandlerSerializableを圧縮したデータ
    public byte[] CompressedCommandJson { get; init; } = Array.Empty<byte>();
    
    // アプリケーションバージョン情報（互換性チェック用）
    public string CommandAssemblyVersion { get; init; } = string.Empty;
    
    // デフォルトコンストラクタ（シリアライザ用）
    public SerializableCommandAndMetadata() { }

    // CommandMetadataを取得するメソッド
    public CommandMetadata GetCommandMetadata() => 
        new(CommandId, CausationId, CorrelationId, ExecutedUser);

    // コンストラクタ（直接初期化用）
    private SerializableCommandAndMetadata(
        Guid commandId,
        string causationId,
        string correlationId,
        string executedUser,
        string commandTypeName,
        string projectorTypeName,
        string aggregatePayloadTypeName,
        byte[] compressedCommandJson,
        string commandAssemblyVersion)
    {
        CommandId = commandId;
        CausationId = causationId;
        CorrelationId = correlationId;
        ExecutedUser = executedUser;
        CommandTypeName = commandTypeName;
        ProjectorTypeName = projectorTypeName;
        AggregatePayloadTypeName = aggregatePayloadTypeName;
        CompressedCommandJson = compressedCommandJson;
        CommandAssemblyVersion = commandAssemblyVersion;
    }

    // 変換メソッド：ICommandWithHandlerSerializable + CommandMetadata → SerializableCommandAndMetadata
    public static async Task<SerializableCommandAndMetadata> CreateFromAsync(
        ICommandWithHandlerSerializable command,
        CommandMetadata metadata,
        JsonSerializerOptions options)
    {
        // CommandをJSONシリアライズしてGZip圧縮
        byte[] compressedCommandJson = Array.Empty<byte>();
        string commandAssemblyVersion = "0.0.0.0";

        if (command != null)
        {
            var commandType = command.GetType();
            commandAssemblyVersion = commandType.Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
            
            var commandJson = JsonSerializer.SerializeToUtf8Bytes(
                command, 
                commandType, 
                options);
            
            compressedCommandJson = await CompressAsync(commandJson);
        }

        // プロジェクター名を取得
        string projectorTypeName = string.Empty;
        try
        {
            var projector = command?.GetProjector();
            projectorTypeName = projector?.GetType().Name ?? string.Empty;
        }
        catch
        {
            // エラーが発生した場合は空文字列を使用
        }

        // アグリゲートペイロードタイプ名を取得
        string aggregatePayloadTypeName = string.Empty;
        try
        {
            var payloadTypeOpt = command?.GetAggregatePayloadType();
            if (payloadTypeOpt?.HasValue == true)
            {
                aggregatePayloadTypeName = payloadTypeOpt.Value.Name;
            }
        }
        catch
        {
            // エラーが発生した場合は空文字列を使用
        }

        return new SerializableCommandAndMetadata(
            metadata.CommandId,
            metadata.CausationId,
            metadata.CorrelationId,
            metadata.ExecutedUser,
            command?.GetType().Name ?? string.Empty,
            projectorTypeName,
            aggregatePayloadTypeName,
            compressedCommandJson,
            commandAssemblyVersion
        );
    }

    // 変換メソッド：SerializableCommandAndMetadata → ICommandWithHandlerSerializable + CommandMetadata
    public async Task<OptionalValue<(ICommandWithHandlerSerializable Command, CommandMetadata Metadata)>> ToCommandAndMetadataAsync(
        SekibanDomainTypes domainTypes)
    {
        try
        {
            // コマンドタイプを取得
            Type? commandType = null;
            try
            {
                commandType = domainTypes.CommandTypes.GetCommandTypeByName(CommandTypeName);
                if (commandType == null)
                {
                    // 型が見つからない場合は互換性なしと判断
                    return OptionalValue<(ICommandWithHandlerSerializable, CommandMetadata)>.Empty;
                }
            }
            catch
            {
                // 例外が発生した場合も互換性なしと判断
                return OptionalValue<(ICommandWithHandlerSerializable, CommandMetadata)>.Empty;
            }

            // CommandをJSONデシリアライズ
            ICommandWithHandlerSerializable? command = null;
            
            if (CompressedCommandJson.Length > 0)
            {
                var decompressedJson = await DecompressAsync(CompressedCommandJson);
                command = (ICommandWithHandlerSerializable?)JsonSerializer.Deserialize(
                    decompressedJson, 
                    commandType, 
                    domainTypes.JsonSerializerOptions);
                
                if (command == null)
                {
                    return OptionalValue<(ICommandWithHandlerSerializable, CommandMetadata)>.Empty;
                }
            }
            else
            {
                // 圧縮データがない場合はエラー
                return OptionalValue<(ICommandWithHandlerSerializable, CommandMetadata)>.Empty;
            }

            // CommandMetadataを再構築
            var metadata = GetCommandMetadata();

            return new OptionalValue<(ICommandWithHandlerSerializable, CommandMetadata)>((command, metadata));
        }
        catch (Exception)
        {
            // 変換中に例外が発生した場合は、互換性なしと判断
            return OptionalValue<(ICommandWithHandlerSerializable, CommandMetadata)>.Empty;
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