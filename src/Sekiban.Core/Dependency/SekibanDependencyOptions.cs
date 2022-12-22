using Sekiban.Core.Aggregate;
using Sekiban.Core.Events;
using System.Reflection;
namespace Sekiban.Core.Dependency;

/// <summary>
///     System use dependency injection options
///     Application developers do not need to use this class directly
/// </summary>
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
        IEnumerable<(Type serviceType, Type? implementationType)> transientDependencies) => new(
        new RegisteredEventTypes(assemblies),
        new SekibanAggregateTypes(assemblies),
        transientDependencies);
}
