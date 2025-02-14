using Sekiban.Pure.Events;
using System.Text.Json;
namespace Sekiban.Pure.Serialize;

public class SekibanSourceGenSerializer : ISekibanSerializer
{
    private readonly JsonSerializerOptions _serializerOptions;
    private SekibanSourceGenSerializer(JsonSerializerOptions serializerOptions) =>
        _serializerOptions = serializerOptions ??
            new JsonSerializerOptions
                { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static SekibanSourceGenSerializer FromOptions<TEventTypes>(JsonSerializerOptions? serializerOptions)
        where TEventTypes : IEventTypes, new()
    {
        var eventTypes = new TEventTypes();
        // check if all event types are registered
        eventTypes.CheckEventJsonContextOption(serializerOptions);
        // ソースジェネレーターで生成されたオプションを利用できるようにする
        return new SekibanSourceGenSerializer(serializerOptions);
    }

    public JsonSerializerOptions GetJsonSerializerOptions() => _serializerOptions;
    public string Serialize<T>(T json) => JsonSerializer.Serialize(json, _serializerOptions);

    public T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, _serializerOptions);
}
public class SekibanSerializer : ISekibanSerializer
{
    private readonly JsonSerializerOptions _serializerOptions;
    private SekibanSerializer(JsonSerializerOptions serializerOptions) =>
        _serializerOptions = serializerOptions ??
            new JsonSerializerOptions
                { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static SekibanSerializer FromOptions(JsonSerializerOptions? serializerOptions, IEventTypes eventTypes)
    {
        // check if all event types are registered
        eventTypes.CheckEventJsonContextOption(serializerOptions);
        // ソースジェネレーターで生成されたオプションを利用できるようにする
        return new SekibanSerializer(serializerOptions);
    }

    public JsonSerializerOptions GetJsonSerializerOptions() => _serializerOptions;
    public string Serialize<T>(T json) => JsonSerializer.Serialize(json, _serializerOptions);

    public T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, _serializerOptions);
}