using System.Text.Json;
namespace Sekiban.Pure.Serialize;

public class SekibanReflectionSerializer : ISekibanSerializer
{
    private readonly JsonSerializerOptions _serializerOptions = new()
        { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public JsonSerializerOptions GetJsonSerializerOptions() => _serializerOptions;
    public string Serialize<T>(T json) => JsonSerializer.Serialize(json, _serializerOptions);

    public T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, _serializerOptions);
}