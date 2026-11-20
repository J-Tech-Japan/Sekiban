using System.Text.Json;

namespace DcbOrleans.Web;

public record CommandResponse(bool Success, Guid? EventId, Guid? AggregateId, string? Error, string? SortableUniqueId);

/// <summary>
///     Turns an API error body into a readable message: ProblemDetails <c>errors</c> (validation, per field),
///     then <c>detail</c>, <c>error</c> or <c>title</c>, else the raw body.
/// </summary>
public static class ApiErrorReader
{
    public static async Task<string> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                {
                    var messages = errors.EnumerateObject()
                        .SelectMany(field => field.Value.ValueKind == JsonValueKind.Array
                            ? field.Value.EnumerateArray().Select(message => $"{field.Name}: {message}")
                            : new[] { $"{field.Name}: {field.Value}" })
                        .ToList();
                    if (messages.Count > 0)
                    {
                        return string.Join(" ", messages);
                    }
                }

                foreach (var name in new[] { "detail", "error", "title" })
                {
                    if (root.TryGetProperty(name, out var value) &&
                        value.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(value.GetString()))
                    {
                        return value.GetString()!;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON: fall through to the raw body.
        }

        return string.IsNullOrWhiteSpace(body) ? $"Request failed with status {(int)response.StatusCode}" : body;
    }
}
