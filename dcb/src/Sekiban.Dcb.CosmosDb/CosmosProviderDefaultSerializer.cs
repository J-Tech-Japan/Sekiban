using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Text;

namespace Sekiban.Dcb.CosmosDb;

/// <summary>
/// The provider-owned default JSON serializer used by contexts created from a connection string. Keeping the
/// instance on the context makes the measurement and the actual Cosmos client use the same serializer boundary.
/// </summary>
internal sealed class CosmosProviderDefaultSerializer : CosmosSerializer
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new CamelCaseNamingStrategy()
        },
        NullValueHandling = NullValueHandling.Include,
        DateTimeZoneHandling = DateTimeZoneHandling.Utc
    };

    public override Stream ToStream<T>(T input)
    {
        var json = JsonConvert.SerializeObject(input, Settings);
        return new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
    }

    public override T FromStream<T>(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        using var jsonReader = new JsonTextReader(reader);
        return JsonSerializer.Create(Settings).Deserialize<T>(jsonReader)!;
    }
}
