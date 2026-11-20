namespace Dcb.Cli;

// A cache file found under the cache root; IsLegacy for the old <root>/events.db location.
readonly record struct CacheFile(string Path, bool IsLegacy);

// The resolved store a command talks to. For sqlite, ConnectionString is the database file path.
readonly record struct ConnectionTarget(string DatabaseType, string ConnectionString, string CosmosDatabaseName);

// Expected command failure: the message is printed to stderr and the CLI exits with code 1.
sealed class CliException(string message) : Exception(message);
