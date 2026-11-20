using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

/// <summary>
///     Non-secret identity of a database connection, used for console output, cache metadata and the cache folder name.
///     Only allowlisted keys (host, port, database, user, endpoint, ...) are kept; every other key (passwords, account
///     keys, shared access signatures, tokens, ...) is dropped. Query strings and fragments are removed from URL values,
///     so SAS tokens in endpoints are not kept either.
/// </summary>
internal static class ConnectionIdentity
{
    // Allowlisted keys (spaces removed, lower case) mapped to one canonical name, so aliases such as Npgsql's
    // Server/Host, DB/Database, User Id/Username or SearchPath/Search Path give the same identity.
    private static readonly Dictionary<string, string> AllowedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["host"] = "host", ["server"] = "host", ["address"] = "host", ["datasource"] = "host",
        ["port"] = "port",
        ["database"] = "database", ["db"] = "database", ["initialcatalog"] = "database", ["dbname"] = "database",
        ["username"] = "username", ["userid"] = "username", ["uid"] = "username",
        // Schema selection: Sekiban's Postgres provider uses unqualified table names, so these select the data.
        ["searchpath"] = "searchpath", ["options"] = "options",
        ["accountendpoint"] = "accountendpoint", ["serviceurl"] = "serviceurl", ["region"] = "region", ["serviceid"] = "serviceid"
    };

    private const string LegacySourceNotShown = "(legacy cache; source not shown)";

    /// <summary>Allowlisted key/value pairs (lower-case keys, sorted). Throws <see cref="CliException" /> when unparsable.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> GetPublicParts(string connectionString)
    {
        DbConnectionStringBuilder builder;
        try
        {
            builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        }
        catch (ArgumentException)
        {
            throw new CliException("The connection string could not be parsed.");
        }

        return builder.Keys.Cast<string>()
            .Select(key => (Key: key, Canonical: AllowedKeys.GetValueOrDefault(key.Replace(" ", ""))))
            .Where(entry => entry.Canonical != null)
            .Select(entry => new KeyValuePair<string, string>(
                entry.Canonical!,
                RemoveUrlSecrets(Convert.ToString(builder[entry.Key]) ?? "")))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ThenBy(pair => pair.Value, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    ///     Throws unless the connection names what it reads (Postgres: a database; Cosmos: an account endpoint).
    ///     Required before a cache is used or filled, so caches of different databases are never mixed.
    /// </summary>
    public static void EnsureIdentifiesDatabase(string databaseType, string connectionString)
    {
        var required = databaseType switch
        {
            "postgres" => "database",
            "cosmos" => "accountendpoint",
            _ => null
        };
        if (required != null && GetPublicParts(connectionString).All(pair => pair.Key != required))
        {
            throw new CliException(
                $"The {databaseType} connection string has no {(required == "database" ? "Database (or DB)" : "AccountEndpoint")} value, " +
                "so its cache cannot be identified. Add it to the connection string or use --cache-mode off.");
        }
    }

    /// <summary>
    ///     Safe form of a source stored in cache metadata. New caches store <see cref="Describe" /> output (or a SQLite
    ///     path); legacy caches stored the first 50 characters of the raw connection string, which may contain secrets.
    /// </summary>
    public static string DescribeStoredSource(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return LegacySourceNotShown;
        }
        if (!stored.Contains('=') && !stored.Contains(';'))
        {
            // SQLite source path
            return stored;
        }
        try
        {
            var parts = GetPublicParts(stored.EndsWith("...", StringComparison.Ordinal) ? stored[..^3] : stored);
            return parts.Count == 0
                ? LegacySourceNotShown
                : string.Join(";", parts.Select(pair => $"{pair.Key}={pair.Value}"));
        }
        catch (CliException)
        {
            return LegacySourceNotShown;
        }
    }

    /// <summary>Human readable description without secrets (sqlite: the full file path).</summary>
    public static string Describe(string databaseType, string connectionString, string cosmosDatabaseName)
    {
        if (databaseType == "sqlite")
        {
            return connectionString;
        }

        var description = string.Join(";", GetPublicParts(connectionString).Select(pair => $"{pair.Key}={pair.Value}"));
        return databaseType == "cosmos" ? $"{description};database={cosmosDatabaseName}" : description;
    }

    /// <summary>
    ///     Cache folder name: "&lt;database type&gt;-&lt;16 hex&gt;". The hash input is a JSON array of [key, value] pairs built
    ///     from the parsed, allowlisted values, so quoting cannot make two different connections collide.
    /// </summary>
    public static string ComputeKey(string databaseType, string connectionString, string cosmosDatabaseName)
    {
        var pairs = new List<string[]> { new[] { "type", databaseType } };
        if (databaseType == "sqlite")
        {
            pairs.Add(["path", connectionString]);
        }
        else
        {
            pairs.AddRange(GetPublicParts(connectionString).Select(pair => new[] { pair.Key, pair.Value }));
            if (databaseType == "cosmos")
            {
                pairs.Add(["cosmosdatabase", cosmosDatabaseName]);
            }
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(pairs)));
        return $"{databaseType}-{Convert.ToHexString(hash)[..16].ToLowerInvariant()}";
    }

    private static string RemoveUrlSecrets(string value)
    {
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            return value;
        }

        var end = value.IndexOfAny(['?', '#']);
        var withoutQuery = end >= 0 ? value[..end] : value;
        // Drop user info ("scheme://user:secret@host") as well.
        var schemeEnd = withoutQuery.IndexOf("://", StringComparison.Ordinal) + 3;
        var at = withoutQuery.IndexOf('@', schemeEnd);
        var slash = withoutQuery.IndexOf('/', schemeEnd);
        if (at >= 0 && (slash < 0 || at < slash))
        {
            withoutQuery = withoutQuery[..schemeEnd] + withoutQuery[(at + 1)..];
        }
        return withoutQuery;
    }
}
