using Sekiban.Core.Types;
using System.Reflection;
namespace Sekiban.Core.Events;

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
