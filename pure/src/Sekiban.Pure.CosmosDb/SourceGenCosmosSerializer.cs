using Microsoft.Azure.Cosmos;
using Sekiban.Pure.Events;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
namespace Sekiban.Pure.CosmosDb;

public class SourceGenCosmosSerializer<TEventTypes> : CosmosSerializer where TEventTypes : IEventTypes, new()
{
    private readonly JsonSerializerOptions _serializerOptions;

    public SourceGenCosmosSerializer(JsonSerializerOptions serializerOptions)
    {
        var eventTypes = new TEventTypes();
        // check if all event types are registered
        eventTypes.CheckEventJsonContextOption(serializerOptions);
        _serializerOptions = serializerOptions ??
            new JsonSerializerOptions
                { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    }
    public override T FromStream<T>(Stream stream)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (typeof(Stream).IsAssignableFrom(typeof(T)))
        {
            return (T)(object)stream;
        }

        using (stream)
        {
            var typeInfo = _serializerOptions.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>;
            if (typeInfo != null)
            {
                return JsonSerializer.Deserialize(stream, typeInfo) ??
                    throw new JsonException("Failed to deserialize the stream.");
            }
            return JsonSerializer.Deserialize<T>(stream, _serializerOptions) ??
                throw new JsonException("Failed to deserialize the stream.");
        }
    }

    public override Stream ToStream<T>(T input)
    {
        var stream = new MemoryStream();

        var typeInfo = _serializerOptions.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>;

        if (typeInfo != null)
        {
            var json = JsonSerializer.Serialize(input, typeInfo);
            var writer = new Utf8JsonWriter(stream);
            writer.WriteRawValue(json);
            writer.Flush();
        } else
        {
            JsonSerializer.Serialize(stream, input, _serializerOptions);
        }

        stream.Seek(0, SeekOrigin.Begin);
        return stream;
    }
}
