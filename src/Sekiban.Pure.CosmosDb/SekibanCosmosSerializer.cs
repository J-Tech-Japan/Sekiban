using Azure.Core.Serialization;
using Microsoft.Azure.Cosmos;
using System.Text.Json;
namespace Sekiban.Pure.CosmosDb;

public class SekibanCosmosSerializer(JsonSerializerOptions? options = null) : CosmosSerializer
{
    private readonly JsonObjectSerializer _jsonObjectSerializer = new(
        options ??
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    public override T FromStream<T>(Stream stream)
    {
        if (typeof(Stream).IsAssignableFrom(typeof(T)))
        {
            return (T)(object)stream;
        }

        using (stream)
        {
            return (T)_jsonObjectSerializer.Deserialize(stream, typeof(T), default)!;
        }
    }

    public override Stream ToStream<T>(T input)
    {
        var streamPayload = new MemoryStream();
        _jsonObjectSerializer.Serialize(streamPayload, input, typeof(T), default);
        streamPayload.Position = 0;
        return streamPayload;
    }
}