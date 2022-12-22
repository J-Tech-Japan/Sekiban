using Azure.Core.Serialization;
using Sekiban.Core.Shared;
using System.Text.Json;
namespace Sekiban.Infrastructure.Cosmos.Lib.Json;

public class SekibanCosmosSerializer : CosmosSerializer
{
    private readonly JsonObjectSerializer _jsonObjectSerializer;

    public SekibanCosmosSerializer(JsonSerializerOptions? jsonSerializerOptions = null) => _jsonObjectSerializer =
        new JsonObjectSerializer(jsonSerializerOptions ?? SekibanJsonHelper.GetDefaultJsonSerializerOptions());

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
