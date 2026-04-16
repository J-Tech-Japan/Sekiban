namespace Sekiban.Dcb.MaterializedView;

public sealed class MvOptions
{
    public const string DefaultTablePrefix = "sekiban_mv";
    public static readonly TimeSpan DefaultStreamReorderWindow = TimeSpan.FromSeconds(1);

    public int BatchSize { get; set; } = 100;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan StreamReorderWindow { get; set; } = DefaultStreamReorderWindow;
    public int SafeWindowMs { get; set; } = 5000;
    public int MaxConsecutiveFailuresBeforeStop { get; set; } = 3;
    public string TablePrefix { get; set; } = DefaultTablePrefix;
    public PhysicalNameResolver? PhysicalNameResolver { get; set; }
}

public static class MvSchemaHelper
{
    public const string LastSortableUniqueIdColumn = "_last_sortable_unique_id";
    public const string LastAppliedAtColumn = "_last_applied_at";

    public static string MetadataColumnsSql(string timestampSql = "NOW()") =>
        $"{LastSortableUniqueIdColumn} TEXT NOT NULL, {LastAppliedAtColumn} TIMESTAMPTZ NOT NULL DEFAULT {timestampSql}";
}

public static class MvPhysicalName
{
    private const int MaximumIdentifierLength = 63;

    public static string Resolve(MvOptions options, string viewName, int viewVersion, string logicalTable)
    {
        var resolver = options.PhysicalNameResolver ?? ((view, version, logical) =>
            $"{SanitizeSegment(options.TablePrefix)}_{SanitizeSegment(view)}_v{version}_{SanitizeSegment(logical)}");
        var resolved = resolver(viewName, viewVersion, logicalTable);
        ValidateIdentifier(resolved);
        return resolved;
    }

    public static string SanitizeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Identifier segment cannot be empty.", nameof(value));
        }

        var buffer = new List<char>(value.Length);
        var previousWasUnderscore = false;
        foreach (var current in value.Trim().ToLowerInvariant())
        {
            var normalized = char.IsLetterOrDigit(current) ? current : '_';
            if (normalized == '_' && previousWasUnderscore)
            {
                continue;
            }

            buffer.Add(normalized);
            previousWasUnderscore = normalized == '_';
        }

        var sanitized = new string(buffer.ToArray()).Trim('_');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            throw new ArgumentException("Identifier segment becomes empty after sanitization.", nameof(value));
        }

        return sanitized;
    }

    public static void ValidateIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("Identifier cannot be empty.", nameof(identifier));
        }

        if (identifier.Length > MaximumIdentifierLength)
        {
            throw new ArgumentException(
                $"Identifier '{identifier}' exceeds PostgreSQL's {MaximumIdentifierLength} character limit.",
                nameof(identifier));
        }

        if (!(char.IsLetter(identifier[0]) || identifier[0] == '_'))
        {
            throw new ArgumentException(
                $"Identifier '{identifier}' must start with a letter or underscore.",
                nameof(identifier));
        }

        if (identifier.Any(character => !(char.IsLetterOrDigit(character) || character == '_')))
        {
            throw new ArgumentException(
                $"Identifier '{identifier}' contains characters outside [A-Za-z0-9_].",
                nameof(identifier));
        }
    }
}
