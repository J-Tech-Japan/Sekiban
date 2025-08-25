using Sekiban.Pure.Events;
using System.Text.Json;
namespace Sekiban.Pure.Serialize;

public class SekibanSerializer : ISekibanSerializer
{
    private readonly JsonSerializerOptions _serializerOptions;
    private SekibanSerializer(JsonSerializerOptions? serializerOptions) =>
        _serializerOptions = serializerOptions ??
            new JsonSerializerOptions
                { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public JsonSerializerOptions GetJsonSerializerOptions() => _serializerOptions;
    public string Serialize<T>(T json) => JsonSerializer.Serialize(json, _serializerOptions);

    public T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, _serializerOptions) ??
        throw new JsonException("Deserialization failed.");

    public static SekibanSerializer FromOptions(JsonSerializerOptions? serializerOptions, IEventTypes eventTypes)
    {
        // check if all event types are registered
        if (serializerOptions is not null)
        {
            eventTypes.CheckEventJsonContextOption(serializerOptions);
        }
        return new SekibanSerializer(serializerOptions);
    }
}
