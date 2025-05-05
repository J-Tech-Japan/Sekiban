using Newtonsoft.Json;
using Orleans.Storage;
namespace Sekiban.Pure.Orleans.Parts;

public class NewtonsoftJsonSekibanOrleansSerializer : IGrainStorageSerializer
{
    private readonly JsonSerializerSettings _settings;

    public NewtonsoftJsonSekibanOrleansSerializer()
    {
        _settings = new JsonSerializerSettings
        {
            // Similar to IncludeFields = true in System.Text.Json
            ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
            {

            }
        };
    }
    public BinaryData Serialize<T>(T input)
    {
        string json = JsonConvert.SerializeObject(input, _settings);
        return BinaryData.FromString(json);
    }

    public T Deserialize<T>(BinaryData input)
    {
        string json = input.ToString();
        return JsonConvert.DeserializeObject<T>(json, _settings);
    }
}
