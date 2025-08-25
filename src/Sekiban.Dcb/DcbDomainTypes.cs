using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Queries;
using System.Text.Json;
namespace Sekiban.Dcb;

/// <summary>
///     Main class that aggregates all domain type management interfaces for DCB
/// </summary>
public record DcbDomainTypes
{

    public IEventTypes EventTypes { get; init; }
    public ITagTypes TagTypes { get; init; }
    public ITagProjectorTypes TagProjectorTypes { get; init; }
    public ITagStatePayloadTypes TagStatePayloadTypes { get; init; }
    public IMultiProjectorTypes MultiProjectorTypes { get; init; }
    public IQueryTypes QueryTypes { get; init; }
    public JsonSerializerOptions JsonSerializerOptions { get; init; }
    public DcbDomainTypes(
        IEventTypes eventTypes,
        ITagTypes tagTypes,
        ITagProjectorTypes tagProjectorTypes,
        ITagStatePayloadTypes tagStatePayloadTypes,
        IMultiProjectorTypes multiProjectorTypes,
        IQueryTypes queryTypes,
        JsonSerializerOptions? jsonSerializerOptions = null)
    {
        EventTypes = eventTypes;
        TagTypes = tagTypes;
        TagProjectorTypes = tagProjectorTypes;
        TagStatePayloadTypes = tagStatePayloadTypes;
        MultiProjectorTypes = multiProjectorTypes;
        QueryTypes = queryTypes;
        JsonSerializerOptions = jsonSerializerOptions ??
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
    }

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
