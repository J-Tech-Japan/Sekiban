using Google.Protobuf;
using Google.Protobuf.Reflection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;

namespace Sekiban.Pure.Dapr.Serialization;

/// <summary>
/// Helper class for Protobuf serialization and deserialization
/// Manages type mapping and conversion between C# objects and Protobuf messages
/// </summary>
public static class ProtobufHelper
{
    private static readonly ConcurrentDictionary<string, MessageDescriptor> DescriptorCache = new();
    private static readonly ConcurrentDictionary<string, Type> TypeCache = new();
    private static readonly ConcurrentDictionary<Type, MessageParser> ParserCache = new();

    /// <summary>
    /// Serializes a C# object to Protobuf bytes
    /// </summary>
    /// <typeparam name="T">The type of object to serialize</typeparam>
    /// <param name="obj">The object to serialize</param>
    /// <returns>Protobuf serialized bytes</returns>
    public static byte[] Serialize<T>(T obj) where T : class
    {
        if (obj == null)
        {
            return Array.Empty<byte>();
        }

        if (obj is IMessage message)
        {
            return message.ToByteArray();
        }

        throw new InvalidOperationException($"Type {typeof(T).Name} must implement IMessage for Protobuf serialization");
    }

    /// <summary>
    /// Deserializes Protobuf bytes to a C# object
    /// </summary>
    /// <typeparam name="T">The expected type of the deserialized object</typeparam>
    /// <param name="bytes">The Protobuf bytes to deserialize</param>
    /// <returns>The deserialized object</returns>
    public static T Deserialize<T>(byte[] bytes) where T : class
    {
        if (bytes == null || bytes.Length == 0)
        {
            return default!;
        }

        var parser = GetParser<T>();
        if (parser != null)
        {
            return (T)parser.ParseFrom(bytes);
        }

        throw new InvalidOperationException($"No parser found for type {typeof(T).Name}");
    }

    /// <summary>
    /// Deserializes Protobuf bytes to a C# object using type name
    /// </summary>
    /// <param name="typeName">The full type name</param>
    /// <param name="bytes">The Protobuf bytes to deserialize</param>
    /// <returns>The deserialized object</returns>
    public static object? Deserialize(string typeName, byte[] bytes)
    {
        if (string.IsNullOrEmpty(typeName) || bytes == null || bytes.Length == 0)
        {
            return null;
        }

        var type = ResolveType(typeName);
        if (type == null)
        {
            throw new InvalidOperationException($"Cannot resolve type: {typeName}");
        }

        var parser = GetParser(type);
        if (parser != null)
        {
            return parser.ParseFrom(bytes);
        }

        throw new InvalidOperationException($"No parser found for type {typeName}");
    }

    /// <summary>
    /// Gets the message parser for a specific type
    /// </summary>
    private static MessageParser? GetParser<T>() where T : class
    {
        return GetParser(typeof(T));
    }

    /// <summary>
    /// Gets the message parser for a specific type
    /// </summary>
    private static MessageParser? GetParser(Type type)
    {
        if (ParserCache.TryGetValue(type, out var cachedParser))
        {
            return cachedParser;
        }

        // Look for static Parser property (standard Protobuf pattern)
        var parserProperty = type.GetProperty("Parser", BindingFlags.Public | BindingFlags.Static);
        if (parserProperty != null && parserProperty.PropertyType.IsAssignableTo(typeof(MessageParser)))
        {
            var parser = parserProperty.GetValue(null) as MessageParser;
            if (parser != null)
            {
                ParserCache.TryAdd(type, parser);
                return parser;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves a type from its name
    /// </summary>
    private static Type? ResolveType(string typeName)
    {
        if (TypeCache.TryGetValue(typeName, out var cachedType))
        {
            return cachedType;
        }

        // Try to resolve from loaded assemblies
        var type = Type.GetType(typeName);
        if (type == null)
        {
            // Search in all loaded assemblies
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName);
                if (type != null)
                {
                    break;
                }
            }
        }

        if (type != null)
        {
            TypeCache.TryAdd(typeName, type);
        }

        return type;
    }

    /// <summary>
    /// Registers a Protobuf type for faster resolution
    /// </summary>
    public static void RegisterType<T>() where T : class, IMessage
    {
        var type = typeof(T);
        var typeName = type.FullName ?? type.Name;
        TypeCache.TryAdd(typeName, type);
        
        // Pre-cache the parser
        GetParser<T>();
    }

    /// <summary>
    /// Clears all caches
    /// </summary>
    public static void ClearCaches()
    {
        DescriptorCache.Clear();
        TypeCache.Clear();
        ParserCache.Clear();
    }
}

/// <summary>
/// Service for managing Protobuf type mappings between C# domain objects and Protobuf messages
/// </summary>
public interface IProtobufTypeMapper
{
    /// <summary>
    /// Registers a mapping between a domain type and its Protobuf message type
    /// </summary>
    void RegisterMapping<TDomain, TProtobuf>() where TProtobuf : class, IMessage, new();

