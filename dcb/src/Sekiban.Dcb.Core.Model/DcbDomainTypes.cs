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
    public ICoreMultiProjectorTypes MultiProjectorTypes { get; init; }
    public ICoreQueryTypes QueryTypes { get; init; }
    public JsonSerializerOptions JsonSerializerOptions { get; init; }
    public DcbDomainTypes(
        IEventTypes eventTypes,
        ITagTypes tagTypes,
        ITagProjectorTypes tagProjectorTypes,
        ITagStatePayloadTypes tagStatePayloadTypes,
        ICoreMultiProjectorTypes multiProjectorTypes,
        ICoreQueryTypes queryTypes,
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
}
