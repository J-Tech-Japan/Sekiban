using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Orleans.Storage;
namespace Sekiban.Pure.Orleans.Parts;

public class NewtonsoftJsonSekibanOrleansSerializer : IGrainStorageSerializer
{
    private readonly JsonSerializerSettings _settings;

    public NewtonsoftJsonSekibanOrleansSerializer() =>
        _settings = new JsonSerializerSettings
        {
            // Similar to IncludeFields = true in System.Text.Json
            ContractResolver = new DefaultContractResolver()
        };
    public BinaryData Serialize<T>(T input)
    {
        var json = JsonConvert.SerializeObject(input, _settings);
        return BinaryData.FromString(json);
    }

    public T Deserialize<T>(BinaryData input)
    {
        var json = input.ToString();
        var result = JsonConvert.DeserializeObject<T>(json, _settings);
        
        if (result is null)
        {
            throw new InvalidOperationException($"Failed to deserialize {typeof(T).Name} from JSON: {json}");
        }
        
        return result;
    }
}
