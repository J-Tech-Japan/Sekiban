namespace DcbLib.Tags;

/// <summary>
/// Details about a tag write operation
/// </summary>
public record TagWriteResult(
    string Tag,
    long Version,
    DateTimeOffset WrittenAt
);