    /// <summary>
    /// Converts a domain object to its Protobuf representation
    /// </summary>
    byte[] ToProtobuf<TDomain>(TDomain domainObject) where TDomain : class;

    /// <summary>
    /// Converts Protobuf bytes to a domain object
    /// </summary>
    TDomain FromProtobuf<TDomain>(byte[] protobufBytes) where TDomain : class;

    /// <summary>
    /// Converts Protobuf bytes to a domain object using type name
    /// </summary>
    object? FromProtobuf(string domainTypeName, byte[] protobufBytes);
}

/// <summary>
/// Default implementation of IProtobufTypeMapper
/// </summary>
public class ProtobufTypeMapper : IProtobufTypeMapper
{
    private readonly ConcurrentDictionary<Type, Type> _domainToProtobufMap = new();
    private readonly ConcurrentDictionary<Type, Type> _protobufToDomainMap = new();
    private readonly ConcurrentDictionary<Type, Func<object, IMessage>> _converters = new();
    private readonly ConcurrentDictionary<Type, Func<IMessage, object>> _reverseConverters = new();
    private readonly ILogger<ProtobufTypeMapper> _logger;

    public ProtobufTypeMapper(ILogger<ProtobufTypeMapper> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void RegisterMapping<TDomain, TProtobuf>() where TProtobuf : class, IMessage, new()
    {
        var domainType = typeof(TDomain);
        var protobufType = typeof(TProtobuf);

        _domainToProtobufMap.TryAdd(domainType, protobufType);
        _protobufToDomainMap.TryAdd(protobufType, domainType);

        // Register the Protobuf type for faster resolution
        ProtobufHelper.RegisterType<TProtobuf>();

        _logger.LogDebug("Registered mapping: {DomainType} <-> {ProtobufType}", 
            domainType.Name, protobufType.Name);
    }

    public byte[] ToProtobuf<TDomain>(TDomain domainObject) where TDomain : class
    {
        if (domainObject == null)
        {
            return Array.Empty<byte>();
        }

        var domainType = typeof(TDomain);
        
        // If the domain object is already a Protobuf message, serialize directly
        if (domainObject is IMessage message)
        {
            return message.ToByteArray();
        }

        // Look for registered mapping
        if (_domainToProtobufMap.TryGetValue(domainType, out var protobufType))
        {
            // For now, we assume the domain object can be directly cast or has a converter
            // In a real implementation, you would have converters registered
            throw new NotImplementedException(
                $"Conversion from {domainType.Name} to {protobufType.Name} not implemented. " +
                "Register a converter or ensure the domain object implements IMessage.");
        }

        throw new InvalidOperationException($"No Protobuf mapping found for domain type {domainType.Name}");
    }

    public TDomain FromProtobuf<TDomain>(byte[] protobufBytes) where TDomain : class
    {
        if (protobufBytes == null || protobufBytes.Length == 0)
        {
            return default!;
        }

        var domainType = typeof(TDomain);

        // If the domain type is a Protobuf message, deserialize directly
        if (typeof(IMessage).IsAssignableFrom(domainType))
        {
            return ProtobufHelper.Deserialize<TDomain>(protobufBytes);
        }

        // Look for registered mapping
        if (_domainToProtobufMap.TryGetValue(domainType, out var protobufType))
        {
            // Deserialize to Protobuf message first
            var protobufObject = ProtobufHelper.Deserialize(protobufType.FullName!, protobufBytes);
            
            // Convert to domain object (would use registered converter)
            throw new NotImplementedException(
                $"Conversion from {protobufType.Name} to {domainType.Name} not implemented. " +
                "Register a converter or ensure the domain object implements IMessage.");
        }

        throw new InvalidOperationException($"No Protobuf mapping found for domain type {domainType.Name}");
    }

    public object? FromProtobuf(string domainTypeName, byte[] protobufBytes)
    {
        if (string.IsNullOrEmpty(domainTypeName) || protobufBytes == null || protobufBytes.Length == 0)
        {
            return null;
        }

        var domainType = Type.GetType(domainTypeName);
        if (domainType == null)
        {
            throw new InvalidOperationException($"Cannot resolve domain type: {domainTypeName}");
        }

        // Use reflection to call the generic method
        var method = GetType().GetMethod(nameof(FromProtobuf), new[] { typeof(byte[]) });
        var genericMethod = method!.MakeGenericMethod(domainType);
        return genericMethod.Invoke(this, new object[] { protobufBytes });
    }
}