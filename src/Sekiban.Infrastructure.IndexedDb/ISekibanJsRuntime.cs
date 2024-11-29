namespace Sekiban.Infrastructure.IndexedDb;

public interface ISekibanJsRuntime
{
    Task<ISekibanIndexedDbContext> CreateContextAsync(string context);
}
