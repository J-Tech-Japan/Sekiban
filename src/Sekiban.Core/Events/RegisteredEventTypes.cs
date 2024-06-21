using Sekiban.Core.Command;
using Sekiban.Core.Types;
using System.Reflection;
namespace Sekiban.Core.Events;

/// <summary>
///     Generate Event Types from Assemblies
/// </summary>
public class RegisteredEventTypes
{
    private readonly List<Type> _registeredTypes = [];
    /// <summary>
    ///     Readonly Event Types
    /// </summary>
    public ReadOnlyCollection<Type> RegisteredTypes { get; }
    /// <summary>
    ///     Generate Event Types from Assemblies
    /// </summary>
    /// <param name="assemblies"></param>
    public RegisteredEventTypes(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            var types = assembly.DefinedTypes;
            var decoratedTypes = types.Where(x => x.IsClass && x.ImplementedInterfaces.Contains(typeof(IEventPayloadCommon)));
            foreach (var type in decoratedTypes)
            {
                if (_registeredTypes.Contains(type))
                {
                    continue;
                }
                _registeredTypes.Add(type);
            }
        }

        RegisteredTypes = _registeredTypes.AsReadOnly();
    }
}
public class RegisteredCommandWithHandlerTypes
{
    private readonly List<Type> _registeredTypes = [];
    /// <summary>
    ///     Readonly Event Types
    /// </summary>
    public ReadOnlyCollection<Type> RegisteredTypes { get; }
    /// <summary>
    ///     Generate Event Types from Assemblies
    /// </summary>
    /// <param name="assemblies"></param>
    public RegisteredCommandWithHandlerTypes(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            var types = assembly.DefinedTypes;
            var decoratedTypes = types.Where(x => x.IsClass && x.ImplementedInterfaces.Contains(typeof(ICommandWithHandlerCommon)));
            foreach (var type in decoratedTypes)
            {
                if (_registeredTypes.Contains(type))
                {
                    continue;
                }
                _registeredTypes.Add(type);
            }
        }

        RegisteredTypes = _registeredTypes.AsReadOnly();
    }
}
public class RegisteredNextQueryTypes
{
    private readonly List<Type> _registeredTypes = [];
    /// <summary>
    ///     Readonly Event Types
    /// </summary>
    public ReadOnlyCollection<Type> RegisteredTypes { get; }
    /// <summary>
    ///     Generate Event Types from Assemblies
    /// </summary>
    /// <param name="assemblies"></param>
    public RegisteredNextQueryTypes(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            var types = assembly.DefinedTypes;
            var decoratedTypes = types.Where(x => x.IsClass && x.AsType().IsQueryNextType());
            foreach (var type in decoratedTypes)
            {
                if (_registeredTypes.Contains(type))
                {
                    continue;
                }
                _registeredTypes.Add(type);
            }
        }

        RegisteredTypes = _registeredTypes.AsReadOnly();
    }
}
public class RegisteredNextListQueryTypes
{
    private readonly List<Type> _registeredTypes = [];
    /// <summary>
    ///     Readonly Event Types
    /// </summary>
    public ReadOnlyCollection<Type> RegisteredTypes { get; }
    /// <summary>
    ///     Generate Event Types from Assemblies
    /// </summary>
    /// <param name="assemblies"></param>
    public RegisteredNextListQueryTypes(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            var types = assembly.DefinedTypes;
            var decoratedTypes = types.Where(x => x.IsClass && x.AsType().IsListQueryNextType());
            foreach (var type in decoratedTypes)
            {
                if (_registeredTypes.Contains(type))
                {
                    continue;
                }
                _registeredTypes.Add(type);
            }
        }

        RegisteredTypes = _registeredTypes.AsReadOnly();
    }
}
