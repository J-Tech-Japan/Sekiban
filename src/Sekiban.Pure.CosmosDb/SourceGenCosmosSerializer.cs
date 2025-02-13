using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Azure.Cosmos;
using Sekiban.Pure.Events;

namespace Sekiban.Pure.CosmosDb;

public class SourceGenCosmosSerializer<TEventTypes> : CosmosSerializer where TEventTypes : IEventTypes, new()
{
    private readonly JsonSerializerOptions _serializerOptions;

    public SourceGenCosmosSerializer(JsonSerializerOptions serializerOptions)
    {
        var eventTypes = new TEventTypes();
        // check if all event types are registered
        eventTypes.CheckEventJsonContextOption(serializerOptions);
        // ソースジェネレーターで生成されたオプションを利用できるようにする
        _serializerOptions = serializerOptions ?? new JsonSerializerOptions() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase};
    }
    public override T FromStream<T>(Stream stream)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        // T が Stream 自身の場合はそのまま返す
        if (typeof(Stream).IsAssignableFrom(typeof(T)))
        {
            return (T)(object)stream;
        }

        // Cosmos DB SDK の仕様により、ストリームは SDK 内で閉じられるので using で囲む
        using (stream)
        {
            var typeInfo = _serializerOptions.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>;
            if (typeInfo != null)
            {
                // ソースジェネレータで最適化されたデシリアライゼーション
                return (T)JsonSerializer.Deserialize(stream, typeInfo);
            }
            // ※ 同期処理で呼び出すために GetAwaiter().GetResult() を利用
            return JsonSerializer.Deserialize<T>(stream, _serializerOptions);
        }
    }

    public override Stream ToStream<T>(T input)
    {
        var stream = new MemoryStream();

        // まず、ソースジェネレータで生成された JsonTypeInfo を取得してみる
        var typeInfo = _serializerOptions.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>;

        if (typeInfo != null)
        {
            // ソースジェネレータで最適化されたシリアライゼーション
            var json = JsonSerializer.Serialize(input, typeInfo);
            var writer = new Utf8JsonWriter(stream);
            writer.WriteRawValue(json);
            writer.Flush();
        }
        else
        {
            // 対象型がソースジェネレータに登録されていない場合は、フォールバック
            JsonSerializer.Serialize(stream, input, _serializerOptions);
        }
        
        // ストリームの先頭にシーク
        stream.Seek(0, SeekOrigin.Begin);
        return stream;    
    }
}