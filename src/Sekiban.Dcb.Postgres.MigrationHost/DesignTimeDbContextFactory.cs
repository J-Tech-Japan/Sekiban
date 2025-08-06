using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Sekiban.Dcb.Postgres;

namespace Sekiban.Dcb.Postgres.MigrationHost;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SekibanDcbDbContext>
{
    public SekibanDcbDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("SekibanDcbConnection")
            ?? "Host=localhost;Database=sekiban_dcb;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<SekibanDcbDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new SekibanDcbDbContext(optionsBuilder.Options);
    }
}