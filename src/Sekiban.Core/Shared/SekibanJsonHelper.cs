using Sekiban.Core.Documents;
using Sekiban.Core.Events;
namespace Sekiban.Core.Shared;

/// <summary>
///     Sekiban Json Helper.
///     Uses for the event serialization and deserialization.
/// </summary>
public static class SekibanJsonHelper
{
    /// <summary>
    ///     default JsonSerializerOptions
    /// </summary>
    /// <returns></returns>
    public static JsonSerializerOptions GetDefaultJsonSerializerOptions() =>
        new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNameCaseInsensitive = true };

    /// <summary>
    ///     serialize dynamic object to json string
    /// </summary>
    /// <param name="obj"></param>
    /// <returns></returns>
    public static string? Serialize(dynamic? obj)
    {
        if (obj is null)
        {
            return null;
        }

        return JsonSerializer.Serialize(obj, GetDefaultJsonSerializerOptions());
    }

    /// <summary>
    ///     Serialize Exception to json string
    /// </summary>
    /// <param name="ex"></param>
    /// <returns></returns>
    public static string? Serialize(Exception ex) =>
        // System.Text.Json cannot directly serialize Exception types, so it serializes them after converting to an anonymous type.
        Serialize(new { ex.Message, ex.Source, ex.StackTrace });

    /// <summary>
    ///     Deserialize json string to object
    /// </summary>
    /// <param name="jsonString"></param>
    /// <param name="returnType"></param>
    /// <returns></returns>
    public static object? Deserialize(string? jsonString, Type returnType)
    {
        if (string.IsNullOrEmpty(jsonString))
        {
            return default;
        }

        return JsonSerializer.Deserialize(jsonString, returnType, GetDefaultJsonSerializerOptions());
    }

    /// <summary>
    ///     Deserialize typed object from json string
    /// </summary>
    /// <param name="jsonString"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static T? Deserialize<T>(string? jsonString)
    {
        if (string.IsNullOrEmpty(jsonString))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(jsonString, GetDefaultJsonSerializerOptions());
    }

    /// <summary>
    ///     Deserialize events from json string
    /// </summary>
    /// <param name="jsonElement"></param>
    /// <param name="registeredTypes"></param>
    /// <returns></returns>
    public static IEvent? DeserializeToEvent(JsonElement jsonElement, ReadOnlyCollection<Type> registeredTypes)
    {
        if (GetValue<string>(jsonElement, nameof(IDocument.DocumentTypeName)) is not { } typeName)
        {
            return null;
        }

        return registeredTypes.Where(m => m.Name == typeName)
                .Select(m => ConvertTo(jsonElement, typeof(Event<>).MakeGenericType(m)) as IEvent)
                .FirstOrDefault(m => m is not null) ??
            EventHelper.GetUnregisteredEvent(jsonElement);
    }

    /// <summary>
    ///     converts json object to specific type
    /// </summary>
    /// <param name="jsonObj"></param>
    /// <param name="convertionType"></param>
    /// <returns></returns>
    public static object? ConvertTo(dynamic? jsonObj, Type convertionType)
    {
        if (jsonObj is null)
        {
            return default;
        }

        var jsonString = Serialize(jsonObj);
        return Deserialize(jsonString, convertionType);
    }
    /// <summary>
    ///     converts json object to specific type using generic type
    /// </summary>
    /// <param name="jsonObj"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static T? ConvertTo<T>(dynamic? jsonObj)
    {
        if (jsonObj is null)
        {
            return default;
        }

        var jsonString = Serialize(jsonObj);
        return Deserialize<T>(jsonString);
    }

    /// <summary>
    ///     Get Value from Json Element
    /// </summary>
    /// <param name="jsonObj"></param>
    /// <param name="propertyName"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static T? GetValue<T>(dynamic? jsonObj, string propertyName) => GetValue<T>(Serialize(jsonObj), propertyName);

    /// <summary>
    ///     Get Value from Json Element
    /// </summary>
    /// <param name="jsonString"></param>
    /// <param name="propertyName"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static T? GetValue<T>(string? jsonString, string propertyName)
    {
        if (jsonString is null)
        {
            return default;
        }

        var node = JsonNode.Parse(jsonString, new JsonNodeOptions { PropertyNameCaseInsensitive = true })?[propertyName];
        if (node is null)
        {
            return default;
        }

        return node.GetValue<T>();
    }
}
