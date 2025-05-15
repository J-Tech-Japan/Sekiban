using Orleans.Storage;
using JsonSerializer = System.Text.Json.JsonSerializer;
namespace Sekiban.Pure.Orleans.Parts;

public class SystemTextJsonStorageSerializer : IGrainStorageSerializer
{
    public T Deserialize<T>(BinaryData data) =>
        // BinaryData を文字列に変換してからデシリアライズ
        JsonSerializer.Deserialize<T>(data.ToString()) ??
        throw new InvalidOperationException("Failed to deserialize data.");

    public BinaryData Serialize<T>(T state)
    {
        // シリアライズして BinaryData に変換
        var json = JsonSerializer.Serialize(state);
        return BinaryData.FromString(json);
    }
}
