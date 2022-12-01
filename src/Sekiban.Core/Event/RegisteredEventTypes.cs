using System.Reflection;

namespace Sekiban.Core.Event;

public class RegisteredEventTypes
{
    private readonly List<Type> _registeredTypes = new();

    public RegisteredEventTypes(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            var decoratedTypes = assembly.DefinedTypes.Where(x =>
                x.IsClass && x.ImplementedInterfaces.Contains(typeof(IEventPayloadCommon)));
            foreach (var type in decoratedTypes)
            {
                if (_registeredTypes.Contains(type)) continue;
                _registeredTypes.Add(type);
            }
        }

        RegisteredTypes = _registeredTypes.AsReadOnly();
    }

    public ReadOnlyCollection<Type> RegisteredTypes { get; }
}
