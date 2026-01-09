using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Orleans.Storage;
namespace Sekiban.Dcb.Orleans;

/// <summary>
///     Custom JSON serializer for Orleans grain storage that handles DCB types properly
/// </summary>
public class NewtonsoftJsonDcbOrleansSerializer : IGrainStorageSerializer
{
    private readonly JsonSerializerSettings _settings;

    public NewtonsoftJsonDcbOrleansSerializer() =>
        _settings = new JsonSerializerSettings
        {
            // Use default contract resolver that includes fields
            ContractResolver = new DefaultContractResolver(),
            // Preserve type information for complex types
            TypeNameHandling = TypeNameHandling.Auto,
            // Include null values
            NullValueHandling = NullValueHandling.Include
        };

    public BinaryData Serialize<T>(T input)
    {
        var json = JsonConvert.SerializeObject(input, _settings);
        return BinaryData.FromString(json);
    }

    public T Deserialize<T>(BinaryData input)
    {
        var json = input.ToString();

        // Handle empty or null JSON as default state
        if (string.IsNullOrWhiteSpace(json) || json == "null" || json == "{}")
        {
            // Return default instance for value types or create new instance for reference types
            if (typeof(T).IsValueType)
            {
                return default!;
            }

            // Try to create default instance
            try
            {
                return Activator.CreateInstance<T>();
            }
            catch
            {
                return default!;
            }
        }

        var result = JsonConvert.DeserializeObject<T>(json, _settings);

        if (result is null)
        {
            // Return default instead of throwing for grain state deserialization
            try
            {
                return Activator.CreateInstance<T>();
            }
            catch
            {
                return default!;
            }
        }

        return result;
    }
}
