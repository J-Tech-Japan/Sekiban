using Orleans.Storage;
using JsonSerializer = System.Text.Json.JsonSerializer;
namespace Sekiban.Pure.Orleans.Parts;

public class SystemTextJsonStorageSerializer : IGrainStorageSerializer
{
    public T Deserialize<T>(BinaryData data) =>
        JsonSerializer.Deserialize<T>(data.ToString()) ??
        throw new InvalidOperationException("Failed to deserialize data.");

    public BinaryData Serialize<T>(T state)
    {
        var json = JsonSerializer.Serialize(state);
        return BinaryData.FromString(json);
    }
}
