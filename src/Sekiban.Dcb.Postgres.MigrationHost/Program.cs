using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sekiban.Dcb.Postgres;
var host = Host
    .CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        config.AddJsonFile("appsettings.json", false, true);
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        var connectionString = context.Configuration.GetConnectionString("SekibanDcbConnection") ??
            throw new InvalidOperationException("Connection string 'SekibanDcbConnection' not found.");

        services.AddDbContext<SekibanDcbDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });
    })
    .Build();

// Apply migrations
using (var scope = host.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<SekibanDcbDbContext>();

    Console.WriteLine("Applying database migrations...");
    await context.Database.MigrateAsync();
    Console.WriteLine("Database migrations completed successfully.");
}

Console.WriteLine("Migration host completed.");
