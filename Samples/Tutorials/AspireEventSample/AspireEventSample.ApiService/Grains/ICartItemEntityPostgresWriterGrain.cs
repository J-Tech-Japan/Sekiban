using AspireEventSample.ApiService.ReadModel;
using AspireEventSample.ReadModels;
namespace AspireEventSample.ApiService.Grains;

public interface ICartItemEntityPostgresWriterGrain : ICartItemEntityPostgresWriter, IGrainWithStringKey
{
}
public interface ICartItemEntityPostgresWriter : IReadModelAccessor<CartItemDbRecord>, IGrainWithStringKey
{
    Task<List<CartItemDbRecord>> GetItemsByCartIdAsync(Guid cartId);
}