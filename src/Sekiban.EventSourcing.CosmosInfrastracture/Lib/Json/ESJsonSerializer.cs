namespace CosmosInfrastructure.Lib.Json;

public class ESJsonSerializer : CosmosSerializer
{
    private static readonly Encoding DefaultEncoding = new UTF8Encoding(false, true);

    private readonly JsonSerializer _serializer;

    public ESJsonSerializer(JsonSerializerSettings settings) =>
        _serializer = JsonSerializer.Create(settings);

    public override T FromStream<T>(Stream stream)
    {
        if (typeof(Stream).IsAssignableFrom(typeof(T)))
        {
            return (T)(object)stream;
        }

        using (var sr = new StreamReader(stream))
        {
            using (var jsonTextReader = new JsonTextReader(sr))
            {
                return _serializer.Deserialize<T>(jsonTextReader)!;
            }
        }
    }

    public override Stream ToStream<T>(T input)
    {
        var streamPayload = new MemoryStream();
        using (var streamWriter = new StreamWriter(streamPayload, DefaultEncoding, 1024, true))
        {
            using (JsonWriter writer = new JsonTextWriter(streamWriter))
            {
                writer.Formatting = _serializer.Formatting;
                _serializer.Serialize(writer, input);
                writer.Flush();
                streamWriter.Flush();
            }
        }

        streamPayload.Position = 0;
        return streamPayload;
    }
}
