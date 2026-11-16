using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Domains;

/// <summary>
///     AOT-compatible implementation of ITagStatePayloadTypes.
///     Uses JsonTypeInfo for serialization instead of reflection.
/// </summary>
public sealed class AotTagStatePayloadTypes : ITagStatePayloadTypes
{
    private readonly Dictionary<string, Func<byte[], ITagStatePayload?>> _deserializers = new();
    private readonly Dictionary<Type, Func<ITagStatePayload, byte[]>> _serializers = new();
    private readonly Dictionary<Type, string> _payloadNames = new();

    /// <summary>
    ///     Register a tag state payload type with AOT-compatible JsonTypeInfo.
    /// </summary>
    /// <typeparam name="T">The tag state payload type</typeparam>
    /// <param name="payloadName">The payload name</param>
    /// <param name="typeInfo">The JsonTypeInfo for serialization</param>
    public void Register<T>(string payloadName, JsonTypeInfo<T> typeInfo)
        where T : ITagStatePayload
    {
        _deserializers[payloadName] = bytes =>
            JsonSerializer.Deserialize(bytes, typeInfo);

        _serializers[typeof(T)] = state =>
            JsonSerializer.SerializeToUtf8Bytes((T)state, typeInfo);
        _payloadNames[typeof(T)] = payloadName;
    }

    /// <inheritdoc />
    public string GetPayloadName(ITagStatePayload payload) =>
        _payloadNames.TryGetValue(payload.GetType(), out var payloadName)
            ? payloadName
            : throw new Exception($"Unknown payload type: {payload.GetType().Name}");

    /// <inheritdoc />
    public ITagStatePayload DeserializePayload(string payloadName, byte[] jsonBytes)
    {
        if (_deserializers.TryGetValue(payloadName, out var deserializer))
        {
            return deserializer(jsonBytes) ?? throw new Exception($"Failed to deserialize: {payloadName}");
        }
        throw new Exception($"Unknown payload type: {payloadName}");
    }

    /// <inheritdoc />
    public byte[] SerializePayload(ITagStatePayload payload) =>
        _serializers.TryGetValue(payload.GetType(), out var serializer)
            ? serializer(payload)
            : throw new Exception($"Unknown payload type: {payload.GetType().Name}");

    /// <summary>
    ///     Not supported in AOT mode. Returns null.
    /// </summary>
    public Type? GetPayloadType(string payloadName) => null;
}
