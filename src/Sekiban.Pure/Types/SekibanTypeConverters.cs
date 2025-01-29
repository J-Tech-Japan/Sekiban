using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;

namespace Sekiban.Pure.Types;

public record SekibanTypeConverters(IAggregateTypes AggregateTypes, IEventTypes EventTypes, IAggregateProjectorSpecifier AggregateProjectorSpecifier);