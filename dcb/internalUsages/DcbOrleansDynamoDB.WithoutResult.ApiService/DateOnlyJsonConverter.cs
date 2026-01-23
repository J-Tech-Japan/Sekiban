using System.Text.Json;
using System.Text.Json.Serialization;
namespace DcbOrleansDynamoDB.WithoutResult.ApiService;

/// <summary>
///     JSON converter for DateOnly type
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
        throw new JsonException("Unable to parse DateOnly from JSON value");
    }

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(_format));
    }
}
