namespace Sekiban.EventSourcing.Shared;

public static class SekibanJsonHelper
{
    public static JsonSerializerOptions GetDefaultJsonSerializerOptions() => new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    public static string? Serialize(dynamic? obj)
    {
        if (obj is null)
            return null;

        return JsonSerializer.Serialize(obj, GetDefaultJsonSerializerOptions());
    }

    public static string? Serialize(Exception ex)
    {
        return Serialize(new
        {
            ex.Message,
            ex.Source,
            StackTrace = ex.StackTrace?.ToString(),
        });
    }

    public static object? Deserialize(string? jsonString, Type returnType)
    {
        if (string.IsNullOrEmpty(jsonString))
            return default;

        return JsonSerializer.Deserialize(jsonString, returnType);
    }

    public static T? Deserialize<T>(string? jsonString)
    {
        if (string.IsNullOrEmpty(jsonString))
            return default;

        return JsonSerializer.Deserialize<T>(jsonString, GetDefaultJsonSerializerOptions());
    }

    public static object? ConvertTo(dynamic? jsonObj, Type convertionType)
    {
        if (jsonObj is null)
            return default;

        var jsonString = Serialize(jsonObj);
        return Deserialize(jsonString, convertionType);
    }

    public static T? ConvertTo<T>(dynamic? jsonObj)
    {
        if (jsonObj is null)
            return default;

        var jsonString = Serialize(jsonObj);
        return Deserialize<T>(jsonString);
    }

    public static T? GetValue<T>(dynamic? jsonObj, string propertyName)
    {
        var jsonString = Serialize(jsonObj);
        return JsonNode.Parse(jsonString,
            new JsonNodeOptions()
            {
                PropertyNameCaseInsensitive = true,
            })
            ?[propertyName]?.GetValue<T>();
    }
}
