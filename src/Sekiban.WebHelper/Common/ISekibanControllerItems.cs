namespace Sekiban.WebHelper.Common;

public interface ISekibanControllerItems
{
    public IReadOnlyCollection<Type> SekibanAggregates { get; }
    public IReadOnlyCollection<(Type serviceType, Type? implementationType)> SekibanCommands { get; }
}
