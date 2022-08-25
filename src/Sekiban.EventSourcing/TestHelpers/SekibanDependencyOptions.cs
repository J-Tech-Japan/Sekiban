namespace Sekiban.EventSourcing.TestHelpers;

public record SekibanDependencyOptions
{
    public RegisteredEventTypes RegisteredEventTypes { get; init; }
    public SekibanAggregateTypes SekibanAggregateTypes { get; init; }
    public IEnumerable<(Type serviceType, Type? implementationType)> TransientDependencies { get; init; }
    public SekibanDependencyOptions(
        RegisteredEventTypes registeredEventTypes,
        SekibanAggregateTypes sekibanAggregateTypes,
        IEnumerable<(Type serviceType, Type? implementationType)> transientDependencies)
    {
        RegisteredEventTypes = registeredEventTypes;
        SekibanAggregateTypes = sekibanAggregateTypes;
        TransientDependencies = transientDependencies;
    }
}
