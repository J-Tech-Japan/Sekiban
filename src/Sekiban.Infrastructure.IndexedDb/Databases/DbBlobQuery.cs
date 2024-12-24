namespace Sekiban.Infrastructure.IndexedDb.Databases;

public record DbBlobQuery
{
    public string? Name { get; init; }

    public int? MaxCount { get; init; }

    public static DbBlobQuery ForName(string name) =>
        new()
        {
            Name = name,
            MaxCount = 1,
        };
}
