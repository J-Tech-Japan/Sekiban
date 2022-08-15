namespace Sekiban.EventSourcing.WebHelper.Common;

public record SekibanControllerItems(
    IReadOnlyCollection<Type> SekibanAggregates,
    IReadOnlyCollection<(Type serviceType, Type? implementationType)> SekibanCommands,
    IReadOnlyCollection<Type> SingleAggregateProjections,
    IReadOnlyCollection<Type> MultipleAggregatesProjections) : ISekibanControllerItems { }
