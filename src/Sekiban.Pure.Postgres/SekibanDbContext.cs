using Microsoft.EntityFrameworkCore;
namespace Sekiban.Pure.Postgres;

public class SekibanDbContext(DbContextOptions<SekibanDbContext> options) : DbContext(options)
{
    public DbSet<DbEvent> Events { get; set; } = default!;
    public string ConnectionString { get; init; } = string.Empty;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseNpgsql(ConnectionString);
    }
}