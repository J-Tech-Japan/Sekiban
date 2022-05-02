using System.Reflection;
namespace Sekiban.EventSourcing.AggregateEvents;

[AttributeUsage(AttributeTargets.Class)]
public class SekibanEventTypeAttribute : Attribute { }
public class RegisteredEventTypes
{
    private readonly List<Type> _registeredTypes = new();
    public ReadOnlyCollection<Type> RegisteredTypes { get; }

    public RegisteredEventTypes(params Assembly[] assemblies)
    {
        var attributeType = typeof(SekibanEventTypeAttribute);
        foreach (var assembly in assemblies)
        {
            var decoratedTypes = assembly.DefinedTypes.Where(
                x => x.IsClass &&
                    (x.CustomAttributes.Any(a => a.AttributeType == attributeType) ||
                        (x.BaseType?.CustomAttributes.Any(a => a.AttributeType == attributeType) ??
                            false) ||
                        (x.BaseType?.BaseType?.CustomAttributes.Any(
                                a => a.AttributeType == attributeType) ??
                            false)
                    )
            );
            foreach (var type in decoratedTypes)
            {
                _registeredTypes.Add(type);
            }
        }
        RegisteredTypes = _registeredTypes.AsReadOnly();
    }
}
