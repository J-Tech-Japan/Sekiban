using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace Dcb.Cli;

/// <summary>
///     Connection identity for the local cache, and its non-secret description.
///     - The cache folder name is an HMAC-SHA256 (key stored in the cache root) of everything that can change the
///       source: database type, the full connection string as given, the Cosmos database name or SQLite path, and the
///       PG* environment variables that libpq-style clients read. Any difference gives a separate cache, including a
///       password change. Secrets never appear in the folder name.
///     - The description (console output, cache metadata) only contains allowlisted values; Options and secrets are
///       never shown.
/// </summary>
internal static class ConnectionIdentity
{
    private const string LegacySourceNotShown = "(legacy cache; source not shown)";

    // Environment variables that can change the PostgreSQL target or session (Npgsql 10 reads PGAPPNAME,
    // PGCLIENTENCODING, PGGSSENCMODE, PGOPTIONS, PGPASSFILE, PGPASSWORD, PGREQUIREAUTH, PGSSL*, PGTARGETSESSIONATTRS,
    // PGTZ and PGUSER; the libpq ones PGHOST, PGPORT, PGDATABASE, PGSERVICE and PGSERVICEFILE are included as well).
    private static readonly string[] PostgresEnvironmentVariables =
    [
        "PGAPPNAME", "PGCLIENTENCODING", "PGDATABASE", "PGGSSENCMODE", "PGHOST", "PGOPTIONS", "PGPASSFILE",
        "PGPASSWORD", "PGPORT", "PGREQUIREAUTH", "PGSERVICE", "PGSERVICEFILE", "PGSSLCERT", "PGSSLKEY",
        "PGSSLNEGOTIATION", "PGSSLROOTCERT", "PGTARGETSESSIONATTRS", "PGTZ", "PGUSER"
    ];

    /// <summary>
    ///     Allowlisted values for display, sorted by key. Throws <see cref="CliException" /> when the connection string
    ///     cannot be parsed. Postgres: resolved by Npgsql (host, port, database, username, search path); Options are
    ///     never shown. Cosmos: AccountEndpoint as scheme://host:port/path (no user info, query or fragment).
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> GetPublicParts(string databaseType, string connectionString)
    {
        var parts = databaseType == "postgres"
            ? GetPostgresParts(connectionString)
            : GetGenericParts(connectionString);
        return parts
            .Where(pair => !string.IsNullOrEmpty(pair.Value))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<KeyValuePair<string, string>> GetPostgresParts(string connectionString)
    {
        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw new CliException("The PostgreSQL connection string could not be parsed.");
        }

        return
        [
            new("host", builder.Host ?? ""),
            new("port", builder.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("database", builder.Database ?? ""),
            new("username", builder.Username ?? ""),
            new("searchpath", builder.SearchPath ?? "")
        ];
    }

    private static IEnumerable<KeyValuePair<string, string>> GetGenericParts(string connectionString)
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

        if (builder.TryGetValue("AccountEndpoint", out var endpoint))
        {
            return [new("accountendpoint", NormalizeEndpoint(Convert.ToString(endpoint)) ?? "")];
        }
        return [];
    }

    // scheme://host[:port]/path of an absolute URI; null (value dropped) when it is not one.
    private static string? NormalizeEndpoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            return null;
        }
        return uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);
    }

    /// <summary>
    ///     Safe form of a source stored in cache metadata. New caches store <see cref="Describe" /> output (or a SQLite
    ///     path); legacy caches stored the first 50 characters of the raw connection string, which may contain secrets.
    ///     Only the keys host, port, database, username, searchpath and accountendpoint are shown.
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
            var text = stored.EndsWith("...", StringComparison.Ordinal) ? stored[..^3] : stored;
            var builder = new DbConnectionStringBuilder { ConnectionString = text };
            var shown = new List<KeyValuePair<string, string>>();
            foreach (var key in new[] { "host", "port", "database", "username", "searchpath" })
            {
                if (builder.TryGetValue(key, out var value))
                {
                    shown.Add(new(key, Convert.ToString(value) ?? ""));
                }
            }
            if (builder.TryGetValue("accountendpoint", out var endpoint) && NormalizeEndpoint(Convert.ToString(endpoint)) is { } safeEndpoint)
            {
                shown.Add(new("accountendpoint", safeEndpoint));
            }
            return shown.Count == 0
                ? LegacySourceNotShown
                : Serialize(shown.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        }
        catch (ArgumentException)
        {
            return LegacySourceNotShown;
        }
    }

    // key=value pairs in the given order, quoted where needed (round-trips through DbConnectionStringBuilder).
    private static string Serialize(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        var builder = new DbConnectionStringBuilder();
        foreach (var (key, value) in pairs)
        {
            builder[key] = value;
        }
        return builder.ConnectionString;
    }

    /// <summary>Human readable description without secrets (sqlite: the full file path).</summary>
    public static string Describe(string databaseType, string connectionString, string cosmosDatabaseName)
    {
        if (databaseType == "sqlite")
        {
            return connectionString;
        }

        var parts = GetPublicParts(databaseType, connectionString).ToList();
        if (databaseType == "cosmos")
        {
            parts.Add(new("database", cosmosDatabaseName));
        }
        return Serialize(parts);
    }

    /// <summary>
    ///     Cache folder name "&lt;database type&gt;-&lt;16 hex&gt;": HMAC-SHA256 with <paramref name="key" /> over a JSON array
    ///     of [name, value] pairs (database type, connection string as given, Cosmos database name or SQLite path, and
    ///     for Postgres the PG* environment variables).
    /// </summary>
    public static string ComputeKey(string databaseType, string connectionString, string cosmosDatabaseName, byte[] key)
    {
        var pairs = new List<string?[]> { new[] { "type", databaseType }, new[] { "connection", connectionString } };
        if (databaseType == "cosmos")
        {
            pairs.Add(["cosmosdatabase", cosmosDatabaseName]);
        }
        if (databaseType == "postgres")
        {
            pairs.AddRange(PostgresEnvironmentVariables.Select(name => new[] { $"env:{name}", Environment.GetEnvironmentVariable(name) }));
        }

        var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(pairs)));
        return $"{databaseType}-{Convert.ToHexString(hash)[..16].ToLowerInvariant()}";
    }
}
