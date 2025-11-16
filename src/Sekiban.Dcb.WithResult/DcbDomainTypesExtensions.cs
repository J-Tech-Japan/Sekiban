using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Queries;
using System.Text.Json;
namespace Sekiban.Dcb;

/// <summary>
///     Extension methods for DcbDomainTypes to support WithResult package
/// </summary>
public static class DcbDomainTypesExtensions
{
    /// <summary>
    ///     Creates a simple DcbDomainTypes configuration with manual type registration
    /// </summary>
    public static DcbDomainTypes Simple(Action<Builder> configure, JsonSerializerOptions? jsonOptions = null)
    {
        var builder = new Builder(jsonOptions);
        configure(builder);
        return builder.Build();
    }

    /// <summary>
    ///     Simple builder class for configuring domain types
    /// </summary>
    public class Builder
    {
        public SimpleEventTypes EventTypes { get; }
        public SimpleTagTypes TagTypes { get; }
        public SimpleTagProjectorTypes TagProjectorTypes { get; }
        public SimpleTagStatePayloadTypes TagStatePayloadTypes { get; }
        public SimpleMultiProjectorTypes MultiProjectorTypes { get; }
        public SimpleQueryTypes QueryTypes { get; }
        public JsonSerializerOptions JsonOptions { get; }

        internal Builder(JsonSerializerOptions? jsonOptions = null)
        {
            JsonOptions = jsonOptions ??
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                };

            EventTypes = new SimpleEventTypes(JsonOptions);
            TagTypes = new SimpleTagTypes();
            TagProjectorTypes = new SimpleTagProjectorTypes();
            TagStatePayloadTypes = new SimpleTagStatePayloadTypes();
            MultiProjectorTypes = new SimpleMultiProjectorTypes();
            QueryTypes = new SimpleQueryTypes();
        }

        internal DcbDomainTypes Build() =>
            new(
                EventTypes,
                TagTypes,
                TagProjectorTypes,
                TagStatePayloadTypes,
                MultiProjectorTypes,
                QueryTypes,
                JsonOptions);
    }
}
