using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
using Sekiban.Pure.Serialize;
using System.Text.Json;
namespace Sekiban.Pure;

public record SekibanDomainTypes
{
    public SekibanDomainTypes(
        IEventTypes eventTypes,
        IAggregateTypes aggregateTypes,
        ICommandTypes commandTypes,
        IAggregateProjectorSpecifier aggregateProjectorSpecifier,
        IQueryTypes queryTypes,
        IMultiProjectorTypes multiProjectorsType,
        JsonSerializerOptions? jsonSerializerOptions = null)
    {
        EventTypes = eventTypes;
        AggregateTypes = aggregateTypes;
        CommandTypes = commandTypes;
        AggregateProjectorSpecifier = aggregateProjectorSpecifier;
        QueryTypes = queryTypes;
        MultiProjectorsType = multiProjectorsType;
        JsonSerializerOptions = jsonSerializerOptions ??
            new JsonSerializerOptions
                { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        Serializer = SekibanSerializer.FromOptions(JsonSerializerOptions, eventTypes);
    }

    public IEventTypes EventTypes { get; init; }
    public IAggregateTypes AggregateTypes { get; init; }
    public ICommandTypes CommandTypes { get; init; }
    public IAggregateProjectorSpecifier AggregateProjectorSpecifier { get; init; }
    public IQueryTypes QueryTypes { get; init; }
    public IMultiProjectorTypes MultiProjectorsType { get; init; }
    public JsonSerializerOptions JsonSerializerOptions { get; init; }
    public ISekibanSerializer Serializer { get; init; }
}