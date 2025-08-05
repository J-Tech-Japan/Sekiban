using System.Text;
using System.Text.Json;
using DcbLib.Tags;
using ResultBoxes;

namespace DcbLib.Domains;

/// <summary>
/// Simple implementation of ITagStatePayloadTypes
/// Manages registration and deserialization of tag state payload types
/// </summary>
public class SimpleTagStatePayloadTypes : ITagStatePayloadTypes
{
    private readonly Dictionary<string, Type> _payloadTypes = new();
    private readonly JsonSerializerOptions _jsonSerializerOptions;
    
    public SimpleTagStatePayloadTypes(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        _jsonSerializerOptions = jsonSerializerOptions ?? new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
    }
    
    public Type? GetPayloadType(string payloadName)
    {
        return _payloadTypes.TryGetValue(payloadName, out var type) ? type : null;
    }
    
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
                    new InvalidOperationException($"Payload type '{payloadName}' is not registered")
                );
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
                new InvalidCastException($"Deserialized object is not an ITagStatePayload")
            );
        }
        catch (Exception ex)
        {
            return ResultBox.Error<ITagStatePayload>(ex);
        }
    }
    
    public void RegisterPayloadType<T>(string payloadName) where T : ITagStatePayload
    {
        _payloadTypes[payloadName] = typeof(T);
    }
}