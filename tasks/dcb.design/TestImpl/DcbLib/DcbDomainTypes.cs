using System.Text.Json;
using DcbLib.Domains;

namespace DcbLib;

/// <summary>
/// Main class that aggregates all domain type management interfaces for DCB
/// </summary>
public record DcbDomainTypes
{
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
        JsonSerializerOptions = jsonSerializerOptions ?? new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
    }

    public IEventTypes EventTypes { get; init; }
    public ITagTypes TagTypes { get; init; }
    public ITagProjectorTypes TagProjectorTypes { get; init; }
    public ITagStatePayloadTypes TagStatePayloadTypes { get; init; }
    public JsonSerializerOptions JsonSerializerOptions { get; init; }
}

/// <summary>
/// Interface for providing DcbDomainTypes
/// </summary>
public interface IDcbDomainTypesProvider
{
    static abstract DcbDomainTypes Generate(JsonSerializerOptions jsonSerializerOptions);
}