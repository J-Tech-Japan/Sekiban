namespace Sekiban.WebHelper.Common;

public record SekibanControllerItems(
    IReadOnlyCollection<Type> SekibanAggregates,
    IReadOnlyCollection<(Type serviceType, Type? implementationType)> SekibanCommands) : ISekibanControllerItems { }
