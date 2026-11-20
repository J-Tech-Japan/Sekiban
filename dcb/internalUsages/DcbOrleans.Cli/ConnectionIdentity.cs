using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

/// <summary>
///     Non-secret identity of a database connection, used for console output, cache metadata and the cache folder name.
///     Only an allowlist of values is kept; everything else (passwords, account keys, shared access signatures, tokens,
///     other Options switches, ...) is dropped from the description and from the hash input.
/// </summary>
internal static class ConnectionIdentity
{
    private const string LegacySourceNotShown = "(legacy cache; source not shown)";

    /// <summary>
    ///     Allowlisted values as canonical key/value pairs, sorted by key. Throws <see cref="CliException" /> when the
    ///     connection string cannot be parsed.
    ///     Postgres: parsed by Npgsql itself, so aliases (Server/Host, DB/Database, User Id/Username, SearchPath) and
    ///     repeated keys resolve exactly as the provider resolves them (the last one wins). The search path comes from
    ///     Search Path, or else from a search_path switch in Options; Search Path wins because PostgreSQL applies
    ///     startup parameters after the Options switches. All other Options content is dropped.
    ///     Cosmos: only AccountEndpoint (scheme, host, port and path; no user info, query or fragment).
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

        var searchPath = !string.IsNullOrEmpty(builder.SearchPath)
            ? builder.SearchPath
            : GetSearchPathFromOptions(builder.Options);
        return
        [
            new("host", builder.Host ?? ""),
            new("port", builder.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("database", builder.Database ?? ""),
            new("username", builder.Username ?? ""),
            new("searchpath", searchPath ?? "")
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

    // Last search_path set in an Npgsql Options string ("-c search_path=x", "-csearch_path=x" or "--search_path=x").
    private static string? GetSearchPathFromOptions(string? options)
    {
        if (string.IsNullOrWhiteSpace(options))
        {
            return null;
        }

        string? result = null;
        var tokens = options.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            string? setting = tokens[i] switch
            {
                "-c" when i + 1 < tokens.Length => tokens[++i],
                var t when t.StartsWith("--", StringComparison.Ordinal) => t[2..],
                var t when t.StartsWith("-c", StringComparison.Ordinal) => t[2..],
                _ => null
            };
            if (setting is null)
            {
                continue;
            }
            var eq = setting.IndexOf('=');
            if (eq > 0 && setting[..eq].Replace('-', '_').Equals("search_path", StringComparison.OrdinalIgnoreCase))
            {
                result = setting[(eq + 1)..];
            }
        }
        return result;
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

    /// <summary>True when the connection names what it reads (Postgres: a database; Cosmos: an account endpoint).</summary>
    public static bool IdentifiesDatabase(string databaseType, string connectionString)
    {
        var required = databaseType switch
        {
            "postgres" => "database",
            "cosmos" => "accountendpoint",
            _ => null
        };
        return required == null || GetPublicParts(databaseType, connectionString).Any(pair => pair.Key == required);
    }

    /// <summary>Throws unless <see cref="IdentifiesDatabase" />; required before a cache is read or written.</summary>
    public static void EnsureIdentifiesDatabase(string databaseType, string connectionString)
    {
        if (!IdentifiesDatabase(databaseType, connectionString))
        {
            throw new CliException(
                $"The {databaseType} connection string has no {(databaseType == "postgres" ? "Database (or DB)" : "AccountEndpoint")} value, " +
                "so its cache cannot be identified. Add it to the connection string or use --cache-mode off.");
        }
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
            var shown = new List<string>();
            foreach (var key in new[] { "host", "port", "database", "username", "searchpath" })
            {
                if (builder.TryGetValue(key, out var value))
                {
                    shown.Add($"{key}={value}");
                }
            }
            if (builder.TryGetValue("accountendpoint", out var endpoint) && NormalizeEndpoint(Convert.ToString(endpoint)) is { } safeEndpoint)
            {
                shown.Add($"accountendpoint={safeEndpoint}");
            }
            return shown.Count == 0 ? LegacySourceNotShown : string.Join(";", shown);
        }
        catch (ArgumentException)
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

        var description = string.Join(";", GetPublicParts(databaseType, connectionString).Select(pair => $"{pair.Key}={pair.Value}"));
        if (databaseType != "cosmos")
        {
            return description;
        }
        return description.Length == 0 ? $"database={cosmosDatabaseName}" : $"{description};database={cosmosDatabaseName}";
    }

    /// <summary>
    ///     Cache folder name: "&lt;database type&gt;-&lt;16 hex&gt;". The hash input is a JSON array of [key, value] pairs built
    ///     from the resolved, allowlisted values, so quoting cannot make two different connections collide.
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
            pairs.AddRange(GetPublicParts(databaseType, connectionString).Select(pair => new[] { pair.Key, pair.Value }));
            if (databaseType == "cosmos")
            {
                pairs.Add(["cosmosdatabase", cosmosDatabaseName]);
            }
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(pairs)));
        return $"{databaseType}-{Convert.ToHexString(hash)[..16].ToLowerInvariant()}";
    }
}
