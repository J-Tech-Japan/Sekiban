using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Sekiban.Dcb.Postgres;

var relationalAssembly = Assembly.Load("Microsoft.EntityFrameworkCore.Relational");
var options = new DbContextOptionsBuilder<SekibanDcbDbContext>()
    .UseNpgsql("Host=127.0.0.1;Port=1;Database=g62-never-connect;Username=unused;Timeout=1;Command Timeout=1")
    .Options;

await using var db = new SekibanDcbDbContext(options);
var entityTypes = db.Model.GetEntityTypes().ToArray();
if (entityTypes.Length != 6)
{
    throw new InvalidOperationException($"G62 expected six model entities before any database connection, got {entityTypes.Length}");
}

Console.WriteLine($"G62 model entities: {entityTypes.Length}");
Console.WriteLine($"G62 relational assembly: {relationalAssembly.GetName().Name} {relationalAssembly.GetName().Version}");
Console.WriteLine($"G62 provider: {db.Database.ProviderName}");
