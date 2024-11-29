namespace Sekiban.Infrastructure.IndexedDb;

public interface ISekibanIndexedDbContext
{
    Task WriteEventAsync(DbEvent payload);
}
