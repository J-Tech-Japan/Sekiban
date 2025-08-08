using Sekiban.Dcb.Domains;
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
    public JsonSerializerOptions JsonSerializerOptions { get; init; }
    public DcbDomainTypes(
        IEventTypes eventTypes,
        ITagTypes tagTypes,
        ITagProjectorTypes tagProjectorTypes,
        ITagStatePayloadTypes tagStatePayloadTypes,
        JsonSerializerOptions? jsonSerializerOptions = null)
    {
        EventTypes = eventTypes;
        TagTypes = tagTypes;
        TagProjectorTypes = tagProjectorTypes;
        TagStatePayloadTypes = tagStatePayloadTypes;
        JsonSerializerOptions = jsonSerializerOptions ??
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
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
        public JsonSerializerOptions JsonOptions { get; }

        internal Builder(JsonSerializerOptions? jsonOptions = null)
        {
            JsonOptions = jsonOptions ??
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                };

            EventTypes = new SimpleEventTypes(JsonOptions);
            TagTypes = new SimpleTagTypes();
            TagProjectorTypes = new SimpleTagProjectorTypes();
            TagStatePayloadTypes = new SimpleTagStatePayloadTypes();
        }

        internal DcbDomainTypes Build() =>
            new(EventTypes, TagTypes, TagProjectorTypes, TagStatePayloadTypes, JsonOptions);
    }
}
