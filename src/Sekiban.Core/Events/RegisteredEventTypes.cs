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
