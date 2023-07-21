using Sekiban.Core.Aggregate;
using Sekiban.Core.Events;
using System.Reflection;
namespace Sekiban.Core.Dependency;

/// <summary>
///     System use base interface for Dependency Definition
///     Application developers does not need to use this interface directly
/// </summary>
public interface IDependencyDefinition : IQueryDefinition
{
    /// <summary>
    ///     Convert to SekibanDependencyOptions
    /// </summary>
    /// <returns>SekibanDependencyOptions</returns>
    public SekibanDependencyOptions GetSekibanDependencyOptions() =>
        new(
            new RegisteredEventTypes(GetExecutingAssembly(), SekibanEventSourcingDependency.GetAssembly()),
            new SekibanAggregateTypes(GetExecutingAssembly(), SekibanEventSourcingDependency.GetAssembly()),
            GetCommandDependencies().Concat(GetSubscriberDependencies()));
    /// <summary>
    ///     Get library's assembly.
    ///     Just return Assembly.GetExecutingAssembly() in each dependency definition.
    /// </summary>
    /// <returns></returns>
    Assembly GetExecutingAssembly();
    /// <summary>
    ///     List of Command Dependencies
    /// </summary>
    /// <returns></returns>
    IEnumerable<(Type serviceType, Type? implementationType)> GetCommandDependencies();
    /// <summary>
    ///     List of Event Subscriber Dependencies
    /// </summary>
    /// <returns></returns>
    IEnumerable<(Type serviceType, Type? implementationType)> GetSubscriberDependencies();
    /// <summary>
    ///     Get Aggregate Definitions in the dependency definition
    /// </summary>
    /// <returns></returns>
    IEnumerable<IAggregateDependencyDefinition> GetAggregateDefinitions();
}
