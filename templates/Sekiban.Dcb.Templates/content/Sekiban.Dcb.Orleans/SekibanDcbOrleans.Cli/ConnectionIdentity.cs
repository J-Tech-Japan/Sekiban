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

    // Marker pair: the identity cannot be determined (Options / PGOPTIONS could not be parsed with certainty).
    // Never part of a hash: a connection with this marker gets no cache.
    private const string UndeterminedKey = "options";
    private const string UndeterminedValue = "(not shown; not parsed)";

    /// <summary>
    ///     Allowlisted values as canonical key/value pairs, sorted by key. Throws <see cref="CliException" /> when the
    ///     connection string cannot be parsed.
    ///     Postgres: parsed by Npgsql itself, so aliases (Server/Host, DB/Database, User Id/Username, SearchPath) and
    ///     repeated keys resolve exactly as the provider resolves them (the last one wins). The search path comes from
    ///     Search Path, or else from a search_path switch in the effective options (Options, else the PGOPTIONS
    ///     environment variable); Search Path wins because PostgreSQL applies startup parameters after the options.
    ///     Options are only used when they parse with certainty (see <see cref="TryGetSearchPathFromOptions" />);
    ///     otherwise the identity is "not determinable" (no cache is used) and no part of them is shown.
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

        var parts = new List<KeyValuePair<string, string>>
        {
            new("host", builder.Host ?? ""),
            new("port", builder.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("database", builder.Database ?? ""),
            new("username", builder.Username ?? "")
        };

        // Npgsql sends PGOPTIONS when the connection string has no Options.
        var options = !string.IsNullOrEmpty(builder.Options)
            ? builder.Options
            : Environment.GetEnvironmentVariable("PGOPTIONS");
        if (!TryGetSearchPathFromOptions(options, out var optionsSearchPath))
        {
            parts.Add(new(UndeterminedKey, UndeterminedValue));
        }
        parts.Add(new("searchpath", !string.IsNullOrEmpty(builder.SearchPath) ? builder.SearchPath : optionsSearchPath ?? ""));
        return parts;
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

    // Fail-safe parser for the (libpq style) options string. It succeeds only when the string has no backslash and
    // consists solely of whitespace-separated "-c name=value" or "--name=value" switches; searchPath is the last
    // search_path value (null when none). Anything else returns false, so the identity is treated as undeterminable.
    private static bool TryGetSearchPathFromOptions(string? options, out string? searchPath)
    {
        searchPath = null;
        if (string.IsNullOrWhiteSpace(options))
        {
            return true;
        }
        if (options.Contains('\\'))
        {
            return false;
        }

        var tokens = options.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            string setting;
            if (tokens[i] == "-c" && i + 1 < tokens.Length)
            {
                setting = tokens[++i];
            }
            else if (tokens[i].StartsWith("--", StringComparison.Ordinal) && tokens[i].Length > 2)
            {
                setting = tokens[i][2..];
            }
            else
            {
                return false;
            }

            var eq = setting.IndexOf('=');
            if (eq <= 0 || setting.StartsWith('-'))
            {
                return false;
            }
            if (setting[..eq].Replace('-', '_').Equals("search_path", StringComparison.OrdinalIgnoreCase))
            {
                searchPath = setting[(eq + 1)..];
            }
        }
        return true;
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
        if (required == null)
        {
            return true;
        }
        var parts = GetPublicParts(databaseType, connectionString);
        return parts.Any(pair => pair.Key == required) && !parts.Any(pair => pair.Key == UndeterminedKey);
    }

    /// <summary>Throws unless <see cref="IdentifiesDatabase" />; required before a cache is read or written.</summary>
    public static void EnsureIdentifiesDatabase(string databaseType, string connectionString)
    {
        if (!IdentifiesDatabase(databaseType, connectionString))
        {
            var undetermined = GetPublicParts(databaseType, connectionString).Any(pair => pair.Key == UndeterminedKey);
            throw new CliException(undetermined
                ? "The connection's Options (or the PGOPTIONS environment variable) cannot be parsed with certainty, " +
                  "so its cache cannot be identified. Use only '-c name=value' / '--name=value' switches without backslashes, " +
                  "or use --cache-mode off."
                : $"The {databaseType} connection string has no {(databaseType == "postgres" ? "Database (or DB)" : "AccountEndpoint")} value, " +
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
