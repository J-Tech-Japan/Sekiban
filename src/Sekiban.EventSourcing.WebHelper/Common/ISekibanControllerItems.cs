namespace Sekiban.EventSourcing.WebHelper.Common;

public interface ISekibanControllerItems
{
    public IReadOnlyCollection<Type> SekibanAggregates { get; }
    public IReadOnlyCollection<(Type serviceType, Type? implementationType)> SekibanCommands { get; }
    public IReadOnlyCollection<Type> SingleAggregateProjections { get; }
    public IReadOnlyCollection<Type> MultipleAggregatesProjections { get; }
}
