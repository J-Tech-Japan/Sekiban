using System.Text.Json;

namespace Sekiban.Pure.Postgres;

public class PostgresDbSerializerOptions
{
    public static JsonSerializerOptions CreateDefaultOptions() =>
        new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
}
