using System.Text.Json;
using System.Text.Json.Serialization;
using Sekiban.Pure.Documents;

namespace SharedDomain;

/// <summary>
/// Custom JSON converter for PartitionKeys to allow it to be used as a dictionary key
/// </summary>
public class PartitionKeysJsonConverter : JsonConverter<PartitionKeys>
{
    public override PartitionKeys Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject token");
        }

        string? rootPartitionKey = null;
        string? aggregateGroup = null;
        Guid aggregateId = Guid.Empty;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected PropertyName token");
            }

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "rootPartitionKey":
                    rootPartitionKey = reader.TokenType == JsonTokenType.Null ? string.Empty : reader.GetString();
                    break;
                case "aggregateGroup":
                    aggregateGroup = reader.GetString();
                    break;
                case "aggregateId":
                    aggregateId = reader.GetGuid();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        // Create a new PartitionKeys using the proper constructor
        return new PartitionKeys(aggregateId, aggregateGroup ?? string.Empty, rootPartitionKey ?? string.Empty);
    }

    public override void Write(Utf8JsonWriter writer, PartitionKeys value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        
        var rootPartitionKey = (string?)typeof(PartitionKeys).GetProperty("RootPartitionKey")?.GetValue(value);
        var aggregateGroup = (string?)typeof(PartitionKeys).GetProperty("AggregateGroup")?.GetValue(value);
        var aggregateId = (Guid)typeof(PartitionKeys).GetProperty("AggregateId")?.GetValue(value)!;

        if (rootPartitionKey != null)
        {
            writer.WriteString("rootPartitionKey", rootPartitionKey);
        }
        else
        {
            writer.WriteNull("rootPartitionKey");
        }
        
        writer.WriteString("aggregateGroup", aggregateGroup);
        writer.WriteString("aggregateId", aggregateId);
        
        writer.WriteEndObject();
    }

    public override void WriteAsPropertyName(Utf8JsonWriter writer, PartitionKeys value, JsonSerializerOptions options)
    {
        // Create a string representation of PartitionKeys to use as dictionary key
        var rootPartitionKey = (string?)typeof(PartitionKeys).GetProperty("RootPartitionKey")?.GetValue(value);
        var aggregateGroup = (string?)typeof(PartitionKeys).GetProperty("AggregateGroup")?.GetValue(value);
        var aggregateId = (Guid)typeof(PartitionKeys).GetProperty("AggregateId")?.GetValue(value)!;
        
        var key = $"{rootPartitionKey ?? "null"}|{aggregateGroup}|{aggregateId}";
        writer.WritePropertyName(key);
    }

    public override PartitionKeys ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var key = reader.GetString() ?? throw new JsonException("Expected non-null property name");
        var parts = key.Split('|');
        
        if (parts.Length != 3)
        {
            throw new JsonException("Invalid PartitionKeys format in property name");
        }

        var rootPartitionKey = parts[0] == "null" ? null : parts[0];
        var aggregateGroup = parts[1];
        var aggregateId = Guid.Parse(parts[2]);
        
        // Create a new PartitionKeys using the proper constructor
        return new PartitionKeys(aggregateId, aggregateGroup, rootPartitionKey ?? string.Empty);
    }
}
