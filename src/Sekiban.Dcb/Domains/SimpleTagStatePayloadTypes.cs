using ResultBoxes;
using Sekiban.Dcb.Tags;
using System.Text;
using System.Text.Json;
namespace Sekiban.Dcb.Domains;

/// <summary>
///     Simple implementation of ITagStatePayloadTypes
///     Manages registration and deserialization of tag state payload types
/// </summary>
public class SimpleTagStatePayloadTypes : ITagStatePayloadTypes
{
    private readonly JsonSerializerOptions _jsonSerializerOptions;
    private readonly Dictionary<string, Type> _payloadTypes = new();

    public SimpleTagStatePayloadTypes(JsonSerializerOptions? jsonSerializerOptions = null) =>
        _jsonSerializerOptions = jsonSerializerOptions ??
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };

    public Type? GetPayloadType(string payloadName) =>
        _payloadTypes.TryGetValue(payloadName, out var type) ? type : null;

    public ResultBox<ITagStatePayload> DeserializePayload(string payloadName, byte[] jsonBytes)
    {
        try
        {
            // Special handling for empty payload
            if (payloadName == nameof(EmptyTagStatePayload))
            {
                return ResultBox.FromValue((ITagStatePayload)new EmptyTagStatePayload());
            }

            // Get the payload type
            var payloadType = GetPayloadType(payloadName);
            if (payloadType == null)
            {
                return ResultBox.Error<ITagStatePayload>(
                    new InvalidOperationException($"Payload type '{payloadName}' is not registered"));
            }

            // Convert bytes to JSON string
            var json = Encoding.UTF8.GetString(jsonBytes);

            // Deserialize to the specific type
            var payload = JsonSerializer.Deserialize(json, payloadType, _jsonSerializerOptions);

            if (payload is ITagStatePayload tagStatePayload)
            {
                return ResultBox.FromValue(tagStatePayload);
            }

            return ResultBox.Error<ITagStatePayload>(
                new InvalidCastException("Deserialized object is not an ITagStatePayload"));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<ITagStatePayload>(ex);
        }
    }

    public ResultBox<byte[]> SerializePayload(ITagStatePayload payload)
    {
        try
        {
            // Special handling for empty payload
            if (payload is EmptyTagStatePayload)
            {
                return ResultBox.FromValue(Array.Empty<byte>());
            }

            // Serialize the payload to JSON
            var json = JsonSerializer.Serialize(payload, payload.GetType(), _jsonSerializerOptions);
            var jsonBytes = Encoding.UTF8.GetBytes(json);

            return ResultBox.FromValue(jsonBytes);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<byte[]>(ex);
        }
    }

    public void RegisterPayloadType<T>(string? name = null) where T : ITagStatePayload
    {
        var payloadName = name ?? typeof(T).Name;
        var newType = typeof(T);

        if (_payloadTypes.TryGetValue(payloadName, out var existingType))
        {
            if (existingType != newType)
            {
                throw new InvalidOperationException(
                    $"Tag state payload name '{payloadName}' is already registered with type '{existingType.FullName}'. " +
                    $"Cannot register it with different type '{newType.FullName}'.");
            }
        }
        _payloadTypes[payloadName] = newType;
    }
}
