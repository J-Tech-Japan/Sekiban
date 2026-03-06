using Dcb.Domain.WithoutResult;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sekiban.Dcb.ColdEvents;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.Sqlite;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        var domainTypes = DomainType.GetDomainTypes();
        var databaseType = context.Configuration.GetSection("Sekiban").GetValue<string>("Database")?.ToLower() ?? "postgres";
        var coldEventEnabled = ResolveColdEventEnabled(context.Configuration);

        services.AddSingleton(domainTypes);

        if (databaseType == "sqlite")
        {
            var sqliteCacheDir = context.Configuration.GetValue<string>("Sekiban:SqliteCachePath") ??
                                 context.HostingEnvironment.ContentRootPath;
            var sqliteCachePath = Path.Combine(sqliteCacheDir, "events.db");
            services.AddSekibanDcbSqlite(sqliteCachePath);
        }
        else if (databaseType == "cosmos")
        {
            services.AddSekibanDcbCosmosDbWithAspire();
        }
        else if (databaseType == "postgres")
        {
            services.AddSekibanDcbPostgresWithAspire("DcbPostgres");
        }
        else
        {
            throw new InvalidOperationException(
                $"Unsupported Sekiban:Database '{databaseType}'. Supported values are sqlite, cosmos, postgres.");
        }

        services.AddSekibanDcbColdEventDefaults();
        if (coldEventEnabled)
        {
            services.AddSekibanDcbColdExport(
                context.Configuration,
                context.HostingEnvironment.ContentRootPath,
                addBackgroundService: false);
        }
    })
    .Build();

host.Run();

static bool ResolveColdEventEnabled(IConfiguration configuration)
{
    var coldConfig = configuration.GetSection("Sekiban:ColdEvent");
    var configuredOptions = coldConfig.Get<ColdEventStoreOptions>() ?? new ColdEventStoreOptions();
    return string.IsNullOrWhiteSpace(coldConfig["Enabled"]) || configuredOptions.Enabled;
}
