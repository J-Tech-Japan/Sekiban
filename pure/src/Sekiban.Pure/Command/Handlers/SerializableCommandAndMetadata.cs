using ResultBoxes;
using System.IO.Compression;
using System.Text.Json;
namespace Sekiban.Pure.Command.Handlers;

[Serializable]
public record SerializableCommandAndMetadata
{
    public Guid CommandId { get; init; } = Guid.Empty;
    public string CausationId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public string ExecutedUser { get; init; } = string.Empty;

    public string CommandTypeName { get; init; } = string.Empty;
    public string ProjectorTypeName { get; init; } = string.Empty;
    public string AggregatePayloadTypeName { get; init; } = string.Empty;

    public byte[] CompressedCommandJson { get; init; } = Array.Empty<byte>();

    public string CommandAssemblyVersion { get; init; } = string.Empty;

    public SerializableCommandAndMetadata() { }

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

    public CommandMetadata GetCommandMetadata() =>
        new(CommandId, CausationId, CorrelationId, ExecutedUser);

    public static async Task<SerializableCommandAndMetadata> CreateFromAsync(
        ICommandWithHandlerSerializable command,
        CommandMetadata metadata,
        JsonSerializerOptions options)
    {
        var compressedCommandJson = Array.Empty<byte>();
        var commandAssemblyVersion = "0.0.0.0";

        if (command != null)
        {
            var commandType = command.GetType();
            commandAssemblyVersion = commandType.Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

            var commandJson = JsonSerializer.SerializeToUtf8Bytes(command, commandType, options);

            compressedCommandJson = await CompressAsync(commandJson);
        }

        var projectorTypeName = string.Empty;
        try
        {
            var projector = command?.GetProjector();
            projectorTypeName = projector?.GetType().Name ?? string.Empty;
        }
        catch
        {
        }

        var aggregatePayloadTypeName = string.Empty;
        try
        {
            var payloadTypeOpt = command?.GetAggregatePayloadType();
            if (payloadTypeOpt?.HasValue == true && payloadTypeOpt.Value != null)
            {
                aggregatePayloadTypeName = payloadTypeOpt.Value.Name;
            }
        }
        catch
        {
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
            commandAssemblyVersion);
    }

    public async Task<OptionalValue<(ICommandWithHandlerSerializable Command, CommandMetadata Metadata)>>
        ToCommandAndMetadataAsync(SekibanDomainTypes domainTypes)
    {
        try
        {
            Type? commandType = null;
            try
            {
                commandType = domainTypes.CommandTypes.GetCommandTypeByName(CommandTypeName);
                if (commandType == null)
                {
                    return OptionalValue<(ICommandWithHandlerSerializable, CommandMetadata)>.Empty;
                }
            }
            catch
            {
                return OptionalValue<(ICommandWithHandlerSerializable, CommandMetadata)>.Empty;
            }

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
            } else
            {
                return OptionalValue<(ICommandWithHandlerSerializable, CommandMetadata)>.Empty;
            }

            var metadata = GetCommandMetadata();

            return new OptionalValue<(ICommandWithHandlerSerializable, CommandMetadata)>((command, metadata));
        }
        catch (Exception)
        {
            return OptionalValue<(ICommandWithHandlerSerializable, CommandMetadata)>.Empty;
        }
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
