﻿namespace Sekiban.Core.Shared;

public static class SekibanJsonHelper
{
    public static JsonSerializerOptions GetDefaultJsonSerializerOptions()
    {
        return new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNameCaseInsensitive = true };
    }

    public static string? Serialize(dynamic? obj)
    {
        if (obj is null)
        {
            return null;
        }

        return JsonSerializer.Serialize(obj, GetDefaultJsonSerializerOptions());
    }

    public static string? Serialize(Exception ex)
    {
        // System.Text.JsonはException型を直接シリアライズできないので、匿名型にしてからシリアライズする
        return Serialize(new { ex.Message, ex.Source, ex.StackTrace });
    }

    public static object? Deserialize(string? jsonString, Type returnType)
    {
        if (string.IsNullOrEmpty(jsonString))
        {
            return default;
        }

        return JsonSerializer.Deserialize(jsonString, returnType, GetDefaultJsonSerializerOptions());
    }

    public static T? Deserialize<T>(string? jsonString)
    {
        if (string.IsNullOrEmpty(jsonString))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(jsonString, GetDefaultJsonSerializerOptions());
    }

    public static object? ConvertTo(dynamic? jsonObj, Type convertionType)
    {
        if (jsonObj is null)
        {
            return default;
        }

        var jsonString = Serialize(jsonObj);
        return Deserialize(jsonString, convertionType);
    }

    public static T? ConvertTo<T>(dynamic? jsonObj)
    {
        if (jsonObj is null)
        {
            return default;
        }

        var jsonString = Serialize(jsonObj);
        return Deserialize<T>(jsonString);
    }

    public static T? GetValue<T>(dynamic? jsonObj, string propertyName)
    {
        return GetValue<T>(Serialize(jsonObj), propertyName);
    }

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