using System.Text.Json;
using System.Text.Json.Serialization;

namespace DcbOrleans.ApiService;

/// <summary>
/// JSON converter for DateOnly type
/// </summary>
public class DateOnlyJsonConverter : JsonConverter<DateOnly>
{
    private readonly string _format = "yyyy-MM-dd";

    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            if (DateOnly.TryParseExact(value, _format, out var date))
            {
                return date;
            }
        }
        throw new JsonException($"Unable to parse DateOnly from JSON value");
    }

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(_format));
    }
}

/// <summary>
/// JSON converter for TimeOnly type
/// </summary>
public class TimeOnlyJsonConverter : JsonConverter<TimeOnly>
{
    private readonly string _format = "HH:mm:ss";

    public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            if (TimeOnly.TryParseExact(value, _format, out var time))
            {
                return time;
            }
        }
        throw new JsonException($"Unable to parse TimeOnly from JSON value");
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(_format));
    }
}
