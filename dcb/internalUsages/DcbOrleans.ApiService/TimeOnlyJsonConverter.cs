using System.Text.Json;
using System.Text.Json.Serialization;
namespace DcbOrleans.ApiService;

/// <summary>
///     JSON converter for TimeOnly type
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
        throw new JsonException("Unable to parse TimeOnly from JSON value");
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(_format));
    }
}
