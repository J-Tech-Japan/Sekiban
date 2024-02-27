using Microsoft.EntityFrameworkCore;
namespace Sekiban.Infrastructure.Postgres.Databases;

public class SekibanDbContext(DbContextOptions<SekibanDbContext> options) : DbContext(options)
{
    public DbSet<DbEvent> Events { get; set; } = default!;
    public DbSet<DbDissolvableEvent> DissolvableEvents { get; set; } = default!;
    public string ConnectionString { get; init; } = string.Empty;

    public DbSet<DbCommandDocument> Commands { get; set; } = default!;
    public DbSet<DbSingleProjectionSnapshotDocument> SingleProjectionSnapshots { get; set; } = default!;
    // public DbSet<DbItem> MultiProjectionSnapshots { get; set; } = default!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseNpgsql(ConnectionString);
    }
}
