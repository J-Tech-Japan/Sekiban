using System.Collections.Concurrent;
namespace Sekiban.Pure.Dapr.Serialization;

/// <summary>
///     Default implementation of type registry for Dapr serialization
/// </summary>
public class DaprTypeRegistry : IDaprTypeRegistry
{
    private readonly ConcurrentDictionary<string, Type> _aliasToType = new();
    private readonly ConcurrentDictionary<Type, string> _typeToAlias = new();

    public void RegisterType<T>(string typeAlias) where T : class
    {
        RegisterType(typeof(T), typeAlias);
    }

    public void RegisterType(Type type, string typeAlias)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrEmpty(typeAlias);

        if (!_aliasToType.TryAdd(typeAlias, type))
        {
            var existingType = _aliasToType[typeAlias];
            if (existingType != type)
            {
                throw new InvalidOperationException(
                    $"Type alias '{typeAlias}' is already registered for type '{existingType.FullName}'");
            }
        }

        if (!_typeToAlias.TryAdd(type, typeAlias))
        {
            var existingAlias = _typeToAlias[type];
            if (existingAlias != typeAlias)
            {
                throw new InvalidOperationException(
                    $"Type '{type.FullName}' is already registered with alias '{existingAlias}'");
            }
        }
    }

    public Type? ResolveType(string typeAlias)
    {
        ArgumentException.ThrowIfNullOrEmpty(typeAlias);
        return _aliasToType.TryGetValue(typeAlias, out var type) ? type : null;
    }

    public string GetTypeAlias(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (_typeToAlias.TryGetValue(type, out var alias))
        {
            return alias;
        }

        // Fallback to full name if not registered
        return type.FullName ?? type.Name;
    }

    public bool IsTypeRegistered(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _typeToAlias.ContainsKey(type);
    }

    public bool IsAliasRegistered(string typeAlias)
    {
        ArgumentException.ThrowIfNullOrEmpty(typeAlias);
        return _aliasToType.ContainsKey(typeAlias);
    }
}
