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
    private static readonly HashSet<string> AllowedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "host", "server", "data source", "datasource", "address", "port",
        "database", "initial catalog", "dbname",
        "username", "user name", "user id", "userid", "uid",
        "accountendpoint", "serviceurl", "service url", "region", "service id", "serviceid"
    };

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
            .Where(AllowedKeys.Contains)
            .Select(key => new KeyValuePair<string, string>(
                key.ToLowerInvariant(),
                RemoveUrlSecrets(Convert.ToString(builder[key]) ?? "")))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ThenBy(pair => pair.Value, StringComparer.Ordinal)
            .ToList();
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
