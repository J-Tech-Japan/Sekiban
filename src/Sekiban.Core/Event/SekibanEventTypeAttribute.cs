using System.Reflection;
namespace Sekiban.Core.Event;

[AttributeUsage(AttributeTargets.Class)]
public class SekibanEventTypeAttribute : Attribute
{
}
public class RegisteredEventTypes
{
    private readonly List<Type> _registeredTypes = new();

    public RegisteredEventTypes(params Assembly[] assemblies)
    {
        var attributeType = typeof(SekibanEventTypeAttribute);
        foreach (var assembly in assemblies)
        {
            var decoratedTypes = assembly.DefinedTypes.Where(x => x.IsClass && x.ImplementedInterfaces.Contains(typeof(IEventPayload)));
            foreach (var type in decoratedTypes)
            {
                _registeredTypes.Add(type);
            }
        }
        RegisteredTypes = _registeredTypes.AsReadOnly();
    }
    public ReadOnlyCollection<Type> RegisteredTypes { get; }
}
