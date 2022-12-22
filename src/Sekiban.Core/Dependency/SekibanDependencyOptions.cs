using System.Reflection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Events;

namespace Sekiban.Core.Dependency;

public record SekibanDependencyOptions
{
    public SekibanDependencyOptions(
        RegisteredEventTypes registeredEventTypes,
        SekibanAggregateTypes sekibanAggregateTypes,
        IEnumerable<(Type serviceType, Type? implementationType)> transientDependencies)
    {
        RegisteredEventTypes = registeredEventTypes;
        SekibanAggregateTypes = sekibanAggregateTypes;
        TransientDependencies = transientDependencies;
    }

    public RegisteredEventTypes RegisteredEventTypes { get; init; }
    public SekibanAggregateTypes SekibanAggregateTypes { get; init; }
    public IEnumerable<(Type serviceType, Type? implementationType)> TransientDependencies { get; init; }

    public static SekibanDependencyOptions CreateMergedOption(
        Assembly[] assemblies,
        IEnumerable<(Type serviceType, Type? implementationType)> transientDependencies)
    {
        return new(
            new RegisteredEventTypes(assemblies),
            new SekibanAggregateTypes(assemblies),
            transientDependencies);
    }
}
