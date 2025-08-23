using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Orleans.Storage;

namespace Sekiban.Dcb.Orleans;

/// <summary>
/// Custom JSON serializer for Orleans grain storage that handles DCB types properly
/// </summary>
public class NewtonsoftJsonDcbOrleansSerializer : IGrainStorageSerializer
{
    private readonly JsonSerializerSettings _settings;

    public NewtonsoftJsonDcbOrleansSerializer()
    {
        _settings = new JsonSerializerSettings
        {
            // Use default contract resolver that includes fields
            ContractResolver = new DefaultContractResolver(),
            // Preserve type information for complex types
            TypeNameHandling = TypeNameHandling.Auto,
            // Include null values
            NullValueHandling = NullValueHandling.Include
        };
    }

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