using Microsoft.Extensions.Configuration;

namespace Sekiban.Pure.Postgres;

public record SekibanPostgresDbOption
{
    public const string EventsTableId = "events";
    public const string EventsTableIdDissolvable = "dissolvableevents";
    public const string PostgresConnectionStringNameDefaultValue = "SekibanPostgres";
    public const string ItemsTableId = "items";
    public const string ItemsTableIdDissolvable = "dissolvableitems";

    public bool MigrationFinished { get; set; } = false;

    public string ConnectionStringName { get; init; } = PostgresConnectionStringNameDefaultValue;
    public string? ConnectionString { get; init; }

    public static SekibanPostgresDbOption FromConfiguration(
        IConfigurationSection section,
        IConfigurationRoot configurationRoot)
    {
        var azureSection = section.GetSection("Postgres");
        var postgresConnectionStringName = azureSection.GetValue<string>(nameof(ConnectionStringName)) ??
                                           PostgresConnectionStringNameDefaultValue;
        var postgresConnectionString = configurationRoot.GetConnectionString(postgresConnectionStringName) ??
                                       section.GetValue<string>(nameof(ConnectionString)) ??
                                       section.GetValue<string>("PostgresConnectionString");
        return new SekibanPostgresDbOption
        {
            ConnectionStringName = postgresConnectionStringName,
            ConnectionString = postgresConnectionString
        };
    }

    public static SekibanPostgresDbOption FromConnectionStringName(
        string connectionStringName,
        IConfigurationRoot configurationRoot)
    {
        var postgresConnectionStringName = connectionStringName ?? PostgresConnectionStringNameDefaultValue;
        var postgresConnectionString = configurationRoot.GetConnectionString(postgresConnectionStringName);
        return new SekibanPostgresDbOption
        {
            ConnectionStringName = postgresConnectionStringName,
            ConnectionString = postgresConnectionString
        };
    }
}